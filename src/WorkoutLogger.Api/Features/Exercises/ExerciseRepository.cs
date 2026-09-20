using Dapper;
using WorkoutLogger.Api.Common;
using WorkoutLogger.Api.Persistence;

namespace WorkoutLogger.Api.Features.Exercises;

/// <remarks>
/// Init-only properties rather than a positional record: SQLite reports every
/// integer as INTEGER and every float as REAL, so Dapper's constructor binding
/// cannot match an int/bool/decimal parameter. Property setters convert.
/// </remarks>
public sealed record ExerciseSummary
{
    public long Id { get; init; }
    public string CanonicalName { get; init; } = string.Empty;
    public string? MuscleGroup { get; init; }
    public string? Modality { get; init; }
    public bool IsUnresolved { get; init; }
    public int TimesLogged { get; init; }
}

public enum MergeOutcome { Merged, SourceNotFound, TargetNotFound, SameExercise }

public sealed class ExerciseRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public ExerciseRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ExerciseSummary>> ListAsync(bool unresolvedOnly, CancellationToken ct)
    {
        using var connection = _connectionFactory.Create();

        var rows = await connection.QueryAsync<ExerciseSummary>(new CommandDefinition(
            ExerciseSql.List, new { unresolvedOnly = unresolvedOnly ? 1 : 0 }, cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<bool> AddAliasAsync(long exerciseId, string alias, CancellationToken ct)
    {
        var normalized = TextNormalizer.Normalize(alias);

        using var connection = _connectionFactory.Create();

        var exists = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            ExerciseSql.ExistsById, new { id = exerciseId }, cancellationToken: ct));

        if (exists is null)
        {
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            ExerciseSql.InsertAlias,
            new { exerciseId, normalized, alias = alias.Trim(), now = Clock.UtcNowIso() },
            cancellationToken: ct));

        return true;
    }

    /// <summary>
    /// Folds <paramref name="sourceId"/> into <paramref name="targetId"/>: existing
    /// logged data is repointed, the source name becomes an alias of the target, and
    /// the source row is deleted.
    /// </summary>
    /// <remarks>
    /// This is what makes the unresolved bucket recoverable rather than a dead end —
    /// adding an alias alone only fixes future imports, not the rows already stored.
    /// The awkward case is a workout that already contains both exercises, where a
    /// straight repoint would violate UNIQUE(WorkoutId, ExerciseId); there the source
    /// block's sets are appended to the target block and the empty block dropped.
    /// </remarks>
    public async Task<MergeOutcome> MergeAsync(long sourceId, long targetId, CancellationToken ct)
    {
        if (sourceId == targetId)
        {
            return MergeOutcome.SameExercise;
        }

        using var connection = _connectionFactory.Create();
        using var transaction = connection.BeginTransaction();

        var source = await connection.QuerySingleOrDefaultAsync<ExerciseNames>(new CommandDefinition(
            ExerciseSql.SelectNamesById, new { sourceId }, transaction, cancellationToken: ct));

        if (source is null)
        {
            return MergeOutcome.SourceNotFound;
        }

        var targetExists = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            ExerciseSql.ExistsById, new { id = targetId }, transaction, cancellationToken: ct));

        if (targetExists is null)
        {
            return MergeOutcome.TargetNotFound;
        }

        // 1. Workouts holding both: append the source sets to the target block,
        //    offsetting SetNumber past whatever is already there.
        await connection.ExecuteAsync(new CommandDefinition(
            ExerciseSql.MergeAppendSetsToTargetBlock,
            new { sourceId, targetId }, transaction, cancellationToken: ct));

        // 2. Drop the now-empty source blocks from those same workouts.
        await connection.ExecuteAsync(new CommandDefinition(
            ExerciseSql.MergeDeleteEmptySourceBlocks,
            new { sourceId, targetId }, transaction, cancellationToken: ct));

        // 3. Everything left can simply be repointed.
        await connection.ExecuteAsync(new CommandDefinition(
            ExerciseSql.MergeRepointRemainingBlocks,
            new { sourceId, targetId }, transaction, cancellationToken: ct));

        // 4. Carry the aliases over, then make the source's own name an alias so the
        //    spelling that caused this resolves correctly next time.
        await connection.ExecuteAsync(new CommandDefinition(
            ExerciseSql.MergeCarryAliasesAndDeleteSource,
            new
            {
                sourceId,
                targetId,
                normalizedName = source.NormalizedName,
                canonicalName = source.CanonicalName,
                now = Clock.UtcNowIso()
            },
            transaction, cancellationToken: ct));

        transaction.Commit();
        return MergeOutcome.Merged;
    }

    private sealed record ExerciseNames(string CanonicalName, string NormalizedName);
}

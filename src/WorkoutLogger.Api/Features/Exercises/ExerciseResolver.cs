using Dapper;
using Microsoft.Data.Sqlite;
using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Features.Exercises;

public sealed record ResolvedExercise(long ExerciseId, string CanonicalName, bool WasCreatedUnresolved);

/// <summary>
/// Maps a free-text exercise name onto a canonical Exercise row.
/// </summary>
/// <remarks>
/// Lookup order is alias, then canonical name, then create-as-unresolved. The
/// fallback is deliberate: an unrecognised name still gets its data stored and
/// shows up in <c>GET /api/exercises/unresolved</c> to be aliased later, rather
/// than failing the import and losing the entry.
///
/// Instantiated per import so the cache is scoped to one request/transaction.
/// </remarks>
public sealed class ExerciseResolver
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction? _transaction;
    private readonly Dictionary<string, ResolvedExercise> _cache = new(StringComparer.Ordinal);

    public ExerciseResolver(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        _connection = connection;
        _transaction = transaction;
    }

    public async Task<ResolvedExercise> ResolveAsync(string rawName, CancellationToken ct)
    {
        var normalized = TextNormalizer.Normalize(rawName);

        if (normalized.Length == 0)
        {
            throw new ArgumentException("Exercise name normalized to an empty string.", nameof(rawName));
        }

        if (_cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        var resolved = await LookupAsync(normalized, ct)
                       ?? await CreateUnresolvedAsync(rawName.Trim(), normalized, ct);

        _cache[normalized] = resolved;
        return resolved;
    }

    private async Task<ResolvedExercise?> LookupAsync(string normalized, CancellationToken ct)
    {
        var row = await _connection.QuerySingleOrDefaultAsync<ExerciseRow>(new CommandDefinition(
            ExerciseSql.ResolveByAliasOrName, new { normalized }, _transaction, cancellationToken: ct));

        return row is null ? null : new ResolvedExercise(row.Id, row.CanonicalName, false);
    }

    private sealed record ExerciseRow(long Id, string CanonicalName);

    private async Task<ResolvedExercise> CreateUnresolvedAsync(string rawName, string normalized, CancellationToken ct)
    {
        var id = await _connection.ExecuteScalarAsync<long>(new CommandDefinition(
            ExerciseSql.InsertUnresolved,
            new { rawName, normalized, createdAtUtc = Clock.UtcNowIso() },
            _transaction,
            cancellationToken: ct));

        return new ResolvedExercise(id, rawName, true);
    }
}

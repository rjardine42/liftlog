using Dapper;
using WorkoutLogger.Api.Persistence;

namespace WorkoutLogger.Api.Features.Workouts;

/// <remarks>
/// Init-only properties rather than a positional record: SQLite reports every
/// integer as INTEGER and every float as REAL, so Dapper's constructor binding
/// cannot match an int/bool/decimal parameter. Property setters convert.
/// </remarks>
public sealed record WorkoutSummary
{
    public long Id { get; init; }
    public string PerformedOn { get; init; } = string.Empty;
    public string? Label { get; init; }
    public string? Notes { get; init; }
    public int ExerciseCount { get; init; }
    public int SetCount { get; init; }
}

public sealed record WorkoutDetail(long Id, string PerformedOn, string? Label, string? Notes, string SourceName, IReadOnlyList<ExerciseBlock> Exercises);

public sealed record ExerciseBlock(long ExerciseId, string Exercise, string RawName, int Position, string? Notes, IReadOnlyList<SetRow> Sets);

public sealed record SetRow(int SetNumber, int Reps, decimal? Weight, string Unit, decimal? Rpe, bool IsWarmup);

/// <summary>One row per set, flattened for charting and export.</summary>
/// <remarks>
/// Init-only properties rather than a positional record: SQLite reports every
/// integer as INTEGER and every float as REAL, so Dapper's constructor binding
/// cannot match an int/bool/decimal parameter. Property setters convert.
/// </remarks>
public sealed record FlatSet
{
    public string PerformedOn { get; init; } = string.Empty;
    public long WorkoutId { get; init; }
    public long ExerciseId { get; init; }
    public string Exercise { get; init; } = string.Empty;
    public int SetNumber { get; init; }
    public int Reps { get; init; }
    public decimal? Weight { get; init; }
    public string Unit { get; init; } = string.Empty;
    public decimal? Rpe { get; init; }
    public bool IsWarmup { get; init; }
}

public sealed class WorkoutRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public WorkoutRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WorkoutSummary>> ListAsync(
        DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
    {
        const string sql =
            """
            SELECT w.Id, w.PerformedOn, w.Label, w.Notes,
                   COUNT(DISTINCT we.Id) AS ExerciseCount,
                   COUNT(s.Id)           AS SetCount
            FROM Workout w
            LEFT JOIN WorkoutExercise we ON we.WorkoutId = w.Id
            LEFT JOIN WorkoutSet s       ON s.WorkoutExerciseId = we.Id
            WHERE (@from IS NULL OR w.PerformedOn >= @from)
              AND (@to   IS NULL OR w.PerformedOn <= @to)
            GROUP BY w.Id
            ORDER BY w.PerformedOn DESC, w.Id DESC
            LIMIT @pageSize OFFSET @offset;
            """;

        using var connection = _connectionFactory.Create();

        var rows = await connection.QueryAsync<WorkoutSummary>(new CommandDefinition(
            sql,
            new { from = Iso(from), to = Iso(to), pageSize, offset = (page - 1) * pageSize },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task<WorkoutDetail?> GetAsync(long id, CancellationToken ct)
    {
        using var connection = _connectionFactory.Create();

        var header = await connection.QuerySingleOrDefaultAsync<WorkoutHeader>(new CommandDefinition(
            "SELECT Id, PerformedOn, Label, Notes, SourceName FROM Workout WHERE Id = @id;",
            new { id }, cancellationToken: ct));

        if (header is null)
        {
            return null;
        }

        const string sql =
            """
            SELECT we.ExerciseId, e.CanonicalName AS Exercise, we.RawExerciseName AS RawName,
                   we.Position, we.Notes,
                   s.SetNumber, s.Reps, s.Weight, s.WeightUnit AS Unit, s.Rpe, s.IsWarmup
            FROM WorkoutExercise we
            JOIN Exercise e    ON e.Id = we.ExerciseId
            LEFT JOIN WorkoutSet s ON s.WorkoutExerciseId = we.Id
            WHERE we.WorkoutId = @id
            ORDER BY we.Position, s.SetNumber;
            """;

        var rows = await connection.QueryAsync<BlockRow>(new CommandDefinition(sql, new { id }, cancellationToken: ct));

        var exercises = rows
            .GroupBy(r => new { r.ExerciseId, r.Exercise, r.RawName, r.Position, r.Notes })
            .Select(g => new ExerciseBlock(
                g.Key.ExerciseId, g.Key.Exercise, g.Key.RawName, g.Key.Position, g.Key.Notes,
                g.Where(r => r.SetNumber.HasValue)
                 .Select(r => new SetRow(r.SetNumber!.Value, r.Reps!.Value, r.Weight, r.Unit!, r.Rpe, r.IsWarmup))
                 .ToList()))
            .ToList();

        return new WorkoutDetail(header.Id, header.PerformedOn, header.Label, header.Notes, header.SourceName, exercises);
    }

    public async Task<IReadOnlyList<FlatSet>> ListSetsAsync(
        long? exerciseId, DateOnly? from, DateOnly? to, bool includeWarmups, CancellationToken ct)
    {
        const string sql =
            """
            SELECT w.PerformedOn, w.Id AS WorkoutId, we.ExerciseId, e.CanonicalName AS Exercise,
                   s.SetNumber, s.Reps, s.Weight, s.WeightUnit AS Unit, s.Rpe, s.IsWarmup
            FROM WorkoutSet s
            JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
            JOIN Workout w          ON w.Id = we.WorkoutId
            JOIN Exercise e         ON e.Id = we.ExerciseId
            WHERE (@exerciseId IS NULL OR we.ExerciseId = @exerciseId)
              AND (@from IS NULL OR w.PerformedOn >= @from)
              AND (@to   IS NULL OR w.PerformedOn <= @to)
              AND (@includeWarmups = 1 OR s.IsWarmup = 0)
            ORDER BY w.PerformedOn, w.Id, we.Position, s.SetNumber;
            """;

        using var connection = _connectionFactory.Create();

        var rows = await connection.QueryAsync<FlatSet>(new CommandDefinition(
            sql,
            new { exerciseId, from = Iso(from), to = Iso(to), includeWarmups = includeWarmups ? 1 : 0 },
            cancellationToken: ct));

        return rows.ToList();
    }

    private static string? Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd");

    private sealed record WorkoutHeader(long Id, string PerformedOn, string? Label, string? Notes, string SourceName);

    private sealed record BlockRow
    {
        public long ExerciseId { get; init; }
        public string Exercise { get; init; } = string.Empty;
        public string RawName { get; init; } = string.Empty;
        public int Position { get; init; }
        public string? Notes { get; init; }
        public int? SetNumber { get; init; }
        public int? Reps { get; init; }
        public decimal? Weight { get; init; }
        public string? Unit { get; init; }
        public decimal? Rpe { get; init; }
        public bool IsWarmup { get; init; }
    }
}

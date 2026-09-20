using Dapper;
using Microsoft.Data.Sqlite;

namespace WorkoutLogger.Api.Features.Analytics.Metrics;

/// <summary>
/// Best estimated one-rep max per bucket, via the Epley formula
/// (<c>weight x (1 + reps / 30)</c>).
/// </summary>
/// <remarks>
/// The reference implementation of <see cref="IWorkoutMetric"/> — it exists to
/// prove the seam with something real. Warmups are excluded and bodyweight sets
/// (no weight recorded) are ignored, since neither says anything about a maximum.
/// </remarks>
public sealed class EstimatedOneRepMaxMetric : IWorkoutMetric
{
    public string Key => "e1rm";

    public string DisplayName => "Estimated 1RM";

    public string Unit => "lb";

    public async Task<MetricSeries> ComputeAsync(MetricQuery query, SqliteConnection connection, CancellationToken ct)
    {
        var sql =
            $"""
             SELECT {BucketSql(query.Bucket)} AS Bucket,
                    MAX(s.Weight * (1.0 + s.Reps / 30.0)) AS Value,
                    COUNT(*)                              AS SampleCount
             FROM WorkoutSet s
             JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
             JOIN Workout w          ON w.Id = we.WorkoutId
             WHERE s.IsWarmup = 0
               AND s.Weight IS NOT NULL
               AND (@exerciseId IS NULL OR we.ExerciseId = @exerciseId)
               AND (@from IS NULL OR w.PerformedOn >= @from)
               AND (@to   IS NULL OR w.PerformedOn <= @to)
             GROUP BY Bucket
             ORDER BY Bucket;
             """;

        var rows = await connection.QueryAsync<MetricPoint>(new CommandDefinition(
            sql,
            new
            {
                exerciseId = query.ExerciseId,
                from = query.From?.ToString("yyyy-MM-dd"),
                to = query.To?.ToString("yyyy-MM-dd")
            },
            cancellationToken: ct));

        return new MetricSeries(Key, Unit, rows.ToList());
    }

    /// <summary>
    /// Bucket start as an ISO date. Weeks start Monday: SQLite's %w is
    /// Sunday-based, so shift by 6 days and take the preceding Monday.
    /// </summary>
    private static string BucketSql(MetricBucket bucket) => bucket switch
    {
        MetricBucket.Day => "w.PerformedOn",
        MetricBucket.Week => "date(w.PerformedOn, '-6 days', 'weekday 1')",
        MetricBucket.Month => "strftime('%Y-%m-01', w.PerformedOn)",
        _ => "w.PerformedOn"
    };
}

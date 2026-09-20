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
        var sql = string.Format(AnalyticsSql.EstimatedOneRepMax, BucketSql(query.Bucket));

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

    /// <summary>Bucket start as an ISO date.</summary>
    private static string BucketSql(MetricBucket bucket) => bucket switch
    {
        MetricBucket.Day => AnalyticsSql.BucketDay,
        MetricBucket.Week => AnalyticsSql.BucketWeek,
        MetricBucket.Month => AnalyticsSql.BucketMonth,
        _ => AnalyticsSql.BucketDay
    };
}

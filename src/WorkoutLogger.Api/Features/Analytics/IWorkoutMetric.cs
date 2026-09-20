using Microsoft.Data.Sqlite;

namespace WorkoutLogger.Api.Features.Analytics;

public enum MetricBucket { Day, Week, Month }

public sealed record MetricQuery(long? ExerciseId, DateOnly? From, DateOnly? To, MetricBucket Bucket);

/// <remarks>
/// Init-only properties rather than a positional record: SQLite reports every
/// integer as INTEGER and every float as REAL, so Dapper's constructor binding
/// cannot match an int/bool/decimal parameter. Property setters convert.
/// </remarks>
public sealed record MetricPoint
{
    /// <summary>Bucket start as an ISO date.</summary>
    public string Bucket { get; init; } = string.Empty;

    public decimal Value { get; init; }

    /// <summary>How many sets fed this point — lets a caller tell a trend from a single lucky set.</summary>
    public int SampleCount { get; init; }
}

public sealed record MetricSeries(string Key, string Unit, IReadOnlyList<MetricPoint> Points);

/// <summary>
/// One computed view over the stored sets.
/// </summary>
/// <remarks>
/// The extension seam for analytics. Every metric returns the same
/// <see cref="MetricSeries"/> shape and is reached through the same route, so
/// adding volume, RPE-vs-load or PRs later means writing one class and one
/// registration line — no new endpoint, no change to any caller.
/// </remarks>
public interface IWorkoutMetric
{
    /// <summary>URL segment for this metric, e.g. "e1rm".</summary>
    string Key { get; }

    string DisplayName { get; }

    /// <summary>Unit of the computed value, for axis labelling.</summary>
    string Unit { get; }

    Task<MetricSeries> ComputeAsync(MetricQuery query, SqliteConnection connection, CancellationToken ct);
}

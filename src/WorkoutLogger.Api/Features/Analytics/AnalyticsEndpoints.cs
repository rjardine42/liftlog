using WorkoutLogger.Api.Persistence;

namespace WorkoutLogger.Api.Features.Analytics;

public static class AnalyticsEndpoints
{
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/analytics").WithTags("Analytics");

        group.MapGet("/metrics", (MetricRegistry registry) => registry.Describe())
            .WithSummary("List the metrics this server can compute.");

        group.MapGet("/{key}", ComputeAsync)
            .WithSummary("Compute one metric as a time series.");
    }

    private static async Task<IResult> ComputeAsync(
        string key,
        MetricRegistry registry,
        IDbConnectionFactory connectionFactory,
        long? exerciseId, DateOnly? from, DateOnly? to, string? bucket,
        CancellationToken ct)
    {
        if (registry.Find(key) is not { } metric)
        {
            return Results.NotFound(new
            {
                error = $"Unknown metric '{key}'.",
                available = registry.Describe().Select(m => m.Key)
            });
        }

        if (!TryParseBucket(bucket, out var parsedBucket))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["bucket"] = ["bucket must be one of: day, week, month."]
            });
        }

        using var connection = connectionFactory.Create();
        var query = new MetricQuery(exerciseId, from, to, parsedBucket);

        return Results.Ok(await metric.ComputeAsync(query, connection, ct));
    }

    private static bool TryParseBucket(string? value, out MetricBucket bucket)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            bucket = MetricBucket.Week;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out bucket) && Enum.IsDefined(bucket);
    }
}

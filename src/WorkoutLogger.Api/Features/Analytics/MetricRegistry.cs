namespace WorkoutLogger.Api.Features.Analytics;

public sealed record MetricDescriptor(string Key, string DisplayName, string Unit);

public sealed class MetricRegistry
{
    private readonly Dictionary<string, IWorkoutMetric> _metrics;

    public MetricRegistry(IEnumerable<IWorkoutMetric> metrics)
    {
        _metrics = metrics.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);
    }

    public IWorkoutMetric? Find(string key) =>
        _metrics.TryGetValue(key, out var metric) ? metric : null;

    public IReadOnlyList<MetricDescriptor> Describe() =>
        _metrics.Values
            .Select(m => new MetricDescriptor(m.Key, m.DisplayName, m.Unit))
            .OrderBy(m => m.Key, StringComparer.Ordinal)
            .ToList();
}

public static class MetricRegistrationExtensions
{
    /// <summary>Registers a metric so it appears at /api/analytics/{key}.</summary>
    public static IServiceCollection AddWorkoutMetric<TMetric>(this IServiceCollection services)
        where TMetric : class, IWorkoutMetric
        => services.AddSingleton<IWorkoutMetric, TMetric>();
}

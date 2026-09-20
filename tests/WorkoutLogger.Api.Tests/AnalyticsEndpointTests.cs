using System.Net;
using System.Net.Http.Json;
using WorkoutLogger.Api.Features.Analytics;
using WorkoutLogger.Api.Features.Import;
using static WorkoutLogger.Api.Tests.Payloads;

namespace WorkoutLogger.Api.Tests;

public sealed class AnalyticsEndpointTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;

    public AnalyticsEndpointTests(ApiFactory factory) => _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        // 2026-01-05 and 2026-01-07 fall in the same Monday-start week; 2026-01-12 is the next.
        var request = new ImportBatchRequest
        {
            Workouts =
            [
                Workout("2026-01-05", Exercise("Barbell Bench Press",
                    Set(10, 500, isWarmup: true),  // excluded: warmup
                    Set(8, 185))),                 // 185 * (1 + 8/30)  = 234.33
                Workout("2026-01-07", Exercise("Barbell Bench Press",
                    Set(3, 225))),                 // 225 * (1 + 3/30)  = 247.50
                Workout("2026-01-12", Exercise("Barbell Bench Press",
                    Set(5, 205)))                  // 205 * (1 + 5/30)  = 239.17
            ]
        };

        (await _client.PostAsJsonAsync("/api/workouts/batch", request)).EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metrics_lists_what_the_server_can_compute()
    {
        var metrics = await _client.GetFromJsonAsync<List<MetricDescriptor>>("/api/analytics/metrics");

        var e1rm = Assert.Single(metrics!, m => m.Key == "e1rm");
        Assert.Equal("Estimated 1RM", e1rm.DisplayName);
        Assert.Equal("lb", e1rm.Unit);
    }

    [Fact]
    public async Task E1rm_buckets_by_week_and_takes_the_best_working_set()
    {
        var series = await _client.GetFromJsonAsync<MetricSeries>("/api/analytics/e1rm?bucket=week");

        Assert.Equal(2, series!.Points.Count);

        Assert.Equal("2026-01-05", series.Points[0].Bucket);
        Assert.Equal(247.5m, Math.Round(series.Points[0].Value, 2));
        Assert.Equal(2, series.Points[0].SampleCount); // the 500lb warmup is not counted

        Assert.Equal("2026-01-12", series.Points[1].Bucket);
        Assert.Equal(239.17m, Math.Round(series.Points[1].Value, 2));
    }

    [Fact]
    public async Task E1rm_can_bucket_by_day_and_by_month()
    {
        var daily = await _client.GetFromJsonAsync<MetricSeries>("/api/analytics/e1rm?bucket=day");
        Assert.Equal(["2026-01-05", "2026-01-07", "2026-01-12"], daily!.Points.Select(p => p.Bucket));

        var monthly = await _client.GetFromJsonAsync<MetricSeries>("/api/analytics/e1rm?bucket=month");
        Assert.Equal("2026-01-01", Assert.Single(monthly!.Points).Bucket);
    }

    [Fact]
    public async Task E1rm_respects_the_date_range()
    {
        var series = await _client.GetFromJsonAsync<MetricSeries>("/api/analytics/e1rm?bucket=day&from=2026-01-12");

        Assert.Equal("2026-01-12", Assert.Single(series!.Points).Bucket);
    }

    [Fact]
    public async Task An_unknown_metric_returns_404_and_names_the_ones_that_exist()
    {
        var response = await _client.GetAsync("/api/analytics/nonsense");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("e1rm", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unrecognised_bucket_is_a_validation_error()
    {
        var response = await _client.GetAsync("/api/analytics/e1rm?bucket=fortnight");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

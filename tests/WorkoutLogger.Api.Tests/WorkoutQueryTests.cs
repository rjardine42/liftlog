using System.Net;
using System.Net.Http.Json;
using WorkoutLogger.Api.Features.Import;
using WorkoutLogger.Api.Features.Workouts;
using static WorkoutLogger.Api.Tests.Payloads;

namespace WorkoutLogger.Api.Tests;

public sealed class WorkoutQueryTests : IClassFixture<ApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;

    public WorkoutQueryTests(ApiFactory factory) => _client = factory.CreateClient();

    public async Task InitializeAsync()
    {
        var request = new ImportBatchRequest
        {
            Workouts =
            [
                Workout("2026-01-05",
                    Exercise("Barbell Bench Press", Set(10, 95, isWarmup: true), Set(8, 185, 7.5m), Set(6, 195, 9m)),
                    Exercise("Barbell Row", Set(10, 135))),
                Workout("2026-01-07", Exercise("Barbell Bench Press", Set(8, 190, 8m)))
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/workouts/batch", request);
        response.EnsureSuccessStatusCode();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_returns_workouts_newest_first_with_counts()
    {
        var workouts = await _client.GetFromJsonAsync<List<WorkoutSummary>>("/api/workouts");

        Assert.Equal(2, workouts!.Count);
        Assert.Equal("2026-01-07", workouts[0].PerformedOn);

        var first = workouts.Single(w => w.PerformedOn == "2026-01-05");
        Assert.Equal(2, first.ExerciseCount);
        Assert.Equal(4, first.SetCount);
    }

    [Fact]
    public async Task List_filters_by_date_range()
    {
        var workouts = await _client.GetFromJsonAsync<List<WorkoutSummary>>("/api/workouts?from=2026-01-06");

        Assert.Equal("2026-01-07", Assert.Single(workouts!).PerformedOn);
    }

    [Fact]
    public async Task Detail_nests_exercises_and_their_sets_in_order()
    {
        var workouts = await _client.GetFromJsonAsync<List<WorkoutSummary>>("/api/workouts?from=2026-01-05&to=2026-01-05");
        var id = Assert.Single(workouts!).Id;

        var detail = await _client.GetFromJsonAsync<WorkoutDetail>($"/api/workouts/{id}");

        Assert.Equal(2, detail!.Exercises.Count);
        Assert.Equal("Barbell Bench Press", detail.Exercises[0].Exercise);
        Assert.Equal("Barbell Row", detail.Exercises[1].Exercise);

        var bench = detail.Exercises[0];
        Assert.Equal([1, 2, 3], bench.Sets.Select(s => s.SetNumber));
        Assert.True(bench.Sets[0].IsWarmup);
        Assert.Equal(9m, bench.Sets[2].Rpe);
        Assert.Equal("lb", bench.Sets[2].Unit);
    }

    [Fact]
    public async Task Detail_returns_404_for_a_workout_that_does_not_exist()
    {
        var response = await _client.GetAsync("/api/workouts/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Sets_excludes_warmups_unless_asked_for()
    {
        var working = await _client.GetFromJsonAsync<List<FlatSet>>("/api/sets");
        var all = await _client.GetFromJsonAsync<List<FlatSet>>("/api/sets?includeWarmups=true");

        Assert.Equal(4, working!.Count);
        Assert.Equal(5, all!.Count);
        Assert.DoesNotContain(working, s => s.IsWarmup);
    }

    [Fact]
    public async Task Sets_can_be_narrowed_to_one_exercise_and_come_back_in_date_order()
    {
        var all = await _client.GetFromJsonAsync<List<FlatSet>>("/api/sets");
        var benchId = all!.First(s => s.Exercise == "Barbell Bench Press").ExerciseId;

        var bench = await _client.GetFromJsonAsync<List<FlatSet>>($"/api/sets?exerciseId={benchId}");

        Assert.Equal(3, bench!.Count);
        Assert.All(bench, s => Assert.Equal("Barbell Bench Press", s.Exercise));
        Assert.Equal(["2026-01-05", "2026-01-05", "2026-01-07"], bench.Select(s => s.PerformedOn));
        Assert.Equal(185m, bench[0].Weight);
    }
}

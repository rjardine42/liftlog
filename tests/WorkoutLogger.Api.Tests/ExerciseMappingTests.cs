using System.Net;
using System.Net.Http.Json;
using Dapper;
using WorkoutLogger.Api.Features.Exercises;
using WorkoutLogger.Api.Features.Import;
using WorkoutLogger.Api.Features.Workouts;
using static WorkoutLogger.Api.Tests.Payloads;

namespace WorkoutLogger.Api.Tests;

/// <summary>
/// Covers the path out of the unresolved bucket: teaching the catalogue a new
/// spelling, and folding a mistaken entry into the exercise it should have been.
/// </summary>
public sealed class ExerciseMappingTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ExerciseMappingTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task An_added_alias_makes_later_imports_resolve_to_the_canonical_exercise()
    {
        var benchId = await ExerciseIdAsync("Barbell Bench Press");

        var response = await _client.PostAsJsonAsync(
            $"/api/exercises/{benchId}/aliases", new AddAliasRequest("Comp Bench"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var result = await ImportAsync(Workout("2026-01-20", Exercise("comp bench", Set(5, 225))));

        Assert.Empty(result.UnresolvedExercises);
        Assert.Equal(benchId, await ExerciseIdOfWorkoutAsync("2026-01-20"));
    }

    [Fact]
    public async Task Adding_an_alias_to_a_missing_exercise_is_a_404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/exercises/999999/aliases", new AddAliasRequest("Whatever"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_empty_alias_is_rejected()
    {
        var benchId = await ExerciseIdAsync("Barbell Bench Press");

        var response = await _client.PostAsJsonAsync(
            $"/api/exercises/{benchId}/aliases", new AddAliasRequest("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Merging_repoints_already_logged_data_and_retires_the_source()
    {
        var import = await ImportAsync(Workout("2026-02-10", Exercise("Pendlay Row", Set(8, 135), Set(8, 145))));
        var sourceId = Assert.Single(import.UnresolvedExercises).ExerciseId;
        var targetId = await ExerciseIdAsync("Barbell Row");

        var response = await _client.PostAsync($"/api/exercises/{sourceId}/merge-into/{targetId}", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The sets already logged now belong to the canonical exercise...
        Assert.Equal(targetId, await ExerciseIdOfWorkoutAsync("2026-02-10"));

        // ...the source row is gone...
        using var connection = _factory.OpenConnection();
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Exercise WHERE Id = @sourceId;", new { sourceId }));

        // ...and the spelling that caused it now resolves on its own.
        await ImportAsync(Workout("2026-02-12", Exercise("pendlay row", Set(8, 155))));
        Assert.Equal(targetId, await ExerciseIdOfWorkoutAsync("2026-02-12"));
    }

    [Fact]
    public async Task Merging_into_an_exercise_the_same_workout_already_has_combines_the_sets()
    {
        // The awkward case: a straight repoint here would break UNIQUE(WorkoutId, ExerciseId).
        // A name unique to this test, since the class shares one database.
        var import = await ImportAsync(Workout("2026-03-15",
            Exercise("Barbell Row", Set(8, 135), Set(8, 140)),
            Exercise("Seal Row", Set(6, 155), Set(6, 160))));

        var sourceId = Assert.Single(import.UnresolvedExercises).ExerciseId;
        var targetId = await ExerciseIdAsync("Barbell Row");

        var response = await _client.PostAsync($"/api/exercises/{sourceId}/merge-into/{targetId}", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var workouts = await _client.GetFromJsonAsync<List<WorkoutSummary>>("/api/workouts?from=2026-03-15&to=2026-03-15");
        var detail = await _client.GetFromJsonAsync<WorkoutDetail>($"/api/workouts/{workouts!.Single().Id}");

        var block = Assert.Single(detail!.Exercises);
        Assert.Equal("Barbell Row", block.Exercise);

        // All four sets survive, renumbered so the appended ones follow on.
        Assert.Equal([1, 2, 3, 4], block.Sets.Select(s => s.SetNumber));
        Assert.Equal([135m, 140m, 155m, 160m], block.Sets.Select(s => s.Weight));
    }

    [Fact]
    public async Task Merging_an_exercise_into_itself_is_rejected()
    {
        var benchId = await ExerciseIdAsync("Barbell Bench Press");

        var response = await _client.PostAsync($"/api/exercises/{benchId}/merge-into/{benchId}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(999999, false)]
    [InlineData(0, true)]
    public async Task Merging_with_a_missing_exercise_is_a_404(long missingId, bool missingIsTarget)
    {
        var benchId = await ExerciseIdAsync("Barbell Bench Press");

        var (source, target) = missingIsTarget ? (benchId, 999999L) : (missingId, benchId);
        var response = await _client.PostAsync($"/api/exercises/{source}/merge-into/{target}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<ImportResult> ImportAsync(WorkoutDto workout)
    {
        var response = await _client.PostAsJsonAsync("/api/workouts", workout);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private async Task<long> ExerciseIdAsync(string canonicalName)
    {
        using var connection = _factory.OpenConnection();

        return await connection.ExecuteScalarAsync<long>(
            "SELECT Id FROM Exercise WHERE CanonicalName = @canonicalName;", new { canonicalName });
    }

    private async Task<long> ExerciseIdOfWorkoutAsync(string performedOn)
    {
        using var connection = _factory.OpenConnection();

        return await connection.ExecuteScalarAsync<long>(
            """
            SELECT we.ExerciseId FROM WorkoutExercise we
            JOIN Workout w ON w.Id = we.WorkoutId
            WHERE w.PerformedOn = @performedOn;
            """,
            new { performedOn });
    }
}

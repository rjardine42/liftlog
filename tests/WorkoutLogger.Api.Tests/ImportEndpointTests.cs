using System.Net;
using System.Net.Http.Json;
using Dapper;
using WorkoutLogger.Api.Features.Import;
using static WorkoutLogger.Api.Tests.Payloads;

namespace WorkoutLogger.Api.Tests;

/// <remarks>
/// Every test uses its own PerformedOn date. The class shares one database, and
/// xunit does not guarantee test order, so distinct dates keep the assertions
/// independent of what else has run.
/// </remarks>
public sealed class ImportEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ImportEndpointTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Single_workout_persists_its_exercises_and_sets()
    {
        var workout = Workout("2026-01-05",
            Exercise("Barbell Bench Press", Set(8, 185, 7.5m), Set(6, 195, 9m)),
            Exercise("Barbell Row", Set(10, 135)));

        var result = await PostWorkoutAsync(workout);

        Assert.Equal(1, result.WorkoutsCreated);
        Assert.Equal(0, result.WorkoutsMatched);
        Assert.Equal(2, result.ExercisesImported);
        Assert.Equal(0, result.ExercisesSkipped);
        Assert.Equal(3, await CountSetsOnAsync("2026-01-05"));
    }

    [Fact]
    public async Task Batch_imports_every_workout()
    {
        var request = new ImportBatchRequest
        {
            Source = "test",
            Workouts =
            [
                Workout("2026-02-02", Exercise("Squat", Set(5, 225))),
                Workout("2026-02-04", Exercise("Deadlift", Set(3, 315))),
                Workout("2026-02-06", Exercise("OHP", Set(8, 95)))
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/workouts/batch", request);
        var result = await ReadResultAsync(response);

        Assert.Equal(3, result.WorkoutsReceived);
        Assert.Equal(3, result.WorkoutsCreated);
        Assert.Equal(3, result.ExercisesImported);
    }

    [Fact]
    public async Task Reimporting_the_same_workout_skips_instead_of_duplicating()
    {
        var workout = Workout("2026-03-10", Exercise("Barbell Bench Press", Set(8, 185)));

        await PostWorkoutAsync(workout);
        var second = await PostWorkoutAsync(workout);

        Assert.Equal(0, second.ExercisesImported);
        Assert.Equal(1, second.ExercisesSkipped);
        Assert.Equal(1, second.WorkoutsMatched);
        Assert.Equal("Already logged for this date.", second.Skipped[0].Reason);

        // The skip must be a no-op, not a second copy of the sets.
        Assert.Equal(1, await CountSetsOnAsync("2026-03-10"));
    }

    [Fact]
    public async Task Same_date_different_exercises_land_in_one_workout()
    {
        await PostWorkoutAsync(Workout("2026-04-01", Exercise("Barbell Bench Press", Set(8, 185))));
        var second = await PostWorkoutAsync(Workout("2026-04-01", Exercise("Barbell Row", Set(10, 135))));

        Assert.Equal(0, second.WorkoutsCreated);
        Assert.Equal(1, second.WorkoutsMatched);
        Assert.Equal(1, second.ExercisesImported);

        using var connection = _factory.OpenConnection();

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Workout WHERE PerformedOn = '2026-04-01';"));

        Assert.Equal(2, await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM WorkoutExercise we
            JOIN Workout w ON w.Id = we.WorkoutId
            WHERE w.PerformedOn = '2026-04-01';
            """));

        // Appending must continue the ordering rather than restart it.
        Assert.Equal("1,2", await connection.ExecuteScalarAsync<string>(
            """
            SELECT GROUP_CONCAT(Position) FROM (
                SELECT we.Position FROM WorkoutExercise we
                JOIN Workout w ON w.Id = we.WorkoutId
                WHERE w.PerformedOn = '2026-04-01' ORDER BY we.Position);
            """));
    }

    [Fact]
    public async Task Aliases_and_canonical_names_resolve_to_the_same_exercise()
    {
        await PostWorkoutAsync(Workout("2026-05-01", Exercise("bench", Set(8, 185))));
        await PostWorkoutAsync(Workout("2026-05-03", Exercise("Barbell Bench Press", Set(8, 190))));

        using var connection = _factory.OpenConnection();

        var distinctExercises = await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(DISTINCT we.ExerciseId) FROM WorkoutExercise we
            JOIN Workout w ON w.Id = we.WorkoutId
            WHERE w.PerformedOn IN ('2026-05-01', '2026-05-03');
            """);

        Assert.Equal(1, distinctExercises);
    }

    [Fact]
    public async Task Unknown_exercise_is_stored_as_unresolved_and_reported()
    {
        var result = await PostWorkoutAsync(
            Workout("2026-06-01", Exercise("Jefferson Curl", Set(10, 45))));

        Assert.Equal(1, result.ExercisesImported);
        var unresolved = Assert.Single(result.UnresolvedExercises);
        Assert.Equal("Jefferson Curl", unresolved.Name);

        using var connection = _factory.OpenConnection();

        Assert.Equal(1, await connection.ExecuteScalarAsync<int>(
            "SELECT IsUnresolved FROM Exercise WHERE Id = @id;", new { id = unresolved.ExerciseId }));

        var listed = await _client.GetFromJsonAsync<List<UnresolvedRow>>("/api/exercises/unresolved");
        Assert.Contains(listed!, e => e.Id == unresolved.ExerciseId);
    }

    [Fact]
    public async Task Exercise_level_rpe_fills_only_the_sets_that_omit_their_own()
    {
        await PostWorkoutAsync(Workout("2026-07-01",
            Exercise("Leg Press", rpe: 8m, Set(10, 300), Set(10, 300, rpe: 9.5m))));

        using var connection = _factory.OpenConnection();

        var rpes = (await connection.QueryAsync<decimal?>(
            """
            SELECT s.Rpe FROM WorkoutSet s
            JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
            JOIN Workout w ON w.Id = we.WorkoutId
            WHERE w.PerformedOn = '2026-07-01' ORDER BY s.SetNumber;
            """)).ToList();

        Assert.Equal([8m, 9.5m], rpes);
    }

    [Theory]
    [InlineData(11, 8, 185, "lb", "rpe")]
    [InlineData(8, 0, 185, "lb", "reps")]
    [InlineData(8, 8, -5, "lb", "weight")]
    [InlineData(8, 8, 185, "kg", "unit")]
    public async Task Invalid_set_is_rejected_and_nothing_is_written(
        decimal rpe, int reps, decimal weight, string unit, string expectedField)
    {
        var workout = new WorkoutDto
        {
            PerformedOn = "2026-08-01",
            Exercises =
            [
                new WorkoutExerciseDto
                {
                    Name = "Barbell Bench Press",
                    Sets = [new WorkoutSetDto { Reps = reps, Weight = weight, Unit = unit, Rpe = rpe }]
                }
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/workouts", workout);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains(problem!.Errors.Keys, key => key.EndsWith(expectedField, StringComparison.Ordinal));

        // The wrapper the single-workout endpoint adds must not leak into error keys.
        Assert.All(problem.Errors.Keys, key => Assert.StartsWith("exercises[0]", key, StringComparison.Ordinal));

        Assert.Equal(0, await CountSetsOnAsync("2026-08-01"));
    }

    [Fact]
    public async Task One_bad_workout_rejects_the_whole_batch()
    {
        var request = new ImportBatchRequest
        {
            Workouts =
            [
                Workout("2026-09-01", Exercise("Barbell Bench Press", Set(8, 185))),
                Workout("not-a-date", Exercise("Barbell Row", Set(8, 135)))
            ]
        };

        var response = await _client.PostAsJsonAsync("/api/workouts/batch", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountSetsOnAsync("2026-09-01"));
    }

    [Fact]
    public async Task The_same_exercise_twice_in_one_workout_is_a_validation_error()
    {
        var workout = Workout("2026-10-01",
            Exercise("Barbell Bench Press", Set(8, 185)),
            Exercise("bench", Set(6, 195)));

        var response = await _client.PostAsJsonAsync("/api/workouts", workout);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountSetsOnAsync("2026-10-01"));
    }

    private async Task<ImportResult> PostWorkoutAsync(WorkoutDto workout) =>
        await ReadResultAsync(await _client.PostAsJsonAsync("/api/workouts", workout));

    private static async Task<ImportResult> ReadResultAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    private async Task<int> CountSetsOnAsync(string performedOn)
    {
        using var connection = _factory.OpenConnection();

        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM WorkoutSet s
            JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
            JOIN Workout w          ON w.Id = we.WorkoutId
            WHERE w.PerformedOn = @performedOn;
            """,
            new { performedOn });
    }

    private sealed record UnresolvedRow(long Id, string CanonicalName);

    private sealed record ValidationProblemResponse(Dictionary<string, string[]> Errors);
}

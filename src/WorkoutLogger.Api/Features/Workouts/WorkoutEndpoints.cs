namespace WorkoutLogger.Api.Features.Workouts;

public static class WorkoutEndpoints
{
    private const int MaxPageSize = 200;

    public static void MapWorkoutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/workouts", ListAsync)
            .WithTags("Workouts")
            .WithSummary("List workouts, newest first.");

        app.MapGet("/api/workouts/{id:long}", GetAsync)
            .WithTags("Workouts")
            .WithSummary("Get one workout with its exercises and sets.");

        app.MapGet("/api/sets", ListSetsAsync)
            .WithTags("Workouts")
            .WithSummary("Flat, chart-ready set rows.");
    }

    private static async Task<IResult> ListAsync(
        WorkoutRepository repository,
        DateOnly? from, DateOnly? to, int? page, int? pageSize,
        CancellationToken ct)
    {
        var resolvedPage = Math.Max(page ?? 1, 1);
        var resolvedPageSize = Math.Clamp(pageSize ?? 50, 1, MaxPageSize);

        return Results.Ok(await repository.ListAsync(from, to, resolvedPage, resolvedPageSize, ct));
    }

    private static async Task<IResult> GetAsync(long id, WorkoutRepository repository, CancellationToken ct)
        => await repository.GetAsync(id, ct) is { } workout
            ? Results.Ok(workout)
            : Results.NotFound();

    private static async Task<IResult> ListSetsAsync(
        WorkoutRepository repository,
        long? exerciseId, DateOnly? from, DateOnly? to, bool? includeWarmups,
        CancellationToken ct)
        => Results.Ok(await repository.ListSetsAsync(exerciseId, from, to, includeWarmups ?? false, ct));
}

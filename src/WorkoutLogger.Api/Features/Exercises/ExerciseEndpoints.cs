using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Features.Exercises;

public sealed record AddAliasRequest(string? Alias);

public static class ExerciseEndpoints
{
    public static void MapExerciseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exercises").WithTags("Exercises");

        group.MapGet("/", (ExerciseRepository repository, CancellationToken ct) =>
                repository.ListAsync(unresolvedOnly: false, ct))
            .WithSummary("List the exercise catalogue.");

        group.MapGet("/unresolved", (ExerciseRepository repository, CancellationToken ct) =>
                repository.ListAsync(unresolvedOnly: true, ct))
            .WithSummary("List names that did not match the catalogue and need mapping.");

        group.MapPost("/{id:long}/aliases", AddAliasAsync)
            .WithSummary("Teach an exercise another spelling so future imports resolve to it.");

        group.MapPost("/{id:long}/merge-into/{targetId:long}", MergeAsync)
            .WithSummary("Fold an exercise into another, repointing everything already logged.");
    }

    private static async Task<IResult> AddAliasAsync(
        long id, AddAliasRequest request, ExerciseRepository repository, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Alias) || TextNormalizer.Normalize(request.Alias).Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["alias"] = ["alias must contain at least one letter or digit."]
            });
        }

        return await repository.AddAliasAsync(id, request.Alias, ct)
            ? Results.NoContent()
            : Results.NotFound();
    }

    private static async Task<IResult> MergeAsync(
        long id, long targetId, ExerciseRepository repository, CancellationToken ct)
        => await repository.MergeAsync(id, targetId, ct) switch
        {
            MergeOutcome.Merged => Results.NoContent(),
            MergeOutcome.SameExercise => Results.BadRequest(new { error = "An exercise cannot be merged into itself." }),
            MergeOutcome.SourceNotFound => Results.NotFound(new { error = $"Exercise {id} was not found." }),
            MergeOutcome.TargetNotFound => Results.NotFound(new { error = $"Exercise {targetId} was not found." }),
            _ => Results.Problem("Unexpected merge outcome.")
        };
}

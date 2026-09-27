using FluentValidation;
using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Features.Import;

public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/workouts").WithTags("Import");

        group.MapPost("/", ImportSingleAsync)
            .WithName("ImportWorkout")
            .WithSummary("Import a single workout.");

        group.MapPost("/batch", ImportBatchAsync)
            .WithName("ImportWorkoutBatch")
            .WithSummary("Import several workouts in one request.");
    }

    private static async Task<IResult> ImportSingleAsync(
        WorkoutDto workout,
        string? source,
        ImportBatchRequestValidator validator,
        WorkoutImportService importService,
        CancellationToken ct)
    {
        var request = new ImportBatchRequest { Source = source, Workouts = [workout] };

        // Validated as a batch of one, then the wrapper prefix is trimmed off the
        // error keys so they line up with the body the caller actually posted.
        return await RunAsync(request, validator, importService, "Workouts[0].", ct);
    }

    private static Task<IResult> ImportBatchAsync(
        ImportBatchRequest request,
        ImportBatchRequestValidator validator,
        WorkoutImportService importService,
        CancellationToken ct)
        => RunAsync(request, validator, importService, trimPrefix: null, ct);

    private static async Task<IResult> RunAsync(
        ImportBatchRequest request,
        ImportBatchRequestValidator validator,
        WorkoutImportService importService,
        string? trimPrefix,
        CancellationToken ct)
    {
        // Validate-first, all-or-nothing: the caller gets every problem at once
        // and nothing is written, so a corrected payload can simply be re-posted.
        var validation = await validator.ValidateAsync(request, ct);

        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToProblemDictionary(trimPrefix));
        }

        try
        {
            return Results.Ok(await importService.ImportAsync(request, ct));
        }
        catch (ImportRejectedException ex)
        {
            return Results.ValidationProblem(ex.Validation.ToProblemDictionary(trimPrefix));
        }
    }
}

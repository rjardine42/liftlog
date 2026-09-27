using FluentValidation;
using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Features.Import;

public sealed class ImportBatchRequestValidator : AbstractValidator<ImportBatchRequest>
{
    public ImportBatchRequestValidator()
    {
        RuleFor(x => x.Workouts)
            .NotEmpty().WithMessage("At least one workout is required.");

        RuleForEach(x => x.Workouts)
            .NotNull().WithMessage("Workout entries cannot be null.")
            .SetValidator(new WorkoutDtoValidator());
    }
}

public sealed class WorkoutDtoValidator : AbstractValidator<WorkoutDto>
{
    public WorkoutDtoValidator()
    {
        // Stop at the first failure: a missing date should report "required",
        // not also "must be an ISO date".
        RuleFor(x => x.PerformedOn)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("performedOn is required.")
            .Must(BeAnIsoDate).WithMessage("performedOn must be an ISO date (yyyy-MM-dd).")
            .Must(NotBeInTheFuture).WithMessage("performedOn cannot be in the future.");

        RuleFor(x => x.Exercises)
            .NotEmpty().WithMessage("At least one exercise is required.");

        RuleFor(x => x.Exercises)
            .Must(HaveNoDuplicateExercises)
            .WithMessage(WorkoutImportService.DuplicateExerciseMessage)
            .When(x => x.Exercises is { Count: > 1 });

        RuleForEach(x => x.Exercises)
            .NotNull().WithMessage("Exercise entries cannot be null.")
            .SetValidator(new WorkoutExerciseDtoValidator());
    }

    private static bool BeAnIsoDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", out _);

    private static bool NotBeInTheFuture(string? value) =>
        !DateOnly.TryParseExact(value, "yyyy-MM-dd", out var date)
        || date <= DateOnly.FromDateTime(DateTime.UtcNow);

    private static bool HaveNoDuplicateExercises(List<WorkoutExerciseDto>? exercises)
    {
        if (exercises is null)
        {
            return true;
        }

        // Compare on the normalized form so "Bench Press" and "bench-press" are
        // caught here. Different aliases of one lift ("Bench" and "Barbell Bench
        // Press") normalize differently; WorkoutImportService catches those once
        // the names are resolved.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return exercises
            .Select(e => TextNormalizer.Normalize(e.Name))
            .Where(n => n.Length > 0)
            .All(seen.Add);
    }
}

public sealed class WorkoutExerciseDtoValidator : AbstractValidator<WorkoutExerciseDto>
{
    public WorkoutExerciseDtoValidator()
    {
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Exercise name is required.")
            .Must(name => TextNormalizer.Normalize(name).Length > 0)
            .WithMessage("Exercise name must contain at least one letter or digit.");

        RuleFor(x => x.Rpe)
            .Must(RpeRules.IsValid!)
            .WithMessage(RpeRules.Message)
            .When(x => x.Rpe.HasValue);

        RuleFor(x => x.Sets)
            .NotEmpty().WithMessage("At least one set is required.");

        RuleForEach(x => x.Sets)
            .NotNull().WithMessage("Set entries cannot be null.")
            .SetValidator(new WorkoutSetDtoValidator());
    }
}

public sealed class WorkoutSetDtoValidator : AbstractValidator<WorkoutSetDto>
{
    public WorkoutSetDtoValidator()
    {
        RuleFor(x => x.Reps)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("reps is required.")
            .GreaterThan(0).WithMessage("reps must be greater than zero.");

        RuleFor(x => x.Weight)
            .GreaterThanOrEqualTo(0).WithMessage("weight cannot be negative.")
            .When(x => x.Weight.HasValue);

        RuleFor(x => x.Unit)
            .Must(unit => string.Equals(unit?.Trim(), WeightUnits.Pounds, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"Only '{WeightUnits.Pounds}' is supported; unit conversion is not implemented yet.")
            .When(x => !string.IsNullOrWhiteSpace(x.Unit));

        RuleFor(x => x.Rpe)
            .Must(RpeRules.IsValid!)
            .WithMessage(RpeRules.Message)
            .When(x => x.Rpe.HasValue);
    }
}

internal static class RpeRules
{
    public const string Message = "rpe must be between 1 and 10 in steps of 0.5.";

    public static bool IsValid(decimal? rpe) =>
        rpe is >= 1m and <= 10m && rpe.Value % 0.5m == 0m;
}

public static class WeightUnits
{
    public const string Pounds = "lb";
}

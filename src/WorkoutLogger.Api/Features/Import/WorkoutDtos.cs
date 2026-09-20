namespace WorkoutLogger.Api.Features.Import;

/// <summary>A batch of whole workouts. The single-workout endpoint wraps its body in one of these.</summary>
public sealed record ImportBatchRequest
{
    public string? Source { get; init; }
    public List<WorkoutDto>? Workouts { get; init; }
}

public sealed record WorkoutDto
{
    public string? ExternalId { get; init; }

    /// <summary>ISO date, yyyy-MM-dd. Kept as a string so a bad value becomes a validation error rather than a deserialization failure.</summary>
    public string? PerformedOn { get; init; }

    /// <summary>Optional session tag ("am"/"pm"). Null means the day's only session.</summary>
    public string? Label { get; init; }

    public string? Notes { get; init; }
    public List<WorkoutExerciseDto>? Exercises { get; init; }
}

public sealed record WorkoutExerciseDto
{
    public string? Name { get; init; }
    public string? Notes { get; init; }

    /// <summary>Shorthand applied to any set in this block that omits its own RPE. Storage stays per-set.</summary>
    public decimal? Rpe { get; init; }

    public List<WorkoutSetDto>? Sets { get; init; }
}

public sealed record WorkoutSetDto
{
    public int? Reps { get; init; }
    public decimal? Weight { get; init; }

    /// <summary>Only "lb" is supported today; the field exists so conversion can be added without a schema change.</summary>
    public string? Unit { get; init; }

    public decimal? Rpe { get; init; }
    public bool IsWarmup { get; init; }
}

public sealed record ImportResult
{
    public long BatchId { get; init; }
    public int WorkoutsReceived { get; init; }
    public int WorkoutsCreated { get; init; }
    public int WorkoutsMatched { get; init; }
    public int ExercisesImported { get; init; }
    public int ExercisesSkipped { get; init; }
    public IReadOnlyList<SkippedExercise> Skipped { get; init; } = [];

    /// <summary>Names that did not match the catalogue and were stored as unresolved. Surfaced so the alias backlog stays visible.</summary>
    public IReadOnlyList<UnresolvedExercise> UnresolvedExercises { get; init; } = [];
}

public sealed record SkippedExercise(string PerformedOn, string Exercise, string Reason);

public sealed record UnresolvedExercise(long ExerciseId, string Name);

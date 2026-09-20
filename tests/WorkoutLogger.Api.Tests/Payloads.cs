using WorkoutLogger.Api.Features.Import;

namespace WorkoutLogger.Api.Tests;

/// <summary>Small builders so each test shows only the field it cares about.</summary>
public static class Payloads
{
    public static WorkoutDto Workout(string performedOn, params WorkoutExerciseDto[] exercises) =>
        new() { PerformedOn = performedOn, Exercises = [.. exercises] };

    public static WorkoutExerciseDto Exercise(string name, params WorkoutSetDto[] sets) =>
        new() { Name = name, Sets = [.. sets] };

    public static WorkoutExerciseDto Exercise(string name, decimal? rpe, params WorkoutSetDto[] sets) =>
        new() { Name = name, Rpe = rpe, Sets = [.. sets] };

    public static WorkoutSetDto Set(int reps, decimal weight, decimal? rpe = null, bool isWarmup = false) =>
        new() { Reps = reps, Weight = weight, Rpe = rpe, IsWarmup = isWarmup };
}

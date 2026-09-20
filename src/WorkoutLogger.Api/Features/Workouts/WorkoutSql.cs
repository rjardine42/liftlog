namespace WorkoutLogger.Api.Features.Workouts;

/// <summary>Every SQL statement used by the Workouts feature.</summary>
internal static class WorkoutSql
{
    public const string ListSummaries =
        """
        SELECT w.Id, w.PerformedOn, w.Label, w.Notes,
               COUNT(DISTINCT we.Id) AS ExerciseCount,
               COUNT(s.Id)           AS SetCount
        FROM Workout w
        LEFT JOIN WorkoutExercise we ON we.WorkoutId = w.Id
        LEFT JOIN WorkoutSet s       ON s.WorkoutExerciseId = we.Id
        WHERE (@from IS NULL OR w.PerformedOn >= @from)
          AND (@to   IS NULL OR w.PerformedOn <= @to)
        GROUP BY w.Id
        ORDER BY w.PerformedOn DESC, w.Id DESC
        LIMIT @pageSize OFFSET @offset;
        """;

    public const string SelectHeaderById =
        "SELECT Id, PerformedOn, Label, Notes, SourceName FROM Workout WHERE Id = @id;";

    /// <remarks>
    /// One row per set, joined flat; the caller groups by exercise block. The
    /// LEFT JOIN keeps an exercise logged without sets in the result.
    /// </remarks>
    public const string SelectExerciseBlocks =
        """
        SELECT we.ExerciseId, e.CanonicalName AS Exercise, we.RawExerciseName AS RawName,
               we.Position, we.Notes,
               s.SetNumber, s.Reps, s.Weight, s.WeightUnit AS Unit, s.Rpe, s.IsWarmup
        FROM WorkoutExercise we
        JOIN Exercise e    ON e.Id = we.ExerciseId
        LEFT JOIN WorkoutSet s ON s.WorkoutExerciseId = we.Id
        WHERE we.WorkoutId = @id
        ORDER BY we.Position, s.SetNumber;
        """;

    public const string ListFlatSets =
        """
        SELECT w.PerformedOn, w.Id AS WorkoutId, we.ExerciseId, e.CanonicalName AS Exercise,
               s.SetNumber, s.Reps, s.Weight, s.WeightUnit AS Unit, s.Rpe, s.IsWarmup
        FROM WorkoutSet s
        JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
        JOIN Workout w          ON w.Id = we.WorkoutId
        JOIN Exercise e         ON e.Id = we.ExerciseId
        WHERE (@exerciseId IS NULL OR we.ExerciseId = @exerciseId)
          AND (@from IS NULL OR w.PerformedOn >= @from)
          AND (@to   IS NULL OR w.PerformedOn <= @to)
          AND (@includeWarmups = 1 OR s.IsWarmup = 0)
        ORDER BY w.PerformedOn, w.Id, we.Position, s.SetNumber;
        """;
}

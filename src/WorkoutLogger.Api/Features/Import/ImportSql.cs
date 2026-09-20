namespace WorkoutLogger.Api.Features.Import;

/// <summary>Every SQL statement used by the Import feature.</summary>
internal static class ImportSql
{
    /// <remarks>
    /// Inserted up front with zero counts because WorkoutExercise carries an FK to
    /// it; the totals are written back by <see cref="UpdateBatchTotals"/> once the
    /// batch has been walked.
    /// </remarks>
    public const string InsertBatch =
        """
        INSERT INTO ImportBatch
            (ReceivedAtUtc, SourceName, PayloadHash, WorkoutsReceived, ExercisesImported, ExercisesSkipped, Status)
        VALUES (@now, @source, @payloadHash, @workoutsReceived, 0, 0, 'Pending')
        RETURNING Id;
        """;

    public const string UpdateBatchTotals =
        """
        UPDATE ImportBatch
        SET ExercisesImported = @imported, ExercisesSkipped = @skippedCount, Status = 'Accepted'
        WHERE Id = @batchId;
        """;

    /// <remarks>
    /// Workouts are find-or-create by (PerformedOn, Label); COALESCE makes a null
    /// label compare equal to a null label rather than never matching.
    /// </remarks>
    public const string FindWorkoutByDateAndLabel =
        "SELECT Id FROM Workout WHERE PerformedOn = @performedOn AND COALESCE(Label, '') = COALESCE(@label, '');";

    public const string InsertWorkout =
        """
        INSERT INTO Workout (ExternalId, PerformedOn, Label, Notes, SourceName, CreatedAtUtc)
        VALUES (@externalId, @performedOn, @label, @notes, @source, @now)
        RETURNING Id;
        """;

    public const string SelectMaxPosition =
        "SELECT COALESCE(MAX(Position), 0) FROM WorkoutExercise WHERE WorkoutId = @workoutId;";

    /// <remarks>
    /// ON CONFLICT DO NOTHING turns the UNIQUE(WorkoutId, ExerciseId) dedupe into a
    /// null result rather than an exception, so a repeat import is a reportable skip
    /// instead of a failure.
    /// </remarks>
    public const string InsertWorkoutExercise =
        """
        INSERT INTO WorkoutExercise
            (WorkoutId, ExerciseId, Position, RawExerciseName, Notes, ImportBatchId, CreatedAtUtc)
        VALUES (@workoutId, @exerciseId, @position, @rawName, @notes, @batchId, @now)
        ON CONFLICT (WorkoutId, ExerciseId) DO NOTHING
        RETURNING Id;
        """;

    public const string InsertWorkoutSet =
        """
        INSERT INTO WorkoutSet
            (WorkoutExerciseId, SetNumber, Reps, Weight, WeightUnit, Rpe, IsWarmup, CreatedAtUtc)
        VALUES (@workoutExerciseId, @setNumber, @reps, @weight, @weightUnit, @rpe, @isWarmup, @now);
        """;
}

namespace WorkoutLogger.Api.Features.Exercises;

/// <summary>Every SQL statement used by the Exercises feature.</summary>
internal static class ExerciseSql
{
    public const string List =
        """
        SELECT e.Id, e.CanonicalName, e.MuscleGroup, e.Modality, e.IsUnresolved,
               COUNT(we.Id) AS TimesLogged
        FROM Exercise e
        LEFT JOIN WorkoutExercise we ON we.ExerciseId = e.Id
        WHERE (@unresolvedOnly = 0 OR e.IsUnresolved = 1)
        GROUP BY e.Id
        ORDER BY e.CanonicalName;
        """;

    public const string ExistsById = "SELECT Id FROM Exercise WHERE Id = @id;";

    public const string InsertAlias =
        """
        INSERT OR IGNORE INTO ExerciseAlias (ExerciseId, NormalizedAlias, RawAlias, CreatedAtUtc)
        VALUES (@exerciseId, @normalized, @alias, @now);
        """;

    public const string SelectNamesById =
        "SELECT CanonicalName, NormalizedName FROM Exercise WHERE Id = @sourceId;";

    /// <remarks>
    /// Merge step 1. For workouts holding both exercises a straight repoint would
    /// violate UNIQUE(WorkoutId, ExerciseId), so the source sets are appended to
    /// the target block with SetNumber offset past whatever is already there.
    /// </remarks>
    public const string MergeAppendSetsToTargetBlock =
        """
        UPDATE WorkoutSet AS s
        SET WorkoutExerciseId = m.TargetBlockId,
            SetNumber         = s.SetNumber + m.SetOffset
        FROM (
            SELECT src.Id AS SourceBlockId,
                   tgt.Id AS TargetBlockId,
                   (SELECT COALESCE(MAX(x.SetNumber), 0) FROM WorkoutSet x WHERE x.WorkoutExerciseId = tgt.Id) AS SetOffset
            FROM WorkoutExercise src
            JOIN WorkoutExercise tgt ON tgt.WorkoutId = src.WorkoutId AND tgt.ExerciseId = @targetId
            WHERE src.ExerciseId = @sourceId
        ) AS m
        WHERE s.WorkoutExerciseId = m.SourceBlockId;
        """;

    /// <remarks>Merge step 2: the source blocks emptied by step 1.</remarks>
    public const string MergeDeleteEmptySourceBlocks =
        """
        DELETE FROM WorkoutExercise
        WHERE ExerciseId = @sourceId
          AND EXISTS (
              SELECT 1 FROM WorkoutExercise t
              WHERE t.WorkoutId = WorkoutExercise.WorkoutId AND t.ExerciseId = @targetId);
        """;

    /// <remarks>Merge step 3: everything left has no conflict and can simply be repointed.</remarks>
    public const string MergeRepointRemainingBlocks =
        "UPDATE WorkoutExercise SET ExerciseId = @targetId WHERE ExerciseId = @sourceId;";

    /// <remarks>
    /// Merge step 4. Carries the aliases over, then makes the source's own name an
    /// alias so the spelling that caused this resolves correctly next time.
    /// OR IGNORE covers an alias the target already holds.
    /// </remarks>
    public const string MergeCarryAliasesAndDeleteSource =
        """
        UPDATE OR IGNORE ExerciseAlias SET ExerciseId = @targetId WHERE ExerciseId = @sourceId;

        INSERT OR IGNORE INTO ExerciseAlias (ExerciseId, NormalizedAlias, RawAlias, CreatedAtUtc)
        VALUES (@targetId, @normalizedName, @canonicalName, @now);

        DELETE FROM Exercise WHERE Id = @sourceId;
        """;

    /// <remarks>Alias first, then canonical name — LIMIT 1 takes the alias when both match.</remarks>
    public const string ResolveByAliasOrName =
        """
        SELECT e.Id, e.CanonicalName
        FROM ExerciseAlias a
        JOIN Exercise e ON e.Id = a.ExerciseId
        WHERE a.NormalizedAlias = @normalized
        UNION ALL
        SELECT e.Id, e.CanonicalName
        FROM Exercise e
        WHERE e.NormalizedName = @normalized
        LIMIT 1;
        """;

    public const string InsertUnresolved =
        """
        INSERT INTO Exercise (CanonicalName, NormalizedName, IsUnresolved, CreatedAtUtc)
        VALUES (@rawName, @normalized, 1, @createdAtUtc)
        RETURNING Id;
        """;
}

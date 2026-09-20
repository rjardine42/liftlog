namespace WorkoutLogger.Api.Features.Analytics;

/// <summary>Every SQL statement used by the Analytics feature.</summary>
internal static class AnalyticsSql
{
    /// <summary>
    /// Best estimated one-rep max per bucket, via the Epley formula
    /// (<c>weight x (1 + reps / 30)</c>).
    /// </summary>
    /// <remarks>
    /// A <see cref="string.Format(string, object?)"/> template: <c>{0}</c> takes one
    /// of the <c>Bucket*</c> expressions below. Warmups are excluded and bodyweight
    /// sets (no weight recorded) are ignored, since neither says anything about a
    /// maximum.
    /// </remarks>
    public const string EstimatedOneRepMax =
        """
        SELECT {0} AS Bucket,
               MAX(s.Weight * (1.0 + s.Reps / 30.0)) AS Value,
               COUNT(*)                              AS SampleCount
        FROM WorkoutSet s
        JOIN WorkoutExercise we ON we.Id = s.WorkoutExerciseId
        JOIN Workout w          ON w.Id = we.WorkoutId
        WHERE s.IsWarmup = 0
          AND s.Weight IS NOT NULL
          AND (@exerciseId IS NULL OR we.ExerciseId = @exerciseId)
          AND (@from IS NULL OR w.PerformedOn >= @from)
          AND (@to   IS NULL OR w.PerformedOn <= @to)
        GROUP BY Bucket
        ORDER BY Bucket;
        """;

    /// <summary>Bucket start as an ISO date.</summary>
    public const string BucketDay = "w.PerformedOn";

    /// <summary>
    /// Bucket start as an ISO date. Weeks start Monday: SQLite's %w is
    /// Sunday-based, so shift by 6 days and take the preceding Monday.
    /// </summary>
    public const string BucketWeek = "date(w.PerformedOn, '-6 days', 'weekday 1')";

    /// <summary>Bucket start as an ISO date.</summary>
    public const string BucketMonth = "strftime('%Y-%m-01', w.PerformedOn)";
}

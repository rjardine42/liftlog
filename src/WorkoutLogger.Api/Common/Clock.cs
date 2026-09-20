namespace WorkoutLogger.Api.Common;

/// <summary>
/// Timestamps are stored as ISO-8601 TEXT. SQLite has no date type, and ISO
/// strings sort chronologically, so range filters work as plain comparisons.
/// </summary>
public static class Clock
{
    public const string IsoFormat = "yyyy-MM-ddTHH:mm:ssZ";

    public static string UtcNowIso() => DateTime.UtcNow.ToString(IsoFormat);
}

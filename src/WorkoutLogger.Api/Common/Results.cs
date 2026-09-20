using FluentValidation.Results;

namespace WorkoutLogger.Api.Common;

public static class ValidationResults
{
    /// <summary>
    /// Converts FluentValidation failures into the dictionary shape
    /// <c>Results.ValidationProblem</c> expects, keyed by a JSON-style path.
    /// </summary>
    /// <param name="result">The failed validation result.</param>
    /// <param name="trimPrefix">
    /// Stripped from the front of every key. The single-workout endpoint wraps its
    /// body in a batch of one, so without this its errors would all read
    /// "Workouts[0].Exercises[0]..." and point at a property the caller never sent.
    /// </param>
    public static IDictionary<string, string[]> ToProblemDictionary(this ValidationResult result, string? trimPrefix = null) =>
        result.Errors
            .GroupBy(e => ToJsonPath(e.PropertyName, trimPrefix))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

    private static string ToJsonPath(string propertyName, string? trimPrefix)
    {
        if (trimPrefix is not null && propertyName.StartsWith(trimPrefix, StringComparison.Ordinal))
        {
            propertyName = propertyName[trimPrefix.Length..];
        }

        // Match the camelCase the payload itself uses: Exercises[0].Sets[0].Reps
        // becomes exercises[0].sets[0].reps.
        return string.Join('.', propertyName
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => char.ToLowerInvariant(segment[0]) + segment[1..]));
    }
}

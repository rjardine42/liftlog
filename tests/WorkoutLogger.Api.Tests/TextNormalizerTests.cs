using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Tests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData("Barbell Bench Press", "barbell bench press")]
    [InlineData("  Barbell   Bench  Press  ", "barbell bench press")]
    [InlineData("Barbell Bench-Press", "barbell bench press")]
    [InlineData("BENCH_PRESS", "bench press")]
    [InlineData("Farmer's Walk", "farmer s walk")]
    [InlineData("Incline DB Press (30°)", "incline db press 30")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_collapses_a_name_to_its_lookup_key(string? input, string expected)
        => Assert.Equal(expected, TextNormalizer.Normalize(input));
}

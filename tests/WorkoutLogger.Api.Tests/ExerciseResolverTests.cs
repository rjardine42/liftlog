using Dapper;
using WorkoutLogger.Api.Features.Exercises;

namespace WorkoutLogger.Api.Tests;

public sealed class ExerciseResolverTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ExerciseResolverTests(ApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("Barbell Bench Press")]   // canonical name
    [InlineData("bench")]                 // seeded alias
    [InlineData("BB Bench")]              // alias, different casing
    [InlineData("  barbell   bench-press  ")] // punctuation and spacing noise
    public async Task Every_spelling_of_a_known_lift_resolves_to_the_same_exercise(string spelling)
    {
        using var connection = _factory.OpenConnection();
        var resolver = new ExerciseResolver(connection);

        var canonical = await resolver.ResolveAsync("Barbell Bench Press", CancellationToken.None);
        var resolved = await resolver.ResolveAsync(spelling, CancellationToken.None);

        Assert.Equal(canonical.ExerciseId, resolved.ExerciseId);
        Assert.False(resolved.WasCreatedUnresolved);
    }

    [Fact]
    public async Task An_unknown_name_is_created_as_unresolved_rather_than_failing()
    {
        using var connection = _factory.OpenConnection();
        var resolver = new ExerciseResolver(connection);

        var resolved = await resolver.ResolveAsync("Zercher Carry", CancellationToken.None);

        Assert.True(resolved.WasCreatedUnresolved);
        Assert.Equal("Zercher Carry", resolved.CanonicalName);

        var isUnresolved = await connection.ExecuteScalarAsync<bool>(
            "SELECT IsUnresolved FROM Exercise WHERE Id = @id;", new { id = resolved.ExerciseId });

        Assert.True(isUnresolved);
    }

    [Fact]
    public async Task An_unknown_name_is_only_created_once()
    {
        using var connection = _factory.OpenConnection();

        // Separate resolvers, so this exercises the database lookup rather than the per-request cache.
        var first = await new ExerciseResolver(connection).ResolveAsync("Landmine Press", CancellationToken.None);
        var second = await new ExerciseResolver(connection).ResolveAsync("landmine  press", CancellationToken.None);

        Assert.Equal(first.ExerciseId, second.ExerciseId);
        Assert.True(first.WasCreatedUnresolved);
        Assert.False(second.WasCreatedUnresolved);
    }

    [Fact]
    public async Task A_name_with_no_letters_or_digits_is_rejected()
    {
        using var connection = _factory.OpenConnection();
        var resolver = new ExerciseResolver(connection);

        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveAsync("---", CancellationToken.None));
    }
}

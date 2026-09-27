using System.Net;
using Microsoft.AspNetCore.Hosting;
using WorkoutLogger.Api.Common;

namespace WorkoutLogger.Api.Tests;

public sealed class ApiKeyTests : IClassFixture<ApiFactory>
{
    private const string Key = "test-api-key";

    private readonly ApiFactory _factory;

    public ApiKeyTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_is_open_without_a_key()
    {
        var response = await KeyedClient(key: null).GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task Api_routes_reject_a_missing_or_wrong_key(string? key)
    {
        var response = await KeyedClient(key).GetAsync("/api/workouts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_routes_accept_the_right_key()
    {
        var response = await KeyedClient(Key).GetAsync("/api/workouts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Startup_fails_outside_development_without_a_key()
    {
        var production = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));

        Assert.ThrowsAny<Exception>(() => production.CreateClient());
    }

    private HttpClient KeyedClient(string? key)
    {
        var client = _factory
            .WithWebHostBuilder(builder => builder.UseSetting(ApiKeyAuthentication.SettingName, Key))
            .CreateClient();

        if (key is not null)
        {
            client.DefaultRequestHeaders.Add(ApiKeyAuthentication.HeaderName, key);
        }

        return client;
    }
}

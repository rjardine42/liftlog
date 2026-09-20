using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using WorkoutLogger.Api.Persistence;

namespace WorkoutLogger.Api.Tests;

/// <summary>
/// Hosts the API against a throwaway SQLite file.
/// </summary>
/// <remarks>
/// A real file rather than an in-memory fake: the point of these tests is to
/// exercise the hand-written SQL, the UNIQUE constraints that implement dedupe,
/// and the FK pragma. Used as an IClassFixture, so each test class gets its own
/// database and DatabaseInitializer builds the schema on first request.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"workoutlogger-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:WorkoutDb", $"Data Source={_databasePath}");
    }

    public SqliteConnection OpenConnection() =>
        Services.GetRequiredService<IDbConnectionFactory>().Create();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

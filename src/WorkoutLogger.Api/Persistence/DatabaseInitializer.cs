using System.Reflection;

namespace WorkoutLogger.Api.Persistence;

/// <summary>
/// Creates the schema and seeds the exercise catalogue at startup.
/// </summary>
/// <remarks>
/// Deliberately not a migration framework. Schema.sql is all
/// CREATE TABLE IF NOT EXISTS and Seed.sql is all INSERT OR IGNORE, so running
/// this on every boot is a no-op after the first. The tradeoff is that there is
/// no versioning: changing Schema.sql means deleting the .db file and
/// re-importing. Swapping in a real migrator later replaces only this class.
/// </remarks>
public sealed class DatabaseInitializer
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(IDbConnectionFactory connectionFactory, ILogger<DatabaseInitializer> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public void Initialize()
    {
        using var connection = _connectionFactory.Create();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL;";
            pragma.ExecuteNonQuery();
        }

        foreach (var script in new[] { "Schema.sql", "Seed.sql" })
        {
            using var command = connection.CreateCommand();
            command.CommandText = ReadScript(script);
            command.ExecuteNonQuery();
        }

        _logger.LogInformation("Database initialized.");
    }

    private static string ReadScript(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"{assembly.GetName().Name}.Persistence.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

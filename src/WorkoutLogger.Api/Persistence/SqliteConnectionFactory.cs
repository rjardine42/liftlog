using Microsoft.Data.Sqlite;

namespace WorkoutLogger.Api.Persistence;

public interface IDbConnectionFactory
{
    SqliteConnection Create();
}

/// <summary>
/// Hands out open SQLite connections with foreign keys enabled.
/// </summary>
/// <remarks>
/// SQLite disables foreign key enforcement by default, and the setting is
/// per-connection rather than per-database. Without <c>Foreign Keys=True</c>
/// every FK and ON DELETE CASCADE in Schema.sql is silently inert.
/// </remarks>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        _connectionString = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true
        }.ToString();
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

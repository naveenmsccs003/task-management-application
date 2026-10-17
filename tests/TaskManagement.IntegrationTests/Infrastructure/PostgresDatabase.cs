using Npgsql;
using Testcontainers.PostgreSql;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>
/// Provides an empty PostgreSQL database for one test fixture.
/// <list type="bullet">
/// <item>If <c>TMA_TEST_POSTGRES</c> is set (a connection string to an existing server with rights to create databases),
/// a uniquely named database is created there and dropped afterwards.</item>
/// <item>Otherwise a PostgreSQL 18 container is started with Testcontainers (requires Docker; used in CI).</item>
/// </list>
/// Tests never run against an in-memory substitute, because unique partial indexes, xmin concurrency and grants matter.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    public const string ExternalServerVariable = "TMA_TEST_POSTGRES";
    public const string Image = "postgres:18-alpine";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _serverConnectionString;
    private readonly string? _databaseName;

    private PostgresDatabase(string connectionString, PostgreSqlContainer? container, string? serverConnectionString, string? databaseName)
    {
        ConnectionString = connectionString;
        _container = container;
        _serverConnectionString = serverConnectionString;
        _databaseName = databaseName;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresDatabase> CreateAsync()
    {
        var external = Environment.GetEnvironmentVariable(ExternalServerVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            var databaseName = $"tma_test_{Guid.NewGuid():N}";
            await using (var connection = new NpgsqlConnection(external))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
                await command.ExecuteNonQueryAsync();
            }

            await EnsureAppRoleAsync(external);
            var connectionString = new NpgsqlConnectionStringBuilder(external) { Database = databaseName }.ConnectionString;
            return new PostgresDatabase(connectionString, null, external, databaseName);
        }

        var container = new PostgreSqlBuilder(Image).Build();
        await container.StartAsync();
        await EnsureAppRoleAsync(container.GetConnectionString());
        return new PostgresDatabase(container.GetConnectionString(), container, null, null);
    }

    /// <summary>
    /// The production application role (deploy/db/create-roles.sql). Migrations grant it only INSERT/SELECT on the
    /// audit table; creating it here lets tests verify those grants. NOLOGIN: tests never connect as it.
    /// </summary>
    private static async Task EnsureAppRoleAsync(string serverConnectionString)
    {
        await using var connection = new NpgsqlConnection(serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'tma_app') THEN CREATE ROLE tma_app NOLOGIN; END IF; END $$;",
            connection);
        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception) when (exception.SqlState is PostgresErrorCodes.DuplicateObject or PostgresErrorCodes.UniqueViolation)
        {
            // Another test fixture created the role at the same moment.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

using Microsoft.EntityFrameworkCore;

namespace TaskManagement.Infrastructure.Persistence;

internal static class DbContextOptionsSetup
{
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Shared by runtime registration and the design-time factory so both build the same model.</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schemas.Ops))
            .UseSnakeCaseNamingConvention();
}

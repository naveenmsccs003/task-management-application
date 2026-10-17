using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskManagement.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c> tooling. Creating a migration does not need a database, so a placeholder
/// connection string is used unless <c>TMA_DESIGN_CONNECTION</c> is set (needed for <c>dotnet ef database update</c>).
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TMA_DESIGN_CONNECTION")
            ?? "Host=localhost;Database=tma_design;Username=tma_migrator";
        var options = DbContextOptionsSetup.Configure(new DbContextOptionsBuilder<AppDbContext>(), connectionString);
        return new AppDbContext((DbContextOptions<AppDbContext>)options.Options);
    }
}

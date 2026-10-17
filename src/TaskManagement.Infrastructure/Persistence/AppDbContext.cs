using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Settings;

namespace TaskManagement.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for all modules. Each module maps its tables into its own schema,
/// and one context lets a business change, its audit rows and its outbox rows share one transaction.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

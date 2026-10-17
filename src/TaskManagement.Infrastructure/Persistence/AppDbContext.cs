using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Audit;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Settings;

namespace TaskManagement.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context for all modules. Each module maps its tables into its own schema,
/// and one context lets a business change, its audit rows and its outbox rows share one transaction.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();

    public DbSet<PasswordToken> PasswordTokens => Set<PasswordToken>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<GlobalSetting> GlobalSettings => Set<GlobalSetting>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<EmailDelivery> EmailDeliveries => Set<EmailDelivery>();

    public void ExpectVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class =>
        Entry(entity).Property("Version").OriginalValue = expectedVersion;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

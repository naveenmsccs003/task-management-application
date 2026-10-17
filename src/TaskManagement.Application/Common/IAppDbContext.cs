using Microsoft.EntityFrameworkCore;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Audit;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Settings;

namespace TaskManagement.Application.Common;

/// <summary>
/// The application's view of the database. Handlers query and change data through these sets and save once,
/// so a change, its audit rows and its queued emails commit together.
/// </summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<PasswordToken> PasswordTokens { get; }

    DbSet<Role> Roles { get; }

    DbSet<Permission> Permissions { get; }

    DbSet<GlobalSetting> GlobalSettings { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    DbSet<EmailDelivery> EmailDeliveries { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the next save fail with a concurrency conflict if the row's version is no longer <paramref name="expectedVersion"/>
    /// (the version the client loaded) — prevents silently overwriting someone else's change (FR-TASK-10).
    /// </summary>
    void ExpectVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class;
}

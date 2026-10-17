using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Audit;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Settings;
using TaskManagement.Domain.Tasks;

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

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskStatusChange> TaskStatusHistory => Set<TaskStatusChange>();

    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskCommentEdit> TaskCommentEdits => Set<TaskCommentEdit>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        Database.BeginTransactionAsync(cancellationToken);

    public async Task<int> AllocateTaskNumberAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Task numbers must be allocated inside a transaction, so a failed create leaves no gap.");
        }

        // Insert-or-increment in one statement; the row lock is held until the transaction ends.
        var numbers = await Database.SqlQuery<int>($"""
            INSERT INTO projects.task_counters (project_id, last_number) VALUES ({projectId}, 1)
            ON CONFLICT (project_id) DO UPDATE SET last_number = task_counters.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return numbers[0];
    }

    public void ExpectVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class =>
        Entry(entity).Property("Version").OriginalValue = expectedVersion;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

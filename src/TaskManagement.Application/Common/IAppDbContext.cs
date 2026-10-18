using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Audit;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Files;
using TaskManagement.Domain.GitLab;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Operations;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Settings;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

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

    DbSet<Project> Projects { get; }

    DbSet<ProjectMember> ProjectMembers { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<TaskStatusChange> TaskStatusHistory { get; }

    DbSet<TaskAssignment> TaskAssignments { get; }

    DbSet<TaskComment> TaskComments { get; }

    DbSet<TaskCommentEdit> TaskCommentEdits { get; }

    DbSet<AlgorithmSubmission> AlgorithmSubmissions { get; }

    DbSet<ChecklistTemplate> ChecklistTemplates { get; }

    DbSet<TaskChecklistItem> TaskChecklistItems { get; }

    DbSet<WorkSession> WorkSessions { get; }

    DbSet<TimeAdjustment> TimeAdjustments { get; }

    DbSet<IdempotencyRecord> IdempotencyRecords { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<NotificationPreference> NotificationPreferences { get; }

    DbSet<JobRun> JobRuns { get; }

    DbSet<ReviewSubmission> ReviewSubmissions { get; }

    DbSet<ApprovalRequest> ApprovalRequests { get; }

    DbSet<DelayReport> DelayReports { get; }

    DbSet<Attachment> Attachments { get; }

    DbSet<GitLabConnection> GitLabConnections { get; }

    DbSet<GitLabProjectLink> GitLabProjectLinks { get; }

    DbSet<GitLabIssueLink> GitLabIssueLinks { get; }

    DbSet<ImportRun> ImportRuns { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>True when the save failed on a unique index (e.g. a second active work session, FR-TIME-03).</summary>
    bool IsUniqueViolation(DbUpdateException exception);

    /// <summary>Forgets all pending changes, so a failed save does not leak into a later one in the same request.</summary>
    void DiscardChanges();

    /// <summary>
    /// Returns the project's next task number. The counter row stays locked until the surrounding transaction ends,
    /// so concurrent creates in one project are serialized and numbers have no gaps.
    /// </summary>
    Task<int> AllocateTaskNumberAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the next save fail with a concurrency conflict if the row's version is no longer <paramref name="expectedVersion"/>
    /// (the version the client loaded) — prevents silently overwriting someone else's change (FR-TASK-10).
    /// </summary>
    void ExpectVersion<TEntity>(TEntity entity, uint expectedVersion)
        where TEntity : class;
}

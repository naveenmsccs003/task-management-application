using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Operations;

namespace TaskManagement.Application.Notifications;

public sealed record JobRunDto(string JobName, DateTimeOffset LastStartedAt, DateTimeOffset LastFinishedAt, bool LastSucceeded, DateTimeOffset? LastSucceededAt, string? LastMessage);

public sealed record FailedEmailDto(Guid Id, string RecipientEmail, string Subject, int Attempts, string? LastError, DateTimeOffset CreatedAt);

public sealed record EmailQueueDto(int Pending, int Failed, int SentLast24Hours, DateTimeOffset? OldestPendingAt, IReadOnlyList<FailedEmailDto> RecentFailures);

/// <summary>GET /admin/system-status (FR-ADM-01, FR-NOT-05): background jobs and the email queue.</summary>
public sealed record SystemStatusDto(DateTimeOffset ServerTime, IReadOnlyList<JobRunDto> Jobs, EmailQueueDto Email);

/// <summary>Records background job runs and reports system status to Admins.</summary>
public sealed class JobRecorder(IAppDbContext db, TimeProvider timeProvider)
{
    public const string SessionMonitor = "session-monitor";
    public const string EmailDelivery = "email-delivery";
    public const string Cleanup = "cleanup";

    /// <summary>Runs <paramref name="job"/> and records its outcome; a failure is recorded and then rethrown.</summary>
    public async Task<T> RunAsync<T>(string jobName, Func<Task<T>> job, Func<T, string> describe, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(describe);
        var started = timeProvider.GetUtcNow();
        try
        {
            var result = await job();
            await RecordAsync(jobName, started, true, describe(result), cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: exception messages can contain connection details.
            await RecordAsync(jobName, started, false, exception.GetType().Name, cancellationToken);
            throw;
        }
    }

    public async Task<SystemStatusDto> StatusAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var jobs = await db.JobRuns.AsNoTracking().OrderBy(j => j.JobName)
            .Select(j => new JobRunDto(j.JobName, j.LastStartedAt, j.LastFinishedAt, j.LastSucceeded, j.LastSucceededAt, j.LastMessage))
            .ToListAsync(cancellationToken);
        var dayAgo = now.AddDays(-1);
        var counts = await db.EmailDeliveries.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Pending = g.Count(e => e.Status == EmailDeliveryStatus.Pending),
                Failed = g.Count(e => e.Status == EmailDeliveryStatus.Failed),
                Sent = g.Count(e => e.Status == EmailDeliveryStatus.Sent && e.SentAt >= dayAgo),
                OldestPending = g.Where(e => e.Status == EmailDeliveryStatus.Pending).Min(e => (DateTimeOffset?)e.CreatedAt),
            })
            .FirstOrDefaultAsync(cancellationToken);
        var failures = await db.EmailDeliveries.AsNoTracking()
            .Where(e => e.Status == EmailDeliveryStatus.Failed)
            .OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .Select(e => new FailedEmailDto(e.Id, e.RecipientEmail, e.Subject, e.Attempts, e.LastError, e.CreatedAt))
            .ToListAsync(cancellationToken);
        return new SystemStatusDto(
            now,
            jobs,
            new EmailQueueDto(counts?.Pending ?? 0, counts?.Failed ?? 0, counts?.Sent ?? 0, counts?.OldestPending, failures));
    }

    private async Task RecordAsync(string jobName, DateTimeOffset started, bool succeeded, string? message, CancellationToken cancellationToken)
    {
        db.DiscardChanges();
        var run = await db.JobRuns.FirstOrDefaultAsync(j => j.JobName == jobName, cancellationToken);
        if (run is null)
        {
            run = JobRun.Create(jobName);
            db.JobRuns.Add(run);
        }

        run.Record(started, timeProvider.GetUtcNow(), succeeded, message);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            // Another instance created the row at the same moment; its record is good enough.
            db.DiscardChanges();
        }
    }
}

/// <summary>
/// Hourly clean-up (database-design.md §7): notifications older than 180 days and sent emails older than 30 days are
/// removed; failed emails are kept 90 days for Admins to inspect. Audit logs are not touched here — they are append-only
/// and purged only by the DBA script <c>deploy/db/purge-audit-logs.sql</c> (FR-AUD-06).
/// </summary>
public sealed class RetentionCleanup(IAppDbContext db, TimeProvider timeProvider)
{
    public static readonly TimeSpan NotificationRetention = TimeSpan.FromDays(180);
    public static readonly TimeSpan SentEmailRetention = TimeSpan.FromDays(30);
    public static readonly TimeSpan FailedEmailRetention = TimeSpan.FromDays(90);

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var notificationCutoff = now - NotificationRetention;
        var sentCutoff = now - SentEmailRetention;
        var failedCutoff = now - FailedEmailRetention;
        var removed = await db.Notifications.Where(n => n.CreatedAt < notificationCutoff).ExecuteDeleteAsync(cancellationToken);
        removed += await db.EmailDeliveries
            .Where(e => (e.Status == EmailDeliveryStatus.Sent && e.CreatedAt < sentCutoff) || (e.Status == EmailDeliveryStatus.Failed && e.CreatedAt < failedCutoff))
            .ExecuteDeleteAsync(cancellationToken);
        return removed;
    }
}

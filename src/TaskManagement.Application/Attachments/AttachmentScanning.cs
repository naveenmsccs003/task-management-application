using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Files;

namespace TaskManagement.Application.Attachments;

/// <summary>
/// The malware-scan job (FR-ATT-07, architecture.md §6, every 10 seconds): scans Pending files and marks them Clean or
/// Infected. An infected file's bytes are removed from storage at once; its record stays for the history. A file the
/// scanner cannot judge is retried and marked Failed after <see cref="Attachment.MaxScanAttempts"/> attempts.
/// </summary>
public sealed partial class AttachmentScanning(
    IAppDbContext db, IFileStorage storage, IMalwareScanner scanner, IAuditLog auditLog, TimeProvider timeProvider, ILogger<AttachmentScanning> logger)
{
    public const int BatchSize = 20;

    /// <summary>Drafts that were never attached to a comment, algorithm or review are removed after this long.</summary>
    public static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(24);

    public async Task<int> ScanPendingAsync(CancellationToken cancellationToken)
    {
        if (!scanner.IsEnabled)
        {
            return 0;
        }

        var pending = await db.Attachments
            .Where(a => a.ScanStatus == AttachmentScanStatus.Pending)
            .OrderBy(a => a.UploadedAt)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);
        foreach (var attachment in pending)
        {
            var now = timeProvider.GetUtcNow();
            try
            {
                MalwareScanVerdict verdict;
                await using (var content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken))
                {
                    verdict = await scanner.ScanAsync(content, cancellationToken);
                }

                if (verdict == MalwareScanVerdict.Clean)
                {
                    attachment.MarkClean(now);
                }
                else
                {
                    attachment.MarkInfected(now);
                    await storage.DeleteAsync(attachment.StorageKey, cancellationToken);
                    LogInfected(logger, attachment.Id);
                    auditLog.Record("attachment.infected", "Attachment", attachment.Id.ToString(), new AuditChanges()
                        .Set("taskId", attachment.TaskId)
                        .Set("fileName", attachment.OriginalFileName)
                        .Set("uploadedBy", attachment.UploadedBy), attachment.ProjectId);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                attachment.RecordScanError(now);
                LogScanError(logger, exception, attachment.Id, attachment.ScanAttempts);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }

    /// <summary>Removes drafts older than <see cref="DraftLifetime"/>, with their stored bytes. They were never part of a record.</summary>
    public async Task<int> RemoveStaleDraftsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - DraftLifetime;
        var drafts = await db.Attachments.Where(a => a.OwnerId == null && a.UploadedAt < cutoff).Take(500).ToListAsync(cancellationToken);
        foreach (var draft in drafts)
        {
            await storage.DeleteAsync(draft.StorageKey, cancellationToken);
            db.Attachments.Remove(draft);
        }

        if (drafts.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return drafts.Count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attachment {AttachmentId} was found to contain malware; its content was removed.")]
    private static partial void LogInfected(ILogger logger, Guid attachmentId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attachment {AttachmentId} could not be scanned (attempt {Attempt}).")]
    private static partial void LogScanError(ILogger logger, Exception exception, Guid attachmentId, int attempt);
}

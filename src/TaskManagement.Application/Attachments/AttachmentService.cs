using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Files;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Attachments;

public sealed record AttachmentDto(
    Guid Id,
    AttachmentOwnerType OwnerType,
    Guid? OwnerId,
    string FileName,
    string ContentType,
    string FileType,
    FileCategory Category,
    long SizeBytes,
    AttachmentScanStatus ScanStatus,
    UserRef? UploadedBy,
    DateTimeOffset UploadedAt,
    bool CanPreview,
    bool CanDelete);

public sealed record AttachmentTypeDto(string Code, FileCategory Category, IReadOnlyList<string> Extensions, long MaxBytes);

/// <summary>GET /attachment-rules: what the upload form may offer (allowed types and their size limits).</summary>
public sealed record AttachmentRulesDto(IReadOnlyList<AttachmentTypeDto> AllowedTypes, int MaxFilesPerRecord);

/// <summary>A file ready to be sent: <see cref="Inline"/> only for scanned images and videos opened as a preview.</summary>
public sealed record AttachmentContent(Stream Content, string ContentType, string FileName, bool Inline);

/// <summary>
/// Uploads, lists, downloads and deletes attachments (FR-ATT-01..08). Seeing a file follows the view permission of the
/// record it belongs to; anything not visible is "not found" (AC-ATT-1). Uploads are streamed to a temporary file with
/// the size limit enforced while reading (NFR-PERF-03), checked by content signature (FR-ATT-03), stored under a random
/// key and held as Pending until the malware scan passes (FR-ATT-07).
/// </summary>
public sealed class AttachmentService(
    IAppDbContext db, TaskAccess access, SettingsService settings, IFileStorage storage, IMalwareScanner scanner, IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public const int MaxFilesPerRecord = 10;
    private const int BufferSize = 81_920;

    public async Task<AttachmentRulesDto> RulesAsync(CancellationToken cancellationToken)
    {
        var rules = await settings.GetAttachmentsAsync(cancellationToken);
        var types = FileTypes.All.Where(t => rules.AllowedTypes.Contains(t.Code))
            .Select(t => new AttachmentTypeDto(t.Code, t.Category, t.Extensions, rules.MaxBytes(t.Category)))
            .ToList();
        return new AttachmentRulesDto(types, MaxFilesPerRecord);
    }

    /// <summary>
    /// Checks that the caller may attach a file of <paramref name="ownerType"/> to the task, before any of the file is
    /// read. Task files are linked to the task at once; the other kinds become drafts for the caller's next comment,
    /// algorithm or review submission.
    /// </summary>
    public async Task<Result<UploadTarget>> PrepareUploadAsync(Guid taskId, AttachmentOwnerType? ownerType, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (ownerType is not { } type || !Enum.IsDefined(type))
        {
            return Error.Validation("attachment.owner_type", "Say what the file is attached to: Task, Comment, Algorithm or Review.");
        }

        var allowed = visible.Allows(PermissionCatalog.AttachmentsUpload) && type switch
        {
            AttachmentOwnerType.Comment => TaskAccess.CanComment(visible),
            AttachmentOwnerType.Algorithm or AttachmentOwnerType.Review => visible.IsAssignee,
            _ => true,
        };
        if (!allowed)
        {
            return CommonErrors.Forbidden("attach files here");
        }

        var writable = EnsureWritable(visible);
        if (writable.IsFailure)
        {
            return writable.Error!;
        }

        return new UploadTarget(visible, type, await settings.GetAttachmentsAsync(cancellationToken));
    }

    /// <summary>Reads the file from <paramref name="body"/> (not seekable), validates it, stores it and records it.</summary>
    public async Task<Result<AttachmentDto>> UploadAsync(UploadTarget target, string? fileName, Stream body, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        var name = FileTypes.SanitizeFileName(fileName);
        var largest = target.Settings.LargestAllowedBytes();
        if (largest == 0)
        {
            return Error.UnsupportedMediaType("attachment.type_not_allowed", "No file types are allowed at the moment.");
        }

        // DeleteOnClose: an interrupted or refused upload leaves no file behind (edge case 34).
        await using var temp = new FileStream(
            Path.Combine(Path.GetTempPath(), $"tma-upload-{Guid.NewGuid():N}"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            BufferSize, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long size = 0;
        int read;
        while ((read = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            size += read;
            if (size > largest)
            {
                return TooLarge(largest);
            }

            hash.AppendData(buffer, 0, read);
            await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (size == 0)
        {
            return Error.Validation("attachment.empty", "The file is empty.");
        }

        var type = FileTypes.Detect(temp, name);
        if (type is null)
        {
            return Error.Validation(
                "attachment.type_mismatch",
                $"“{name}” is not a supported file type, or its content does not match its extension.");
        }

        if (!target.Settings.AllowedTypes.Contains(type.Code))
        {
            return Error.UnsupportedMediaType("attachment.type_not_allowed", $"{type.Code.ToUpperInvariant()} files are not allowed.");
        }

        if (size > target.Settings.MaxBytes(type.Category))
        {
            return TooLarge(target.Settings.MaxBytes(type.Category));
        }

        var now = timeProvider.GetUtcNow();
        var key = $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}";
        temp.Position = 0;
        await storage.SaveAsync(key, temp, cancellationToken);

        var task = target.Visible.Task;
        var attachment = Attachment.Create(
            task.ProjectId, task.Id, target.OwnerType, target.OwnerType == AttachmentOwnerType.Task ? task.Id : null, name, type.ContentType,
            type.Code, size, hash.GetHashAndReset(), key, scanned: !scanner.IsEnabled, target.Visible.UserId, now);
        db.Attachments.Add(attachment);
        auditLog.Record("attachment.uploaded", "Attachment", attachment.Id.ToString(), new AuditChanges()
            .Set("taskId", task.Id)
            .Set("ownerType", attachment.OwnerType)
            .Set("fileName", attachment.OriginalFileName)
            .Set("fileType", attachment.FileType)
            .Set("sizeBytes", attachment.SizeBytes), task.ProjectId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }

        var uploader = await db.Users.AsNoTracking().Where(u => u.Id == attachment.UploadedBy)
            .Select(u => new UserRef(u.Id, u.FullName)).FirstOrDefaultAsync(cancellationToken);
        return ToDto(attachment, target.Visible, uploader);
    }

    /// <summary>The task's files the caller may see (algorithm files need algorithms.view). Drafts are not listed.</summary>
    public async Task<Result<IReadOnlyList<AttachmentDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var canViewAlgorithm = visible.Allows(PermissionCatalog.AlgorithmsView);
        var attachments = await db.Attachments.AsNoTracking()
            .Where(a => a.TaskId == taskId && a.DeletedAt == null && a.OwnerId != null
                && (canViewAlgorithm || a.OwnerType != AttachmentOwnerType.Algorithm))
            .OrderBy(a => a.UploadedAt)
            .ToListAsync(cancellationToken);
        var ids = attachments.Select(a => a.UploadedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        IReadOnlyList<AttachmentDto> result = attachments.Select(a => ToDto(a, visible, names.GetValueOrDefault(a.UploadedBy))).ToList();
        return Result.Success(result);
    }

    /// <summary>
    /// Opens a file for download, or for an inline preview of a scanned image or video. Downloads are audited
    /// (FR-AUD-02); previews are not, because a video player asks for many ranges of the same file.
    /// </summary>
    public async Task<Result<AttachmentContent>> OpenAsync(Guid attachmentId, bool preview, CancellationToken cancellationToken)
    {
        var found = await LoadVisibleAsync(attachmentId, track: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (attachment, visible) = found.Value;
        var problem = attachment.ScanStatus switch
        {
            AttachmentScanStatus.Pending => Error.Conflict("attachment.not_scanned", "This file is still being checked for malware. Try again in a moment."),
            AttachmentScanStatus.Infected => Error.Conflict("attachment.infected", "This file was found to contain malware and cannot be downloaded."),
            AttachmentScanStatus.Failed => Error.Conflict("attachment.scan_failed", "This file could not be checked for malware, so it cannot be downloaded."),
            _ => null,
        };
        if (problem is not null)
        {
            return problem;
        }

        if (preview && FileTypes.Find(attachment.FileType) is not { Previewable: true })
        {
            return Error.Validation("attachment.no_preview", "This file type cannot be previewed. Download it instead.");
        }

        Stream content;
        try
        {
            content = await storage.OpenReadAsync(attachment.StorageKey, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return Error.NotFound("attachment.content_missing", "The file's content is missing from storage. Ask an administrator.");
        }

        if (!preview)
        {
            auditLog.Record("attachment.downloaded", "Attachment", attachment.Id.ToString(), new AuditChanges()
                .Set("taskId", attachment.TaskId)
                .Set("fileName", attachment.OriginalFileName), visible.Task.ProjectId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new AttachmentContent(content, attachment.ContentType, attachment.OriginalFileName, preview);
    }

    /// <summary>
    /// Soft delete (FR-ATT-08): TLs and PMs (project-wide attachments.delete) always; the uploader with attachments.delete
    /// while the task is not Closed. Drafts can always be removed by their uploader.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken)
    {
        var found = await LoadVisibleAsync(attachmentId, track: true, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (attachment, visible) = found.Value;
        if (!CanDelete(attachment, visible))
        {
            return CommonErrors.Forbidden("delete this file");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        attachment.Delete(visible.UserId, timeProvider.GetUtcNow());
        auditLog.Record("attachment.deleted", "Attachment", attachment.Id.ToString(), new AuditChanges()
            .Set("taskId", attachment.TaskId)
            .Set("fileName", attachment.OriginalFileName)
            .Set("uploadedBy", attachment.UploadedBy), visible.Task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Links the caller's drafts to the comment, algorithm or review submission being saved. The caller saves; on failure
    /// nothing is saved.
    /// </summary>
    public async Task<Result> LinkDraftsAsync(
        Guid taskId, AttachmentOwnerType ownerType, Guid ownerId, IReadOnlyList<Guid>? attachmentIds, Guid userId, CancellationToken cancellationToken)
    {
        var ids = (attachmentIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
        {
            return Result.Success();
        }

        if (ids.Count > MaxFilesPerRecord)
        {
            return Error.Validation("attachment.too_many", $"Attach at most {MaxFilesPerRecord} files.");
        }

        var drafts = await db.Attachments.Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        if (drafts.Count != ids.Count)
        {
            return Error.Validation("attachment.not_linkable", "One of the files is no longer available. Upload it again.");
        }

        foreach (var draft in drafts)
        {
            var linked = draft.LinkTo(ownerType, ownerId, taskId, userId);
            if (linked.IsFailure)
            {
                return linked;
            }
        }

        return Result.Success();
    }

    /// <summary>A deleted comment takes its files with it (they were part of what was said).</summary>
    public async Task DeleteForOwnerAsync(AttachmentOwnerType ownerType, Guid ownerId, Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var attachments = await db.Attachments.Where(a => a.OwnerType == ownerType && a.OwnerId == ownerId && a.DeletedAt == null).ToListAsync(cancellationToken);
        foreach (var attachment in attachments)
        {
            attachment.Delete(userId, now);
        }
    }

    private async Task<Result<(Attachment Attachment, VisibleTask Visible)>> LoadVisibleAsync(Guid attachmentId, bool track, CancellationToken cancellationToken)
    {
        var query = track ? db.Attachments : db.Attachments.AsNoTracking();
        var attachment = await query.FirstOrDefaultAsync(a => a.Id == attachmentId && a.DeletedAt == null, cancellationToken);
        var visible = attachment is null ? null : await access.LoadAsync(attachment.TaskId, track: false, cancellationToken);
        if (attachment is null || visible is null
            || (attachment.IsDraft && attachment.UploadedBy != visible.UserId)
            || (attachment.OwnerType == AttachmentOwnerType.Algorithm && !attachment.IsDraft && !visible.Allows(PermissionCatalog.AlgorithmsView)))
        {
            return CommonErrors.NotFound("Attachment");
        }

        return (attachment, visible);
    }

    private static bool CanDelete(Attachment attachment, VisibleTask visible)
    {
        if (attachment.IsDraft)
        {
            return attachment.UploadedBy == visible.UserId;
        }

        if (visible.Permissions.HasProjectWide(PermissionCatalog.AttachmentsDelete))
        {
            return true;
        }

        return attachment.UploadedBy == visible.UserId
            && visible.Allows(PermissionCatalog.AttachmentsDelete)
            && visible.Task.Status != TaskItemStatus.Closed;
    }

    private static AttachmentDto ToDto(Attachment attachment, VisibleTask visible, UserRef? uploader)
    {
        var type = FileTypes.Find(attachment.FileType);
        return new AttachmentDto(
            attachment.Id,
            attachment.OwnerType,
            attachment.OwnerId,
            attachment.OriginalFileName,
            attachment.ContentType,
            attachment.FileType,
            type?.Category ?? FileCategory.Document,
            attachment.SizeBytes,
            attachment.ScanStatus,
            uploader,
            attachment.UploadedAt,
            type is { Previewable: true } && attachment.ScanStatus == AttachmentScanStatus.Clean,
            !visible.Project.IsReadOnly && CanDelete(attachment, visible));
    }

    /// <summary>Attachments are part of the record: read-only projects and closed or cancelled tasks take no new files.</summary>
    private static Result EnsureWritable(VisibleTask visible)
    {
        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        return TaskWorkflow.IsFinal(visible.Task.Status)
            ? Error.Conflict("task.read_only", $"Task {visible.Task.Key} is {TaskWorkflow.Describe(visible.Task.Status).ToLowerInvariant()}, so no files can be added.")
            : Result.Success();
    }

    private static Error TooLarge(long maxBytes) =>
        Error.TooLarge("attachment.too_large", $"The file is too large. The limit for this type is {maxBytes / (1024 * 1024)} MB.");
}

/// <summary>Where an upload goes, decided before its body is read.</summary>
public sealed record UploadTarget(VisibleTask Visible, AttachmentOwnerType OwnerType, AttachmentSettings Settings);

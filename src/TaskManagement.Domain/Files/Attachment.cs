using TaskManagement.Domain.Common;

namespace TaskManagement.Domain.Files;

/// <summary>The record a file belongs to (FR-ATT-01). Viewing the file follows that record's view permission.</summary>
public enum AttachmentOwnerType
{
    Task,
    Comment,
    Algorithm,
    Review,
}

/// <summary>Malware scan state (FR-ATT-07, Q9). Only <see cref="Clean"/> files can be downloaded.</summary>
public enum AttachmentScanStatus
{
    Pending,
    Clean,
    Infected,
    Failed,
}

/// <summary>
/// A file attached to a task, comment, algorithm submission or review submission (FR-ATT-01..08). The bytes live in
/// private storage under a server-generated key; the original name is kept only as sanitized metadata (FR-ATT-04).
/// Comment, algorithm and review files are uploaded first as drafts (no owner yet) and linked when that record is
/// saved, because those records are created in one step and are immutable afterwards.
/// </summary>
public sealed class Attachment
{
    public const int FileNameMaxLength = 255;
    public const int MaxScanAttempts = 5;

    private Attachment()
    {
        OriginalFileName = string.Empty;
        ContentType = string.Empty;
        FileType = string.Empty;
        StorageKey = string.Empty;
        Sha256 = [];
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid TaskId { get; private set; }

    public AttachmentOwnerType OwnerType { get; private set; }

    /// <summary>The owning record; null while a comment, algorithm or review file is still a draft.</summary>
    public Guid? OwnerId { get; private set; }

    public string OriginalFileName { get; private set; }

    /// <summary>Detected from the file's content, never taken from the browser (FR-ATT-03).</summary>
    public string ContentType { get; private set; }

    /// <summary>The detected type code, e.g. <c>pdf</c> or <c>png</c>.</summary>
    public string FileType { get; private set; }

    public long SizeBytes { get; private set; }

    public byte[] Sha256 { get; private set; }

    public string StorageKey { get; private set; }

    public AttachmentScanStatus ScanStatus { get; private set; }

    public int ScanAttempts { get; private set; }

    public DateTimeOffset? ScannedAt { get; private set; }

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public bool IsDeleted => DeletedAt is not null;

    public bool IsDraft => OwnerId is null;

    public static Attachment Create(
        Guid projectId, Guid taskId, AttachmentOwnerType ownerType, Guid? ownerId, string fileName, string contentType, string fileType,
        long sizeBytes, byte[] sha256, string storageKey, bool scanned, Guid uploadedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentNullException.ThrowIfNull(sha256);
        if (ownerType == AttachmentOwnerType.Task && ownerId != taskId)
        {
            throw new ArgumentException("A task attachment is owned by its task.", nameof(ownerId));
        }

        return new Attachment
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            TaskId = taskId,
            OwnerType = ownerType,
            OwnerId = ownerId,
            OriginalFileName = fileName,
            ContentType = contentType,
            FileType = fileType,
            SizeBytes = sizeBytes,
            Sha256 = sha256,
            StorageKey = storageKey,
            ScanStatus = scanned ? AttachmentScanStatus.Clean : AttachmentScanStatus.Pending,
            ScannedAt = scanned ? now : null,
            UploadedBy = uploadedBy,
            UploadedAt = now,
        };
    }

    /// <summary>Links a draft to the comment, algorithm or review submission it was uploaded for.</summary>
    public Result LinkTo(AttachmentOwnerType ownerType, Guid ownerId, Guid taskId, Guid userId)
    {
        if (IsDeleted || !IsDraft || OwnerType != ownerType || TaskId != taskId || UploadedBy != userId)
        {
            return Error.Validation("attachment.not_linkable", $"“{OriginalFileName}” cannot be attached here. Upload it again.");
        }

        OwnerId = ownerId;
        return Result.Success();
    }

    public void MarkClean(DateTimeOffset now)
    {
        ScanStatus = AttachmentScanStatus.Clean;
        ScannedAt = now;
    }

    public void MarkInfected(DateTimeOffset now)
    {
        ScanStatus = AttachmentScanStatus.Infected;
        ScannedAt = now;
    }

    /// <summary>Counts a scan that could not complete; after <see cref="MaxScanAttempts"/> the file is marked Failed.</summary>
    public void RecordScanError(DateTimeOffset now)
    {
        ScanAttempts++;
        if (ScanAttempts >= MaxScanAttempts)
        {
            ScanStatus = AttachmentScanStatus.Failed;
            ScannedAt = now;
        }
    }

    /// <summary>Soft delete (FR-ATT-08). The stored bytes are kept so the history stays complete.</summary>
    public void Delete(Guid userId, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return;
        }

        DeletedAt = now;
        DeletedBy = userId;
    }
}

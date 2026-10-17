using TaskManagement.Application.Attachments;
using TaskManagement.Application.Common;

namespace TaskManagement.Application.Settings;

/// <summary>Allowed file types and size limits (FR-ATT-02, FR-ROLE-08), editable by Admins.</summary>
public sealed record AttachmentSettings
{
    public const string Key = "attachments";

    /// <summary>No category may go above this, whatever is configured (request body limit of the upload endpoint).</summary>
    public const int HardMaxMb = 1_024;

    public int ImageMaxMb { get; init; } = 10;

    public int DocumentMaxMb { get; init; } = 25;

    public int VideoMaxMb { get; init; } = 200;

    /// <summary>Type codes from <see cref="FileTypes.All"/>; all of them by default.</summary>
    public IReadOnlyList<string> AllowedTypes { get; init; } = FileTypes.All.Select(t => t.Code).ToList();

    public long MaxBytes(FileCategory category) => category switch
    {
        FileCategory.Image => ImageMaxMb,
        FileCategory.Document => DocumentMaxMb,
        _ => VideoMaxMb,
    } * 1024L * 1024L;

    /// <summary>The largest file any allowed type may be; an upload is cut off as soon as it passes this.</summary>
    public long LargestAllowedBytes() =>
        FileTypes.All.Where(t => AllowedTypes.Contains(t.Code)).Select(t => MaxBytes(t.Category)).DefaultIfEmpty(0).Max();

    internal FieldErrors Validate()
    {
        var errors = new FieldErrors();
        if (ImageMaxMb is < 1 or > 100)
        {
            errors.Add("imageMaxMb", "Must be between 1 and 100.");
        }

        if (DocumentMaxMb is < 1 or > 500)
        {
            errors.Add("documentMaxMb", "Must be between 1 and 500.");
        }

        if (VideoMaxMb is < 1 or > HardMaxMb)
        {
            errors.Add("videoMaxMb", $"Must be between 1 and {HardMaxMb}.");
        }

        var unknown = (AllowedTypes ?? []).Where(code => FileTypes.Find(code) is null).ToList();
        if (AllowedTypes is null || unknown.Count > 0)
        {
            errors.Add("allowedTypes", unknown.Count > 0 ? $"Unknown file types: {string.Join(", ", unknown)}." : "Choose the allowed file types.");
        }

        return errors;
    }
}

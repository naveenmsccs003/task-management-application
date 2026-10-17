namespace TaskManagement.Infrastructure.Files;

public enum MalwareScannerKind
{
    /// <summary>ClamAV's clamd over TCP (testing and production, Q9).</summary>
    ClamAv,

    /// <summary>No scanning: files are clean as soon as they are uploaded. Development only; refused in Production.</summary>
    None,
}

/// <summary>Configuration section <c>Attachments</c> (docs/deployment.md).</summary>
public sealed class AttachmentOptions
{
    public const string Section = "Attachments";

    /// <summary>Folder for stored files, outside the web root. Relative paths are resolved from the content root.</summary>
    public string LocalPath { get; set; } = string.Empty;

    public MalwareScannerKind Scanner { get; set; } = MalwareScannerKind.ClamAv;

    public string ClamAvHost { get; set; } = "localhost";

    public int ClamAvPort { get; set; } = 3310;

    public TimeSpan ClamAvTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Uploads per member per minute (NFR-SEC-05).</summary>
    public int UploadsPerMinute { get; set; } = 30;

    public bool ScanWorkerEnabled { get; set; } = true;
}

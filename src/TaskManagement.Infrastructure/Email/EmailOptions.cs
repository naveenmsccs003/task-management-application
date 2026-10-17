namespace TaskManagement.Infrastructure.Email;

public enum EmailTransportKind
{
    /// <summary>Send through an SMTP server (testing and production).</summary>
    Smtp,

    /// <summary>Write each email as an .eml file to a folder (local development without a mail server).</summary>
    PickupDirectory,
}

/// <summary>Bound from the <c>Email</c> configuration section. SMTP credentials come from secrets, never from appsettings.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    public EmailTransportKind Transport { get; set; } = EmailTransportKind.Smtp;

    public string FromAddress { get; set; } = "no-reply@localhost";

    public string FromName { get; set; } = "Task Management";

    public string PickupDirectory { get; set; } = "storage/mail";

    public string SmtpHost { get; set; } = "localhost";

    public int SmtpPort { get; set; } = 1025;

    /// <summary>StartTls, SslOnConnect or None (None only for a local mail catcher).</summary>
    public string SmtpSecurity { get; set; } = "StartTls";

    public string? SmtpUserName { get; set; }

    public string? SmtpPassword { get; set; }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Turns the background sending loop off (integration tests send explicitly and deterministically).</summary>
    public bool BackgroundWorkerEnabled { get; set; } = true;
}

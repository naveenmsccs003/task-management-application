namespace TaskManagement.Domain.Notifications;

public enum EmailDeliveryStatus
{
    Pending,
    Sent,
    Failed,
}

/// <summary>
/// A queued email. It is saved in the same transaction as the business change that caused it,
/// and a background worker sends it with retries (FR-NOT-04, FR-NOT-05).
/// </summary>
public sealed class EmailDelivery
{
    public const int MaxAttempts = 8;

    private EmailDelivery()
    {
        RecipientEmail = string.Empty;
        Subject = string.Empty;
        BodyText = string.Empty;
        BodyHtml = string.Empty;
    }

    public Guid Id { get; private set; }

    public string RecipientEmail { get; private set; }

    public string Subject { get; private set; }

    public string BodyText { get; private set; }

    public string BodyHtml { get; private set; }

    /// <summary>Bodies containing secrets (password links) are wiped once sent or abandoned.</summary>
    public bool ContainsSecret { get; private set; }

    public EmailDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public string? LastError { get; private set; }

    public static EmailDelivery Queue(
        string recipientEmail, string subject, string bodyText, string bodyHtml, bool containsSecret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new EmailDelivery
        {
            Id = Guid.CreateVersion7(now),
            RecipientEmail = recipientEmail,
            Subject = subject,
            BodyText = bodyText,
            BodyHtml = bodyHtml,
            ContainsSecret = containsSecret,
            Status = EmailDeliveryStatus.Pending,
            CreatedAt = now,
            NextAttemptAt = now,
        };
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = EmailDeliveryStatus.Sent;
        SentAt = now;
        Attempts++;
        LastError = null;
        WipeSecretBody();
    }

    /// <summary>Exponential backoff: 30 s, 1 min, 2 min, … up to <see cref="MaxAttempts"/> attempts.</summary>
    public void MarkAttemptFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        if (Attempts >= MaxAttempts)
        {
            Status = EmailDeliveryStatus.Failed;
            WipeSecretBody();
            return;
        }

        NextAttemptAt = now + TimeSpan.FromSeconds(30 * Math.Pow(2, Attempts - 1));
    }

    private void WipeSecretBody()
    {
        if (ContainsSecret)
        {
            BodyText = "[removed after delivery]";
            BodyHtml = "[removed after delivery]";
        }
    }
}

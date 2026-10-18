namespace TaskManagement.Domain.Notifications;

/// <summary>
/// Groups of notifications a member can switch email on or off for (FR-NOT-02). <see cref="Account"/> is mandatory:
/// security emails are always sent.
/// </summary>
public enum NotificationCategory
{
    Account,
    Assignment,
    Algorithm,
    Time,
    Review,
    Requests,
    Deadlines,
    GitLab,
}

/// <summary>The events that produce notifications (FR-NOT-03).</summary>
public enum NotificationType
{
    TaskAssigned,
    AlgorithmSubmitted,
    AlgorithmApproved,
    AlgorithmRejected,
    AllocationWarning,
    AllocationReached,
    SessionAutoClosed,
    SubmittedForReview,
    ReviewApproved,
    RevisionRequested,
    TaskClosed,
    TaskReopened,
    ReassignmentRequested,
    ReassignmentDecided,
    TaskReassigned,
    ExtensionRequested,
    ExtensionDecided,
    DelayReported,
    DueSoon,
    Overdue,
    GitLabIssueClosed,
    GitLabIssueMissing,
}

public static class NotificationCatalog
{
    public static NotificationCategory CategoryOf(NotificationType type) => type switch
    {
        NotificationType.TaskAssigned => NotificationCategory.Assignment,
        NotificationType.AlgorithmSubmitted or NotificationType.AlgorithmApproved or NotificationType.AlgorithmRejected => NotificationCategory.Algorithm,
        NotificationType.AllocationWarning or NotificationType.AllocationReached or NotificationType.SessionAutoClosed => NotificationCategory.Time,
        NotificationType.SubmittedForReview or NotificationType.ReviewApproved or NotificationType.RevisionRequested
            or NotificationType.TaskClosed or NotificationType.TaskReopened => NotificationCategory.Review,
        NotificationType.ReassignmentRequested or NotificationType.ReassignmentDecided or NotificationType.TaskReassigned
            or NotificationType.ExtensionRequested or NotificationType.ExtensionDecided => NotificationCategory.Requests,
        NotificationType.DelayReported or NotificationType.DueSoon or NotificationType.Overdue => NotificationCategory.Deadlines,
        NotificationType.GitLabIssueClosed or NotificationType.GitLabIssueMissing => NotificationCategory.GitLab,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown notification type."),
    };

    /// <summary>Categories whose emails cannot be turned off.</summary>
    public static bool IsMandatory(NotificationCategory category) => category == NotificationCategory.Account;
}

/// <summary>
/// An in-app notification for one member (FR-NOT-01). It is saved in the same transaction as the change that caused it
/// (FR-NOT-04). Text and links must only describe what the recipient may see (FR-NOT-06).
/// </summary>
public sealed class Notification
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 1_000;
    public const int LinkMaxLength = 300;

    private Notification()
    {
        Title = string.Empty;
        Body = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid RecipientId { get; private set; }

    public NotificationType Type { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>Relative app URL, e.g. <c>/tasks/{id}</c>.</summary>
    public string? Link { get; private set; }

    public Guid? ProjectId { get; private set; }

    public Guid? TaskId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    public static Notification Create(
        Guid recipientId, NotificationType type, string title, string body, string? link, Guid? projectId, Guid? taskId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (link is not null && !link.StartsWith('/'))
        {
            throw new ArgumentException("Links must be relative app URLs starting with '/'.", nameof(link));
        }

        return new Notification
        {
            Id = Guid.CreateVersion7(now),
            RecipientId = recipientId,
            Type = type,
            Title = Truncate(title.Trim(), TitleMaxLength),
            Body = Truncate(body.Trim(), BodyMaxLength),
            Link = link,
            ProjectId = projectId,
            TaskId = taskId,
            CreatedAt = now,
        };
    }

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}

/// <summary>A member's email choice for one category. A missing row means email is on (the default).</summary>
public sealed class NotificationPreference
{
    private NotificationPreference()
    {
    }

    public Guid UserId { get; private set; }

    public NotificationCategory Category { get; private set; }

    public bool EmailEnabled { get; private set; }

    public static NotificationPreference Create(Guid userId, NotificationCategory category, bool emailEnabled)
    {
        if (!emailEnabled && NotificationCatalog.IsMandatory(category))
        {
            throw new ArgumentException($"Email for {category} notifications cannot be turned off.", nameof(emailEnabled));
        }

        return new NotificationPreference { UserId = userId, Category = category, EmailEnabled = emailEnabled };
    }

    public void SetEmailEnabled(bool enabled)
    {
        if (!enabled && NotificationCatalog.IsMandatory(Category))
        {
            throw new InvalidOperationException($"Email for {Category} notifications cannot be turned off.");
        }

        EmailEnabled = enabled;
    }
}

using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Application.Notifications;

/// <summary>
/// Pushes newly saved notifications to connected browsers (FR-NOT-07). Called after the transaction commits; delivery is
/// best effort — the notification is already stored, and the browser also polls.
/// </summary>
public interface INotificationPublisher
{
    Task PublishAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken);
}

/// <summary>
/// Creates in-app notifications and their emails inside the caller's unit of work, so they commit together with the
/// business change (FR-NOT-04) — the notification and email rows are the outbox. Emails respect each member's category
/// preferences (FR-NOT-02); deactivated members and the person who caused the event are skipped.
/// </summary>
public sealed class Notifier(IAppDbContext db, ICurrentUser currentUser, IOptions<AppOptions> options, TimeProvider timeProvider)
{
    /// <summary>Adds one notification per recipient. The caller saves.</summary>
    public async Task NotifyAsync(
        IEnumerable<Guid> recipients,
        NotificationType type,
        string title,
        string body,
        string? link,
        Guid? projectId,
        Guid? taskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recipients);
        var actor = currentUser.UserId;
        var ids = recipients.Where(id => id != actor).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var category = NotificationCatalog.CategoryOf(type);
        var mandatory = NotificationCatalog.IsMandatory(category);
        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                EmailEnabled = mandatory
                    || !db.NotificationPreferences.Any(p => p.UserId == u.Id && p.Category == category && !p.EmailEnabled),
            })
            .ToListAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();
        foreach (var user in users)
        {
            db.Notifications.Add(Notification.Create(user.Id, type, title, body, link, projectId, taskId, now));
            if (user.EmailEnabled)
            {
                db.EmailDeliveries.Add(BuildEmail(user.Email, user.FullName, title, body, link, now));
            }
        }
    }

    /// <summary>Active Team Leaders of a project — the "project TL(s)" that workflow events notify.</summary>
    public async Task<IReadOnlyList<Guid>> ProjectLeadsAsync(Guid projectId, CancellationToken cancellationToken) =>
        await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.RemovedAt == null && m.ProjectRoleId == SystemRoles.TeamLeaderId)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    private EmailDelivery BuildEmail(string email, string fullName, string title, string body, string? link, DateTimeOffset now)
    {
        var product = options.Value.ProductName;
        var url = link is null ? null : options.Value.PublicBaseUrl.TrimEnd('/') + link;
        var text = $"""
            Hello {fullName},

            {body}
            {(url is null ? string.Empty : $"\nOpen: {url}\n")}
            You can change which emails you receive in your profile.

            {product}
            """;
        var html = $"""
            <p>Hello {WebUtility.HtmlEncode(fullName)},</p>
            <p>{WebUtility.HtmlEncode(body)}</p>
            {(url is null ? string.Empty : $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">Open in {WebUtility.HtmlEncode(product)}</a></p>")}
            <p>You can change which emails you receive in your profile.</p>
            """;
        return EmailDelivery.Queue(email, $"{title} · {product}", text, html, containsSecret: false, now);
    }
}

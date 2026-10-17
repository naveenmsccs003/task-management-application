using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskManagement.Application.Notifications;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Api.Notifications;

/// <summary>
/// Real-time channel for in-app notifications (FR-NOT-07). The browser only listens; it never sends through the hub.
/// Messages go to the signed-in member's own connections (SignalR's user id is the member id claim).
/// </summary>
[Authorize]
internal sealed class NotificationHub : Hub
{
    public const string Path = "/hubs/notifications";
    public const string NotificationMethod = "notification";
}

/// <summary>Sends each new notification to its recipient's open connections.</summary>
internal sealed class SignalRNotificationPublisher(IHubContext<NotificationHub> hub) : INotificationPublisher
{
    public async Task PublishAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        foreach (var n in notifications)
        {
            var dto = new NotificationDto(n.Id, n.Type, NotificationCatalog.CategoryOf(n.Type), n.Title, n.Body, n.Link, n.CreatedAt, n.IsRead);
            await hub.Clients.User(n.RecipientId.ToString()).SendAsync(NotificationHub.NotificationMethod, dto, cancellationToken);
        }
    }
}

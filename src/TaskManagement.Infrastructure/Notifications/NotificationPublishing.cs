using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using TaskManagement.Application.Notifications;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Infrastructure.Notifications;

/// <summary>Used when no real-time channel is registered (CLI commands, background-only hosts).</summary>
internal sealed class NullNotificationPublisher : INotificationPublisher
{
    public Task PublishAsync(IReadOnlyList<Notification> notifications, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// After a save that added notifications succeeds, hands them to the <see cref="INotificationPublisher"/> (FR-NOT-07).
/// Pushing is best effort: a failure is logged and the browser's polling picks the notification up. Saves inside an
/// explicit transaction are pushed before the commit; none of those currently create notifications.
/// </summary>
internal sealed partial class NotificationPublishingInterceptor(INotificationPublisher publisher, ILogger<NotificationPublishingInterceptor> logger)
    : SaveChangesInterceptor
{
    private List<Notification> _pending = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        _pending = eventData.Context?.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList() ?? [];
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        var saved = _pending;
        _pending = [];
        if (saved.Count > 0)
        {
            try
            {
                await publisher.PublishAsync(saved, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPublishFailed(logger, saved.Count, exception.GetType().Name);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending = [];
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pushing {Count} notifications failed: {ErrorType}. Clients will pick them up by polling.")]
    private static partial void LogPublishFailed(ILogger logger, int count, string errorType);
}

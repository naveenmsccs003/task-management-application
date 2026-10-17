using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Application.Notifications;

public sealed record NotificationDto(
    Guid Id, NotificationType Type, NotificationCategory Category, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool IsRead);

public sealed record NotificationPreferenceDto(NotificationCategory Category, bool EmailEnabled, bool Mandatory);

public sealed record SaveNotificationPreferencesCommand(IReadOnlyList<NotificationPreferenceInput>? Preferences);

public sealed record NotificationPreferenceInput(NotificationCategory Category, bool EmailEnabled);

/// <summary>The signed-in member's own notifications and email preferences (FR-NOT-01, FR-NOT-02). Never another member's.</summary>
public sealed class NotificationService(IAppDbContext db, ICurrentUser currentUser, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var userId = currentUser.RequiredUserId;
        var query = db.Notifications.AsNoTracking().Where(n => n.RecipientId == userId);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(page.Skip).Take(page.SafePageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<NotificationDto>(rows.Select(ToDto).ToList(), page.SafePage, page.SafePageSize, total);
    }

    public Task<int> UnreadCountAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        return db.Notifications.CountAsync(n => n.RecipientId == userId && n.ReadAt == null, cancellationToken);
    }

    /// <summary>Someone else's notification answers 404, like any record the caller cannot see.</summary>
    public async Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.RecipientId == userId, cancellationToken);
        if (notification is null)
        {
            return CommonErrors.NotFound("Notification");
        }

        notification.MarkRead(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<int> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var now = timeProvider.GetUtcNow();
        return await db.Notifications
            .Where(n => n.RecipientId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, now), cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> PreferencesAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var stored = await db.NotificationPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, p => p.EmailEnabled, cancellationToken);
        return Enum.GetValues<NotificationCategory>()
            .Select(c => new NotificationPreferenceDto(c, NotificationCatalog.IsMandatory(c) || stored.GetValueOrDefault(c, true), NotificationCatalog.IsMandatory(c)))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> SavePreferencesAsync(
        SaveNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var requested = command.Preferences ?? [];
        var errors = new FieldErrors();
        foreach (var input in requested)
        {
            if (!Enum.IsDefined(input.Category))
            {
                errors.Add("preferences", "Unknown notification category.");
            }
            else if (!input.EmailEnabled && NotificationCatalog.IsMandatory(input.Category))
            {
                errors.Add("preferences", $"{input.Category} emails cannot be turned off.");
            }
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var userId = currentUser.RequiredUserId;
        var existing = await db.NotificationPreferences.Where(p => p.UserId == userId).ToDictionaryAsync(p => p.Category, cancellationToken);
        var changes = new AuditChanges();
        foreach (var input in requested.GroupBy(i => i.Category).Select(g => g.Last()))
        {
            if (existing.TryGetValue(input.Category, out var preference))
            {
                changes.Change(input.Category.ToString(), preference.EmailEnabled, input.EmailEnabled);
                preference.SetEmailEnabled(input.EmailEnabled);
            }
            else
            {
                changes.Change(input.Category.ToString(), true, input.EmailEnabled);
                db.NotificationPreferences.Add(NotificationPreference.Create(userId, input.Category, input.EmailEnabled));
            }
        }

        if (!changes.IsEmpty)
        {
            auditLog.Record("member.notification_preferences_changed", "User", userId.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(await PreferencesAsync(cancellationToken));
    }

    private static NotificationDto ToDto(Notification n) =>
        new(n.Id, n.Type, NotificationCatalog.CategoryOf(n.Type), n.Title, n.Body, n.Link, n.CreatedAt, n.IsRead);
}

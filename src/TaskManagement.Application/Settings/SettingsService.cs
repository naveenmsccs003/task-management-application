using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Settings;

namespace TaskManagement.Application.Settings;

public sealed record SettingsDto(AuthSettings Auth, WorkSettings Work);

/// <summary>
/// Typed, cached access to global settings. Values are read at the moment they are used (e.g. during sign-in),
/// so Admin changes apply without a restart. Missing rows fall back to the defaults in the settings records.
/// </summary>
public sealed class SettingsService(IAppDbContext db, IMemoryCache cache, IAuditLog auditLog, TimeProvider timeProvider, ICurrentUser currentUser)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<AuthSettings> GetAuthAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey(AuthSettings.Key), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var row = await db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == AuthSettings.Key, cancellationToken);
            return row is null ? new AuthSettings() : JsonSerializer.Deserialize<AuthSettings>(row.ValueJson, JsonOptions) ?? new AuthSettings();
        }) ?? new AuthSettings();

    public async Task<WorkSettings> GetWorkAsync(CancellationToken cancellationToken) =>
        await cache.GetOrCreateAsync(CacheKey(WorkSettings.Key), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var row = await db.GlobalSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == WorkSettings.Key, cancellationToken);
            return row is null ? new WorkSettings() : JsonSerializer.Deserialize<WorkSettings>(row.ValueJson, JsonOptions) ?? new WorkSettings();
        }) ?? new WorkSettings();

    public async Task<SettingsDto> GetAllAsync(CancellationToken cancellationToken) =>
        new(await GetAuthAsync(cancellationToken), await GetWorkAsync(cancellationToken));

    public async Task<Result<WorkSettings>> UpdateWorkAsync(WorkSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var previous = await GetWorkAsync(cancellationToken);
        await SaveAsync(WorkSettings.Key, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
        auditLog.Record("settings.updated", "Setting", WorkSettings.Key, new AuditChanges()
            .Change("maxSessionHours", previous.MaxSessionHours, settings.MaxSessionHours)
            .Change("pauseReasonRequired", previous.PauseReasonRequired, settings.PauseReasonRequired)
            .Change("allocationWarningPercent", previous.AllocationWarningPercent, settings.AllocationWarningPercent));
        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(WorkSettings.Key));
        return settings;
    }

    private async Task SaveAsync(string key, string json, CancellationToken cancellationToken)
    {
        var row = await db.GlobalSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.GlobalSettings.Add(new GlobalSetting(key, json, timeProvider.GetUtcNow(), currentUser.UserId));
        }
        else
        {
            row.Update(json, timeProvider.GetUtcNow(), currentUser.UserId);
        }
    }

    public async Task<Result<AuthSettings>> UpdateAuthAsync(AuthSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = settings.Validate();
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var previous = await GetAuthAsync(cancellationToken);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var row = await db.GlobalSettings.FirstOrDefaultAsync(s => s.Key == AuthSettings.Key, cancellationToken);
        if (row is null)
        {
            db.GlobalSettings.Add(new GlobalSetting(AuthSettings.Key, json, timeProvider.GetUtcNow(), currentUser.UserId));
        }
        else
        {
            row.Update(json, timeProvider.GetUtcNow(), currentUser.UserId);
        }

        auditLog.Record("settings.updated", "Setting", AuthSettings.Key, new AuditChanges()
            .Change("passwordMinLength", previous.PasswordMinLength, settings.PasswordMinLength)
            .Change("maxFailedSignInAttempts", previous.MaxFailedSignInAttempts, settings.MaxFailedSignInAttempts)
            .Change("lockoutMinutes", previous.LockoutMinutes, settings.LockoutMinutes)
            .Change("resetTokenMinutes", previous.ResetTokenMinutes, settings.ResetTokenMinutes)
            .Change("inviteTokenHours", previous.InviteTokenHours, settings.InviteTokenHours));

        await db.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey(AuthSettings.Key));
        return settings;
    }

    private static string CacheKey(string key) => $"settings:{key}";
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;

namespace TaskManagement.Application.Audit;

public sealed record AuditLogQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ActorId,
    string? Action,
    string? EntityType,
    string? EntityId,
    PageRequest Page);

public sealed record AuditEntryDto(
    long Id,
    DateTimeOffset OccurredAt,
    Guid? ActorId,
    string? ActorName,
    string? ActorIp,
    string Action,
    string EntityType,
    string EntityId,
    Guid? ProjectId,
    JsonElement? Changes,
    string? CorrelationId);

/// <summary>FR-AUD-05: search by date range, actor, entity and action; newest first.</summary>
public sealed class SearchAuditLogsHandler(IAppDbContext db)
{
    public async Task<PagedResult<AuditEntryDto>> HandleAsync(AuditLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entries = db.AuditEntries.AsNoTracking();
        if (query.From is { } from)
        {
            entries = entries.Where(e => e.OccurredAt >= from);
        }

        if (query.To is { } to)
        {
            entries = entries.Where(e => e.OccurredAt <= to);
        }

        if (query.ActorId is { } actorId)
        {
            entries = entries.Where(e => e.ActorId == actorId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(e => e.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(e => e.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            entries = entries.Where(e => e.EntityId == query.EntityId);
        }

        var total = await entries.CountAsync(cancellationToken);
        var rows = await entries
            .OrderByDescending(e => e.Id)
            .Skip(query.Page.Skip)
            .Take(query.Page.SafePageSize)
            .Select(e => new
            {
                Entry = e,
                ActorName = db.Users.Where(u => u.Id == e.ActorId).Select(u => u.FullName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new AuditEntryDto(
                r.Entry.Id,
                r.Entry.OccurredAt,
                r.Entry.ActorId,
                r.ActorName,
                r.Entry.ActorIp,
                r.Entry.Action,
                r.Entry.EntityType,
                r.Entry.EntityId,
                r.Entry.ProjectId,
                r.Entry.ChangesJson is null ? null : JsonSerializer.Deserialize<JsonElement>(r.Entry.ChangesJson),
                r.Entry.CorrelationId))
            .ToList();

        return new PagedResult<AuditEntryDto>(items, query.Page.SafePage, query.Page.SafePageSize, total);
    }
}

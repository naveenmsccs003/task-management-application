namespace TaskManagement.Domain.Audit;

/// <summary>
/// One append-only audit record (FR-AUD-01). The database rejects updates and deletes of these rows.
/// <see cref="ChangesJson"/> holds <c>{ "field": { "old": …, "new": … } }</c> and must never contain secrets.
/// </summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        Action = string.Empty;
        EntityType = string.Empty;
        EntityId = string.Empty;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>Null when a background job or an anonymous request (e.g. failed sign-in) caused the change.</summary>
    public Guid? ActorId { get; private set; }

    public string? ActorIp { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public string EntityId { get; private set; }

    public Guid? ProjectId { get; private set; }

    public string? ChangesJson { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditEntry Create(
        DateTimeOffset occurredAt,
        Guid? actorId,
        string? actorIp,
        string action,
        string entityType,
        string entityId,
        Guid? projectId,
        string? changesJson,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return new AuditEntry
        {
            OccurredAt = occurredAt,
            ActorId = actorId,
            ActorIp = actorIp,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ProjectId = projectId,
            ChangesJson = changesJson,
            CorrelationId = correlationId,
        };
    }
}

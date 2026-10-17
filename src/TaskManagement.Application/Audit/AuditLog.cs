using System.Text.Json;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Audit;

namespace TaskManagement.Application.Audit;

internal sealed class AuditLog(IAppDbContext db, ICurrentUser currentUser, TimeProvider timeProvider) : IAuditLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Record(string action, string entityType, string entityId, AuditChanges? changes = null, Guid? projectId = null)
    {
        var json = changes is null || changes.IsEmpty ? null : JsonSerializer.Serialize(changes.ToDictionary(), JsonOptions);
        db.AuditEntries.Add(AuditEntry.Create(
            timeProvider.GetUtcNow(),
            currentUser.UserId,
            currentUser.IpAddress,
            action,
            entityType,
            entityId,
            projectId,
            json,
            currentUser.CorrelationId));
    }
}

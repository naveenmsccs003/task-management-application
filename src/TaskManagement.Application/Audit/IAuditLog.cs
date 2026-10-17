namespace TaskManagement.Application.Audit;

/// <summary>
/// Adds audit entries to the current unit of work; they are saved together with the change they describe.
/// Never pass secrets (passwords, tokens) in <paramref name="changes"/>.
/// </summary>
public interface IAuditLog
{
    void Record(string action, string entityType, string entityId, AuditChanges? changes = null, Guid? projectId = null);
}

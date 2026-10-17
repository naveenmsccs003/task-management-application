namespace TaskManagement.Application.Audit;

/// <summary>Builds the <c>{ "field": { "old": …, "new": … } }</c> payload of an audit entry, skipping unchanged fields.</summary>
public sealed class AuditChanges
{
    private readonly Dictionary<string, object?> _changes = new(StringComparer.Ordinal);

    public bool IsEmpty => _changes.Count == 0;

    public AuditChanges Change<T>(string field, T oldValue, T newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            _changes[field] = new { old = oldValue, @new = newValue };
        }

        return this;
    }

    /// <summary>Records a value that has no "old" side (creation, or extra context such as a reason).</summary>
    public AuditChanges Set<T>(string field, T value)
    {
        _changes[field] = value;
        return this;
    }

    internal IReadOnlyDictionary<string, object?> ToDictionary() => _changes;
}

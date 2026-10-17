using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Common;

/// <summary>Collects input validation messages per field and turns them into one validation <see cref="Error"/>.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public FieldErrors Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
        return this;
    }

    public FieldErrors Required(string field, string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(field, $"{label} is required.");
        }

        return this;
    }

    public FieldErrors MaxLength(string field, string? value, int max, string label)
    {
        if (value is not null && value.Length > max)
        {
            Add(field, $"{label} must be at most {max} characters.");
        }

        return this;
    }

    public Error ToError(string message = "Some fields are invalid.") =>
        Error.Validation("validation_failed", message, _errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}

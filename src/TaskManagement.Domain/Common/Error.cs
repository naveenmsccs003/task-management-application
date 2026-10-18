namespace TaskManagement.Domain.Common;

/// <summary>
/// An expected failure with a stable, machine-readable <see cref="Code"/> (for example <c>task.invalid_transition</c>)
/// and a human-readable <see cref="Message"/>. Codes are part of the API contract — do not rename them.
/// </summary>
public sealed record Error
{
    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? fieldErrors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        Type = type;
        FieldErrors = fieldErrors;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    /// <summary>Field name → messages. Only set for <see cref="ErrorType.Validation"/>.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, message, ErrorType.Validation, fieldErrors);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized, null);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden, null);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound, null);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict, null);

    public static Error TooLarge(string code, string message) => new(code, message, ErrorType.TooLarge, null);

    public static Error Upstream(string code, string message) => new(code, message, ErrorType.Upstream, null);

    public static Error UnsupportedMediaType(string code, string message) => new(code, message, ErrorType.UnsupportedMediaType, null);
}

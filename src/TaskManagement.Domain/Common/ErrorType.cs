namespace TaskManagement.Domain.Common;

/// <summary>
/// The kind of an expected failure. The API maps each kind to one HTTP status code.
/// </summary>
public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
}

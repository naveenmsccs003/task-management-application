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

    /// <summary>An upload larger than the limit for its file type (HTTP 413).</summary>
    TooLarge,

    /// <summary>An upload whose file type is not allowed (HTTP 415).</summary>
    UnsupportedMediaType,

    /// <summary>An external system (GitLab) failed or refused the request (HTTP 502).</summary>
    Upstream,
}

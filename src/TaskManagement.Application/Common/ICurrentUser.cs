namespace TaskManagement.Application.Common;

/// <summary>Who is making the current request. Implemented by the API from the HTTP context.</summary>
public interface ICurrentUser
{
    /// <summary>Null for anonymous requests and background jobs.</summary>
    Guid? UserId { get; }

    string? IpAddress { get; }

    string? CorrelationId { get; }

    Guid RequiredUserId => UserId ?? throw new InvalidOperationException("This operation requires a signed-in user.");
}

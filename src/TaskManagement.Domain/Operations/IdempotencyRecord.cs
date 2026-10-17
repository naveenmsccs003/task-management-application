namespace TaskManagement.Domain.Operations;

/// <summary>
/// The first response to a request sent with an <c>Idempotency-Key</c> header (docs/api-design.md §1). A repeated key
/// from the same member on the same endpoint replays this response instead of acting twice (FR-WORK-07).
/// Rows are removed after 24 hours.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int EndpointMaxLength = 200;

    private IdempotencyRecord()
    {
        Endpoint = string.Empty;
        ResponseBody = string.Empty;
    }

    public Guid UserId { get; private set; }

    public Guid Key { get; private set; }

    public string Endpoint { get; private set; }

    public int ResponseStatus { get; private set; }

    /// <summary>JSON.</summary>
    public string ResponseBody { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(Guid userId, Guid key, string endpoint, int responseStatus, string responseBody, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        return new IdempotencyRecord
        {
            UserId = userId,
            Key = key,
            Endpoint = endpoint,
            ResponseStatus = responseStatus,
            ResponseBody = responseBody,
            CreatedAt = now,
        };
    }
}

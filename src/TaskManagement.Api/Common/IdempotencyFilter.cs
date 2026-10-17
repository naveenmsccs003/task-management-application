using System.Text.Json;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Common;

namespace TaskManagement.Api.Common;

/// <summary>
/// Honors an <c>Idempotency-Key</c> header (a UUID) on work-session actions (FR-WORK-07, docs/api-design.md §1): the first
/// successful response is stored and a repeated key from the same member replays it instead of acting twice. Without the
/// header the request runs normally. The database's one-active-session rule still protects parallel duplicates.
/// </summary>
internal sealed class IdempotencyFilter(IdempotencyStore store, ICurrentUser currentUser, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions)
    : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out var header) || currentUser.UserId is not { } userId)
        {
            return await next(context);
        }

        if (!Guid.TryParse(header, out var key))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"The {HeaderName} header must be a UUID.",
                extensions: new Dictionary<string, object?> { [ProblemDetailsSetup.CodeExtension] = "idempotency.invalid_key" });
        }

        // Method + path identifies the action, so a key cannot be replayed against a different task or action.
        var endpoint = $"{http.Request.Method} {http.Request.Path}";
        var existing = await store.FindAsync(userId, key, http.RequestAborted);
        if (existing is not null)
        {
            return existing.Endpoint == endpoint
                ? Results.Text(existing.ResponseBody, "application/json", statusCode: existing.ResponseStatus)
                : TypedResults.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: $"This {HeaderName} was already used for a different request.",
                    extensions: new Dictionary<string, object?> { [ProblemDetailsSetup.CodeExtension] = "idempotency.key_reused" });
        }

        var result = await next(context);
        if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } success && result is IValueHttpResult valued)
        {
            var body = JsonSerializer.Serialize(valued.Value, jsonOptions.Value.SerializerOptions);
            await store.SaveAsync(userId, key, endpoint, success.StatusCode ?? StatusCodes.Status200OK, body, http.RequestAborted);
        }
        else if (await store.FindAsync(userId, key, http.RequestAborted) is { } raced && raced.Endpoint == endpoint)
        {
            // A parallel request with the same key succeeded first: answer with its result, not this one's conflict.
            return Results.Text(raced.ResponseBody, "application/json", statusCode: raced.ResponseStatus);
        }

        return result;
    }
}

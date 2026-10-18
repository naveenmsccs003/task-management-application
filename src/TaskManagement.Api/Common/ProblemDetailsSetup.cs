using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using TaskManagement.Domain.Common;

namespace TaskManagement.Api.Common;

/// <summary>Turns expected failures (<see cref="Error"/>) into RFC 9457 Problem Details responses.</summary>
internal static class ProblemDetailsSetup
{
    public const string CodeExtension = "code";
    public const string TraceIdExtension = "traceId";

    /// <summary>Adds the trace ID to every Problem Details response, including framework-generated ones.</summary>
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
        {
            context.ProblemDetails.Extensions[TraceIdExtension] =
                Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        });

    public static int ToStatusCode(this ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.TooLarge => StatusCodes.Status413PayloadTooLarge,
        ErrorType.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
        ErrorType.Upstream => StatusCodes.Status502BadGateway,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type."),
    };

    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = new Dictionary<string, object?> { [CodeExtension] = error.Code };
        if (error.FieldErrors is not null)
        {
            extensions["errors"] = error.FieldErrors;
        }

        return TypedResults.Problem(
            statusCode: error.Type.ToStatusCode(),
            title: error.Message,
            extensions: extensions);
    }
}

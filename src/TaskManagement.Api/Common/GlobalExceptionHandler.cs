using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TaskManagement.Api.Common;

/// <summary>
/// Last line of defence for unexpected exceptions: logs the details and returns a Problem Details body
/// without stack traces or internal messages (NFR-SEC-07).
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        int status;
        string title;
        string code;

        switch (exception)
        {
            case DbUpdateConcurrencyException:
                status = StatusCodes.Status409Conflict;
                title = "This record was changed by someone else. Reload it and try again.";
                code = "concurrency_conflict";
                LogConcurrencyConflict(logger, httpContext.Request.Method, httpContext.Request.Path);
                break;
            case BadHttpRequestException badRequest:
                status = badRequest.StatusCode;
                title = "The request could not be read.";
                code = "bad_request";
                break;
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // The client went away; nothing useful to send.
                return true;
            default:
                status = StatusCodes.Status500InternalServerError;
                title = "An unexpected error occurred.";
                code = "internal_error";
                LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);
                break;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Extensions = { [ProblemDetailsSetup.CodeExtension] = code },
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Concurrency conflict for {Method} {Path}")]
    private static partial void LogConcurrencyConflict(ILogger logger, string method, string path);
}

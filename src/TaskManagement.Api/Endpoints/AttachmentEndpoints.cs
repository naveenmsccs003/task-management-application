using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TaskManagement.Api.Common;
using TaskManagement.Application.Attachments;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Files;
using TaskManagement.Infrastructure.Files;

namespace TaskManagement.Api.Endpoints;

/// <summary>
/// Attachments (docs/api-design.md §4.9). Uploads are read straight from the request body as a stream (never buffered
/// as a form), after the caller's permission has been checked. Downloads always go through the API with the parent
/// record's access check; previews are inline only for scanned images and videos.
/// </summary>
internal static class AttachmentEndpoints
{
    public const string UploadRateLimit = "attachments-upload";

    /// <summary>Request body limit for uploads: the hard cap plus room for the multipart framing.</summary>
    private const long MaxRequestBytes = (AttachmentSettings.HardMaxMb + 1L) * 1024 * 1024;

    public static IServiceCollection AddAttachmentUploads(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = configuration.GetValue($"{AttachmentOptions.Section}:{nameof(AttachmentOptions.UploadsPerMinute)}", 30);
        return services.Configure<RateLimiterOptions>(limiter => limiter.AddPolicy(UploadRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1) })));
    }

    public static IEndpointRouteBuilder MapAttachmentEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/attachment-rules", async (AttachmentService attachments, CancellationToken ct) => TypedResults.Ok(await attachments.RulesAsync(ct)))
            .WithTags("Attachments")
            .RequireAuthorization()
            .WithSummary("Signed in — allowed file types and their size limits, for the upload form.");

        var tasks = api.MapGroup("/tasks/{id:guid}/attachments").WithTags("Attachments").RequireAuthorization();
        tasks.MapGet("/", async (Guid id, AttachmentService attachments, CancellationToken ct) => (await attachments.ListAsync(id, ct)).ToHttpResult())
            .WithSummary("tasks.view — the task's files (algorithm files need algorithms.view); drafts are not listed.");
        tasks.MapPost("/", UploadAsync)
            .RequireRateLimiting(UploadRateLimit)
            .WithSummary("attachments.upload — multipart/form-data with one `file`; `ownerType` (Task, Comment, Algorithm, Review) in the query.");

        var files = api.MapGroup("/attachments/{attachmentId:guid}").WithTags("Attachments").RequireAuthorization();
        files.MapGet("/download", (Guid attachmentId, AttachmentService attachments, CancellationToken ct) => SendAsync(attachmentId, false, attachments, ct))
            .WithSummary("The parent record's view permission; 409 until the malware scan has passed.");
        files.MapGet("/preview", (Guid attachmentId, AttachmentService attachments, CancellationToken ct) => SendAsync(attachmentId, true, attachments, ct))
            .WithSummary("Same, inline — scanned images and videos only.");
        files.MapDelete("/", async (Guid attachmentId, AttachmentService attachments, CancellationToken ct) =>
                (await attachments.DeleteAsync(attachmentId, ct)).ToNoContentResult())
            .WithSummary("attachments.delete — the uploader while the task is not Closed, or a TL/PM.");
        return api;
    }

    private static async Task<IResult> UploadAsync(
        Guid id, AttachmentOwnerType? ownerType, HttpRequest request, AttachmentService attachments, CancellationToken ct)
    {
        var target = await attachments.PrepareUploadAsync(id, ownerType, ct);
        if (target.IsFailure)
        {
            return target.Error!.ToProblem();
        }

        var boundary = request.ContentType is { } contentType && MediaTypeHeaderValue.TryParse(contentType, out var media)
            && media.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            ? HeaderUtilities.RemoveQuotes(media.Boundary).Value
            : null;
        if (string.IsNullOrEmpty(boundary))
        {
            return FileRequired();
        }

        if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = MaxRequestBytes;
        }

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                && disposition.DispositionType.Equals("form-data") && disposition.Name.Value == "file"
                && (disposition.FileName.HasValue || disposition.FileNameStar.HasValue))
            {
                var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value;
                var result = await attachments.UploadAsync(target.Value!, fileName, section.Body, ct);
                return result.IsSuccess
                    ? TypedResults.Created($"/api/v1/attachments/{result.Value!.Id}/download", result.Value)
                    : result.Error!.ToProblem();
            }
        }

        return FileRequired();
    }

    private static async Task<IResult> SendAsync(Guid attachmentId, bool preview, AttachmentService attachments, CancellationToken ct)
    {
        var result = await attachments.OpenAsync(attachmentId, preview, ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var file = result.Value!;
        return new AttachmentFileResult(file);
    }

    private static IResult FileRequired() =>
        Error.Validation("attachment.file_required", "Send one file as multipart/form-data in a field named “file”.").ToProblem();

    /// <summary>
    /// Sends the stored file with its detected type (FR-ATT-06): <c>Content-Disposition: attachment</c> unless it is a
    /// preview, a UTF-8 file name, and range support so videos can seek. <c>nosniff</c> and a CSP that allows nothing come
    /// from the security headers middleware.
    /// </summary>
    private sealed class AttachmentFileResult(AttachmentContent file) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var disposition = new ContentDispositionHeaderValue(file.Inline ? "inline" : "attachment");
            disposition.SetHttpFileName(file.FileName);
            httpContext.Response.Headers.ContentDisposition = disposition.ToString();
            httpContext.Response.Headers.CacheControl = "private, no-store";
            return Results.Stream(file.Content, file.ContentType, enableRangeProcessing: true).ExecuteAsync(httpContext);
        }
    }
}

using Microsoft.AspNetCore.Antiforgery;
using TaskManagement.Api.Common;

namespace TaskManagement.Api.Auth;

/// <summary>
/// Requires a valid antiforgery token (header <c>X-XSRF-TOKEN</c>) on every state-changing API request (NFR-SEC-03).
/// ASP.NET Core only validates form posts automatically; JSON APIs need this filter.
/// </summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The request could not be verified. Reload the page and try again.",
                extensions: new Dictionary<string, object?> { [ProblemDetailsSetup.CodeExtension] = "antiforgery_invalid" });
        }

        return await next(context);
    }
}

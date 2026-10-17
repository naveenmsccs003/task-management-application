using System.Diagnostics;
using System.Security.Claims;
using TaskManagement.Application.Common;

namespace TaskManagement.Api.Common;

/// <summary>Reads the signed-in member and request details from the HTTP context. Null outside requests (jobs, CLI).</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => Activity.Current?.TraceId.ToString() ?? accessor.HttpContext?.TraceIdentifier;
}

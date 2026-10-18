using System.Net;
using System.Net.Sockets;

namespace TaskManagement.Application.GitLab;

/// <summary>Where to call and with which token. The token is decrypted only for the call and never logged.</summary>
public sealed record GitLabEndpoint(string BaseUrl, string Token);

public sealed record GitLabUser(long Id, string Username);

public sealed record GitLabProjectInfo(long Id, string Name, string PathWithNamespace, string WebUrl);

public sealed record GitLabIssue(
    long Id,
    long Iid,
    string Title,
    string? Description,
    bool IsClosed,
    IReadOnlyList<string> Labels,
    DateOnly? MilestoneDueDate,
    string WebUrl,
    DateTimeOffset UpdatedAt);

public sealed record GitLabPage<T>(IReadOnlyList<T> Items, int Page, bool HasMore);

public enum GitLabFailure
{
    /// <summary>The token is wrong, expired or lacks the read_api scope.</summary>
    Unauthorized,

    NotFound,

    /// <summary>Still rate limited after retries.</summary>
    RateLimited,

    /// <summary>Unreachable, timed out, a server error, or an address that is not allowed (SSRF guard).</summary>
    Unavailable,
}

/// <summary>A GitLab call that failed. The message is safe to show and store: it never contains the token.</summary>
public sealed class GitLabException(GitLabFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public GitLabFailure Failure { get; } = failure;
}

/// <summary>
/// Read-only access to the GitLab REST API v4 (A14: we never write to GitLab). Implementations retry rate limits and
/// transient failures (FR-IMP-09) and throw <see cref="GitLabException"/> for anything else.
/// </summary>
public interface IGitLabClient
{
    Task<GitLabUser> GetCurrentUserAsync(GitLabEndpoint endpoint, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitLabProjectInfo>> SearchProjectsAsync(GitLabEndpoint endpoint, string? search, CancellationToken cancellationToken);

    Task<GitLabProjectInfo> GetProjectAsync(GitLabEndpoint endpoint, long projectId, CancellationToken cancellationToken);

    /// <summary>Issues of a project, newest first; <paramref name="state"/> is <c>opened</c>, <c>closed</c> or <c>all</c>.</summary>
    Task<GitLabPage<GitLabIssue>> ListIssuesAsync(
        GitLabEndpoint endpoint, long projectId, string state, string? search, int page, CancellationToken cancellationToken);

    /// <summary>The issues with these IIDs (open or closed); missing ones are simply absent. At most 100 per call.</summary>
    Task<IReadOnlyList<GitLabIssue>> GetIssuesAsync(GitLabEndpoint endpoint, long projectId, IReadOnlyCollection<long> iids, CancellationToken cancellationToken);
}

/// <summary>Encrypts GitLab tokens at rest (FR-IMP-02) with keys kept outside the database.</summary>
public interface ITokenProtector
{
    byte[] Protect(string token);

    string Unprotect(byte[] ciphertext);
}

/// <summary>Configuration section <c>GitLab</c>.</summary>
public sealed class GitLabOptions
{
    public const string Section = "GitLab";

    /// <summary>Hosts that may be reached over plain http or on a loopback address (development and tests only).</summary>
    public IList<string> AllowedInsecureHosts { get; } = [];

    public TimeSpan SyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    public bool SyncWorkerEnabled { get; set; } = true;
}

/// <summary>
/// Rules for GitLab base URLs (security.md S19): absolute https URLs only (http only for allow-listed hosts), no user
/// info, query or fragment. Addresses are checked again when connecting, because DNS can change.
/// </summary>
public static class GitLabUrls
{
    /// <summary>Returns the normalized base URL, or null with <paramref name="error"/> set.</summary>
    public static string? Normalize(string? input, GitLabOptions options, out string? error)
    {
        ArgumentNullException.ThrowIfNull(options);
        error = null;
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            error = "Enter the full address, e.g. https://gitlab.example.com.";
            return null;
        }

        var allowed = IsAllowListed(uri.Host, options);
        if (uri.Scheme == Uri.UriSchemeHttp && !allowed)
        {
            error = "The address must start with https://.";
            return null;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The address must not contain a user name, a query or a fragment.";
            return null;
        }

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IsBlocked(address, allowed))
        {
            error = "This address is not allowed.";
            return null;
        }

        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    public static bool IsAllowListed(string host, GitLabOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AllowedInsecureHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Link-local (cloud metadata services live at 169.254.169.254), unspecified and multicast addresses are never
    /// reached; loopback only for allow-listed hosts. Private networks are allowed: self-managed GitLab often lives there.
    /// </summary>
    public static bool IsBlocked(IPAddress address, bool allowListed)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return !allowListed;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast || address.IsIPv6LinkLocal)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 169 && bytes[1] == 254 // link-local / metadata
                || bytes[0] >= 224 // multicast and reserved
                || bytes[0] == 0;
        }

        return false;
    }
}

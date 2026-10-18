using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TaskManagement.Application.GitLab;

namespace TaskManagement.Infrastructure.GitLab;

/// <summary>
/// GitLab REST API v4 over <see cref="HttpClient"/> (named client <see cref="ClientName"/>). The token goes in the
/// <c>PRIVATE-TOKEN</c> header, never in a URL, so it cannot end up in logs. Rate limits (429) and server errors are
/// retried with back-off, honouring <c>Retry-After</c> (FR-IMP-09).
/// </summary>
public sealed class GitLabHttpClient(IHttpClientFactory httpClientFactory, TimeProvider timeProvider) : IGitLabClient
{
    public const string ClientName = "gitlab";
    public const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Retry delays are scaled by this in tests.</summary>
    public double RetryDelayScale { get; init; } = 1;

    public async Task<GitLabUser> GetCurrentUserAsync(GitLabEndpoint endpoint, CancellationToken cancellationToken)
    {
        var (user, _) = await GetAsync<UserJson>(endpoint, "user", cancellationToken);
        return new GitLabUser(user.Id, user.Username ?? string.Empty);
    }

    public async Task<IReadOnlyList<GitLabProjectInfo>> SearchProjectsAsync(GitLabEndpoint endpoint, string? search, CancellationToken cancellationToken)
    {
        var query = "projects?membership=true&simple=true&order_by=last_activity_at&per_page=20";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += "&search=" + Uri.EscapeDataString(search);
        }

        var (projects, _) = await GetAsync<List<ProjectJson>>(endpoint, query, cancellationToken);
        return projects.Select(ToProject).ToList();
    }

    public async Task<GitLabProjectInfo> GetProjectAsync(GitLabEndpoint endpoint, long projectId, CancellationToken cancellationToken)
    {
        var (project, _) = await GetAsync<ProjectJson>(endpoint, $"projects/{projectId}", cancellationToken);
        return ToProject(project);
    }

    public async Task<GitLabPage<GitLabIssue>> ListIssuesAsync(
        GitLabEndpoint endpoint, long projectId, string state, string? search, int page, CancellationToken cancellationToken)
    {
        var query = $"projects/{projectId}/issues?state={Uri.EscapeDataString(state)}&order_by=created_at&sort=desc&per_page=50&page={page}";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += "&search=" + Uri.EscapeDataString(search);
        }

        var (issues, nextPage) = await GetAsync<List<IssueJson>>(endpoint, query, cancellationToken);
        return new GitLabPage<GitLabIssue>(issues.Select(ToIssue).ToList(), page, !string.IsNullOrEmpty(nextPage));
    }

    public async Task<IReadOnlyList<GitLabIssue>> GetIssuesAsync(
        GitLabEndpoint endpoint, long projectId, IReadOnlyCollection<long> iids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(iids);
        if (iids.Count == 0)
        {
            return [];
        }

        var filter = string.Join('&', iids.Select(i => "iids[]=" + i.ToString(CultureInfo.InvariantCulture)));
        var (issues, _) = await GetAsync<List<IssueJson>>(endpoint, $"projects/{projectId}/issues?state=all&per_page=100&{filter}", cancellationToken);
        return issues.Select(ToIssue).ToList();
    }

    /// <summary>The parsed body and GitLab's <c>X-Next-Page</c> header (empty on the last page).</summary>
    private async Task<(T Value, string? NextPage)> GetAsync<T>(GitLabEndpoint endpoint, string relative, CancellationToken cancellationToken)
    {
        var uri = new Uri($"{endpoint.BaseUrl}/api/v4/{relative}");
        var client = httpClientFactory.CreateClient(ClientName);
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("PRIVATE-TOKEN", endpoint.Token);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt < MaxAttempts)
                {
                    await DelayAsync(attempt, null, cancellationToken);
                    continue;
                }

                throw new GitLabException(GitLabFailure.Unavailable, $"GitLab at {endpoint.BaseUrl} could not be reached.", exception);
            }

            if (response.IsSuccessStatusCode)
            {
                using (response)
                {
                    try
                    {
                        var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                        var nextPage = response.Headers.TryGetValues("X-Next-Page", out var next) ? next.FirstOrDefault() : null;
                        return (value ?? throw new JsonException("Empty response."), nextPage);
                    }
                    catch (JsonException exception)
                    {
                        throw new GitLabException(GitLabFailure.Unavailable, "GitLab sent a response that could not be read.", exception);
                    }
                }
            }

            var status = response.StatusCode;
            var retryAfter = response.Headers.RetryAfter;
            response.Dispose();
            var transient = status is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout or HttpStatusCode.InternalServerError;
            if (transient && attempt < MaxAttempts)
            {
                await DelayAsync(attempt, retryAfter, cancellationToken);
                continue;
            }

            throw status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new GitLabException(GitLabFailure.Unauthorized, "GitLab refused the access token. Check that it is valid and has the read_api scope."),
                HttpStatusCode.NotFound => new GitLabException(GitLabFailure.NotFound, "GitLab could not find this project or issue, or the token cannot see it."),
                HttpStatusCode.TooManyRequests => new GitLabException(GitLabFailure.RateLimited, "GitLab is rate limiting requests. Try again later."),
                _ when (int)status is >= 300 and < 400 => new GitLabException(GitLabFailure.Unavailable, "GitLab answered with a redirect, which is not followed. Check the address."),
                _ => new GitLabException(GitLabFailure.Unavailable, $"GitLab answered {(int)status}."),
            };
        }
    }

    private Task DelayAsync(int attempt, System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - timeProvider.GetUtcNow() : TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        delay = delay > MaxRetryDelay ? MaxRetryDelay : delay;
        return Task.Delay(delay * RetryDelayScale, timeProvider, cancellationToken);
    }

    /// <summary>
    /// The connection step of the named client (security.md S19): every address a host name resolves to is checked, so a
    /// DNS answer pointing at a metadata service or loopback is refused even though the URL looked fine when saved.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, GitLabOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        var host = context.DnsEndPoint.Host;
        var allowListed = GitLabUrls.IsAllowListed(host, options);
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, cancellationToken);
        var allowed = addresses.Where(a => !GitLabUrls.IsBlocked(a, allowListed)).ToList();
        if (allowed.Count == 0 || allowed.Count != addresses.Length)
        {
            throw new HttpRequestException($"Connections to {host} are not allowed.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed.ToArray(), context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static GitLabProjectInfo ToProject(ProjectJson p) =>
        new(p.Id, p.Name ?? string.Empty, p.PathWithNamespace ?? string.Empty, p.WebUrl ?? string.Empty);

    private static GitLabIssue ToIssue(IssueJson i) =>
        new(
            i.Id, i.Iid, i.Title ?? string.Empty, i.Description, string.Equals(i.State, "closed", StringComparison.OrdinalIgnoreCase),
            i.Labels ?? [], i.Milestone?.DueDate is { } due && DateOnly.TryParse(due, CultureInfo.InvariantCulture, out var date) ? date : null,
            i.WebUrl ?? string.Empty, i.UpdatedAt);

    private sealed record UserJson(long Id, string? Username);

    private sealed record ProjectJson(long Id, string? Name, string? PathWithNamespace, string? WebUrl);

    private sealed record MilestoneJson(string? DueDate);

    private sealed record IssueJson(
        long Id, long Iid, string? Title, string? Description, string? State, List<string>? Labels, MilestoneJson? Milestone, string? WebUrl,
        DateTimeOffset UpdatedAt);
}

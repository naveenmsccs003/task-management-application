using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskManagement.IntegrationTests.Infrastructure;

/// <summary>
/// An in-memory GitLab REST API v4 (docs/testing.md: "fake GitLab HTTP server"), used as the primary handler of the
/// app's GitLab HTTP client. It checks the PRIVATE-TOKEN header, serves projects and issues, and can be told to fail.
/// </summary>
public sealed partial class FakeGitLab : HttpMessageHandler
{
    public const string BaseUrl = "https://gitlab.test";
    public const string Token = "glpat-test-token-1234abcd";

    private readonly ConcurrentDictionary<long, FakeProject> _projects = new();
    private readonly ConcurrentQueue<HttpStatusCode> _failures = new();
    private long _nextIssueId = 1000;

    public ConcurrentBag<string> Requests { get; } = [];

    public FakeProject AddProject(string path)
    {
        var project = new FakeProject(Random.Shared.NextInt64(10_000, 99_999_999), path);
        _projects[project.Id] = project;
        return project;
    }

    public FakeIssue AddIssue(FakeProject project, string title, string? description = null, string[]? labels = null, DateOnly? milestoneDue = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issue = new FakeIssue
        {
            Id = Interlocked.Increment(ref _nextIssueId),
            Iid = project.Issues.Count + 1,
            Title = title,
            Description = description,
            Labels = labels ?? [],
            MilestoneDue = milestoneDue,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        project.Issues[issue.Iid] = issue;
        issue.WebUrl = $"{BaseUrl}/{project.Path}/-/issues/{issue.Iid}";
        return issue;
    }

    /// <summary>The next requests answer these status codes, one each, before normal service resumes.</summary>
    public void FailNext(params HttpStatusCode[] statuses)
    {
        foreach (var status in statuses)
        {
            _failures.Enqueue(status);
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var uri = request.RequestUri!;
        Requests.Add(uri.PathAndQuery);
        if (_failures.TryDequeue(out var failure))
        {
            var failed = new HttpResponseMessage(failure);
            if (failure == HttpStatusCode.TooManyRequests)
            {
                failed.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            }

            return Task.FromResult(failed);
        }

        if (uri.GetLeftPart(UriPartial.Authority) != BaseUrl || !request.Headers.TryGetValues("PRIVATE-TOKEN", out var tokens) || tokens.Single() != Token)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }

        var path = uri.AbsolutePath;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (path == "/api/v4/user")
        {
            return Json(new { id = 7, username = "importer" });
        }

        if (path == "/api/v4/projects")
        {
            var search = query["search"];
            return Json(_projects.Values.Where(p => search is null || p.Path.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(ProjectJson));
        }

        var match = ProjectPath().Match(path);
        if (!match.Success || !_projects.TryGetValue(long.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), out var project))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (!match.Groups[2].Success)
        {
            return Json(ProjectJson(project));
        }

        var state = query["state"] ?? "opened";
        IEnumerable<FakeIssue> issues = project.Issues.Values.OrderByDescending(i => i.Iid);
        var iids = query.GetValues("iids[]");
        if (iids is not null)
        {
            var wanted = iids.Select(i => long.Parse(i, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            issues = issues.Where(i => wanted.Contains(i.Iid));
        }

        if (state != "all")
        {
            issues = issues.Where(i => i.IsClosed == (state == "closed"));
        }

        return Json(issues.Where(i => !i.Deleted).Select(i => new
        {
            id = i.Id,
            iid = i.Iid,
            title = i.Title,
            description = i.Description,
            state = i.IsClosed ? "closed" : "opened",
            labels = i.Labels,
            milestone = i.MilestoneDue is { } due ? new { due_date = due.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) } : null,
            web_url = i.WebUrl,
            updated_at = i.UpdatedAt,
        }));
    }

    private static object ProjectJson(FakeProject p) =>
        new { id = p.Id, name = p.Path.Split('/')[^1], path_with_namespace = p.Path, web_url = $"{BaseUrl}/{p.Path}" };

    private static Task<HttpResponseMessage> Json(object body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") });

    [GeneratedRegex("^/api/v4/projects/([0-9]+)(/issues)?$")]
    private static partial Regex ProjectPath();
}

public sealed class FakeProject(long id, string path)
{
    public long Id { get; } = id;

    public string Path { get; } = path;

    public ConcurrentDictionary<long, FakeIssue> Issues { get; } = new();
}

public sealed class FakeIssue
{
    public long Id { get; init; }

    public long Iid { get; init; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public required string[] Labels { get; set; }

    public DateOnly? MilestoneDue { get; set; }

    public bool IsClosed { get; set; }

    public bool Deleted { get; set; }

    public string WebUrl { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Simulates an edit in GitLab.</summary>
    public void Change(Action<FakeIssue> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        edit(this);
        UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(1);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.GitLab;
using TaskManagement.Domain.Notifications;
using TaskManagement.Infrastructure.GitLab;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.GitLab;

[Collection(ApiCollection.Name)]
public sealed class GitLabTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private FakeGitLab GitLab => factory.GitLab;

    private async Task<Guid> CreateConnectionAsync(string? token = null)
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var response = await admin.PostAsync("/api/v1/gitlab/connections", new { name = $"GitLab {Guid.NewGuid():N}", baseUrl = FakeGitLab.BaseUrl + "/", token = token ?? FakeGitLab.Token });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return Id(await response.JsonAsync());
    }

    /// <summary>A project linked to a fresh fake GitLab project with three open issues.</summary>
    private async Task<(ProjectScenario Scenario, FakeProject Remote, FakeIssue[] Issues)> LinkedProjectAsync()
    {
        var scenario = await ProjectScenario.CreateAsync(factory);
        var remote = GitLab.AddProject($"acme/app-{Guid.NewGuid():N}");
        var due = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));
        FakeIssue[] issues =
        [
            GitLab.AddIssue(remote, "Sign-up form", "Build the **form**.\u0007", ["frontend", "bug"], due),
            GitLab.AddIssue(remote, "Password reset", null, [new string('x', 40)]),
            GitLab.AddIssue(remote, "Old idea"),
        ];
        issues[2].IsClosed = true;
        var connectionId = await CreateConnectionAsync();
        var link = await scenario.Pm.PutAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link", new { connectionId, gitLabProjectId = remote.Id });
        Assert.True(link.StatusCode == HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        return (scenario, remote, issues);
    }

    private async Task<JsonElement> ImportAsync(ProjectScenario scenario, params long[] iids)
    {
        var response = await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab/import", new { issueIids = iids });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.JsonAsync();
    }

    private async Task<JsonElement> SyncAsync(ProjectScenario scenario) =>
        await (await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab/sync")).JsonAsync();

    private Task<Guid> TaskIdForAsync(FakeIssue issue) =>
        factory.WithDbAsync(db => db.GitLabIssueLinks.Where(l => l.IssueId == issue.Id).Select(l => l.TaskId).SingleAsync());

    [Fact]
    public async Task AC_IMP_3_tokens_are_write_only_and_encrypted_at_rest()
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var id = await CreateConnectionAsync();

        var raw = await (await admin.GetAsync("/api/v1/gitlab/connections")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain(FakeGitLab.Token, raw, StringComparison.Ordinal);
        var listed = JsonDocument.Parse(raw).RootElement.EnumerateArray().Single(c => Id(c) == id);
        Assert.Equal("…abcd", listed.GetProperty("tokenHint").GetString());
        Assert.Equal("https://gitlab.test", listed.GetProperty("baseUrl").GetString());
        Assert.False(listed.TryGetProperty("token", out _));

        // AC-AUTH-6: the database holds only ciphertext.
        var stored = await factory.WithDbAsync(db => db.GitLabConnections.AsNoTracking().SingleAsync(c => c.Id == id));
        Assert.DoesNotContain(FakeGitLab.Token, Encoding.UTF8.GetString(stored.TokenCiphertext), StringComparison.Ordinal);
        var audit = await factory.WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id.ToString()).ToListAsync());
        Assert.DoesNotContain(audit, a => JsonSerializer.Serialize(a).Contains(FakeGitLab.Token, StringComparison.Ordinal));

        // The stored token works; a wrong one is reported without being echoed.
        var tested = await (await admin.PostAsync($"/api/v1/gitlab/connections/{id}/test")).JsonAsync();
        Assert.Equal("Healthy", tested.GetProperty("status").GetString());
        var renamed = await admin.PatchAsync($"/api/v1/gitlab/connections/{id}", new { name = "Renamed " + id, baseUrl = FakeGitLab.BaseUrl, token = "glpat-wrong-token-9999" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var failed = await (await admin.PostAsync($"/api/v1/gitlab/connections/{id}/test")).JsonAsync();
        Assert.Equal("Unhealthy", failed.GetProperty("status").GetString());
        Assert.Contains("read_api", failed.GetProperty("lastError").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("glpat-wrong", failed.ToString(), StringComparison.Ordinal);

        // Only connection managers see connections.
        using var tl = await factory.SignedInClientAsync(SystemRoles.TeamLeaderId);
        Assert.Equal(HttpStatusCode.Forbidden, (await tl.GetAsync("/api/v1/gitlab/connections")).StatusCode);
    }

    [Theory]
    [InlineData("http://gitlab.example.com")]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://user:pass@gitlab.example.com")]
    [InlineData("gitlab.example.com")]
    [InlineData("ftp://gitlab.example.com")]
    public async Task S19_unsafe_base_urls_are_refused(string baseUrl)
    {
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var response = await admin.PostAsync("/api/v1/gitlab/connections", new { name = "Bad " + Guid.NewGuid(), baseUrl, token = FakeGitLab.Token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("baseUrl", out _));
    }

    [Fact]
    public async Task AC_IMP_1_issues_import_as_draft_tasks_once()
    {
        var (scenario, remote, issues) = await LinkedProjectAsync();
        using var _ = scenario;

        var preview = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/gitlab/issues");
        Assert.Equal([2L, 1L], preview.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("iid").GetInt64()));

        var run = await ImportAsync(scenario, 1, 2, 3, 99);
        Assert.Equal(2, run.GetProperty("created").GetInt32());
        Assert.Equal(1, run.GetProperty("skipped").GetInt32()); // the closed issue
        Assert.Equal(1, run.GetProperty("failed").GetInt32()); // #99 does not exist
        Assert.Contains("#99", run.GetProperty("errors")[0].GetString(), StringComparison.Ordinal);

        var task = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{await TaskIdForAsync(issues[0])}");
        Assert.Equal("Draft", task.GetProperty("status").GetString());
        Assert.Equal("Sign-up form", task.GetProperty("title").GetString());
        Assert.Equal("Build the **form**.", task.GetProperty("description").GetString()); // control character dropped
        Assert.Equal("Bug", task.GetProperty("type").GetString());
        Assert.Equal(["frontend", "bug"], task.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal(issues[0].MilestoneDue!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), task.GetProperty("dueDate").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("assignee").ValueKind); // Q10: assignees are not mapped
        Assert.Equal(issues[0].WebUrl, task.GetProperty("gitLab").GetProperty("webUrl").GetString());
        var second = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{await TaskIdForAsync(issues[1])}");
        Assert.Equal(30, second.GetProperty("labels")[0].GetString()!.Length);

        // Importing again creates nothing new (FR-IMP-04), and the preview shows what is already imported.
        var again = await ImportAsync(scenario, 1, 2);
        Assert.Equal(0, again.GetProperty("created").GetInt32());
        Assert.Equal(2, again.GetProperty("updated").GetInt32());
        Assert.Equal(2, await factory.WithDbAsync(db => db.Tasks.CountAsync(t => t.ProjectId == scenario.ProjectId)));
        preview = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/gitlab/issues");
        Assert.All(preview.GetProperty("items").EnumerateArray(), i => Assert.StartsWith(scenario.Key + "-", i.GetProperty("taskKey").GetString(), StringComparison.Ordinal));

        var runs = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/gitlab/import-runs");
        Assert.Equal(2, runs.GetArrayLength());
        Assert.Equal(scenario.PmMember.Id, runs[0].GetProperty("startedBy").GetProperty("id").GetGuid());
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "task.imported" && a.ProjectId == scenario.ProjectId)));
    }

    [Fact]
    public async Task AC_IMP_2_a_closed_issue_flags_the_task_and_tells_the_team_leader_but_never_changes_its_state()
    {
        var (scenario, _, issues) = await LinkedProjectAsync();
        using var _ = scenario;
        await ImportAsync(scenario, 1);
        var taskId = await TaskIdForAsync(issues[0]);
        Assert.Equal(HttpStatusCode.OK, (await scenario.Pm.PostAsync($"/api/v1/tasks/{taskId}/assign", new { assigneeId = scenario.DevMember.Id })).StatusCode);

        issues[0].Change(i => i.IsClosed = true);
        await SyncAsync(scenario);
        await SyncAsync(scenario);

        var task = await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}");
        Assert.Equal("ReadyToStart", task.GetProperty("status").GetString());
        Assert.True(task.GetProperty("flags").GetProperty("gitLabIssueClosed").GetBoolean());
        Assert.Equal("Closed", task.GetProperty("gitLab").GetProperty("state").GetString());
        var notices = await factory.WithDbAsync(db => db.Notifications.AsNoTracking()
            .Where(n => n.TaskId == taskId && n.Type == NotificationType.GitLabIssueClosed).Select(n => n.RecipientId).ToListAsync());
        Assert.Equal([scenario.TlMember.Id], notices); // once, to the TL

        // Reopened in GitLab: the flag clears (task-workflow.md §8).
        issues[0].Change(i => i.IsClosed = false);
        await SyncAsync(scenario);
        Assert.False((await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}")).GetProperty("flags").GetProperty("gitLabIssueClosed").GetBoolean());
    }

    [Fact]
    public async Task Drafts_follow_GitLab_and_later_changes_wait_for_acceptance()
    {
        var (scenario, _, issues) = await LinkedProjectAsync();
        using var _ = scenario;
        await ImportAsync(scenario, 1, 2);
        var draftId = await TaskIdForAsync(issues[1]);
        var assignedId = await TaskIdForAsync(issues[0]);
        await scenario.Pm.PostAsync($"/api/v1/tasks/{assignedId}/assign", new { assigneeId = scenario.DevMember.Id });

        issues[1].Change(i => i.Title = "Password reset by email");
        issues[0].Change(i => i.Description = "New requirements.");
        var run = await SyncAsync(scenario);
        Assert.Equal(2, run.GetProperty("updated").GetInt32());

        Assert.Equal("Password reset by email", (await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{draftId}")).GetProperty("title").GetString());
        var assigned = await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{assignedId}");
        Assert.Equal("Build the **form**.", assigned.GetProperty("description").GetString());
        var pending = assigned.GetProperty("gitLab").GetProperty("pendingChanges");
        Assert.Equal("New requirements.", pending.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("title").ValueKind);
        Assert.Contains("acceptGitLabChanges", assigned.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        // Nothing changed since: the next sync leaves everything alone.
        Assert.Equal(0, (await SyncAsync(scenario)).GetProperty("updated").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.PostAsync($"/api/v1/tasks/{assignedId}/gitlab/accept-changes")).StatusCode);
        var accepted = await (await scenario.Tl.PostAsync($"/api/v1/tasks/{assignedId}/gitlab/accept-changes")).JsonAsync();
        Assert.Equal("New requirements.", accepted.GetProperty("description").GetString());
        Assert.Equal(JsonValueKind.Null, accepted.GetProperty("gitLab").GetProperty("pendingChanges").ValueKind);
        Assert.Equal("gitlab.no_changes", await (await scenario.Tl.PostAsync($"/api/v1/tasks/{assignedId}/gitlab/accept-changes")).ProblemCodeAsync());
    }

    [Fact]
    public async Task Edge_case_29_a_deleted_issue_flags_the_task_once()
    {
        var (scenario, _, issues) = await LinkedProjectAsync();
        using var _ = scenario;
        await ImportAsync(scenario, 1);
        var taskId = await TaskIdForAsync(issues[0]);

        issues[0].Deleted = true;
        await SyncAsync(scenario);
        await SyncAsync(scenario);

        var task = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}");
        Assert.Equal("Missing", task.GetProperty("gitLab").GetProperty("state").GetString());
        Assert.Equal("Draft", task.GetProperty("status").GetString());
        Assert.Equal(1, await factory.WithDbAsync(db => db.Notifications.CountAsync(n => n.TaskId == taskId && n.Type == NotificationType.GitLabIssueMissing)));
    }

    [Fact]
    public async Task FR_IMP_09_rate_limits_are_retried_and_outages_are_reported_without_breaking_anything()
    {
        var (scenario, _, issues) = await LinkedProjectAsync();
        using var _ = scenario;

        GitLab.FailNext(HttpStatusCode.TooManyRequests);
        Assert.Equal(1, (await ImportAsync(scenario, 1)).GetProperty("created").GetInt32());

        GitLab.FailNext(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);
        var down = await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab/import", new { issueIids = new[] { 2 } });
        Assert.Equal(HttpStatusCode.BadGateway, down.StatusCode);
        Assert.Equal("gitlab.unavailable", await down.ProblemCodeAsync());
        var runs = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/gitlab/import-runs");
        Assert.Equal(1, runs[0].GetProperty("failed").GetInt32());

        // A scheduled sync that meets an outage records the failure and the app carries on.
        GitLab.FailNext(HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.BadGateway);
        await factory.Services.GetRequiredService<GitLabSyncWorker>().RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Draft", (await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{await TaskIdForAsync(issues[0])}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Webhooks_need_the_secret_and_only_trigger_a_fresh_read_from_GitLab()
    {
        var (scenario, _, issues) = await LinkedProjectAsync();
        using var _ = scenario;
        await ImportAsync(scenario, 1);
        var secret = await (await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link/webhook-secret")).JsonAsync();
        var path = secret.GetProperty("path").GetString()!;
        var stored = await factory.WithDbAsync(db => db.GitLabProjectLinks.AsNoTracking().SingleAsync(l => l.ProjectId == scenario.ProjectId));
        Assert.NotEqual(Encoding.UTF8.GetBytes(secret.GetProperty("secret").GetString()!), stored.WebhookSecretHash);

        // The payload's title is ignored: the app re-reads the issue from GitLab.
        issues[0].Change(i => i.Title = "Sign-up form v2");
        var payload = new { object_kind = "issue", object_attributes = new { iid = 1, title = "Forged title" } };
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Task<HttpResponseMessage> Send(string? token)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
            if (token is not null)
            {
                request.Headers.Add("X-Gitlab-Token", token);
            }

            return anonymous.SendAsync(request, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await Send(null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send("wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Send(secret.GetProperty("secret").GetString())).StatusCode);
        var task = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{await TaskIdForAsync(issues[0])}");
        Assert.Equal("Sign-up form v2", task.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Only_project_leads_with_gitlab_import_link_and_import()
    {
        var (scenario, remote, _) = await LinkedProjectAsync();
        using var _ = scenario;
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.GetAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.PostAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab/import", new { issueIids = new[] { 1 } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.GetAsync("/api/v1/gitlab/connection-options")).StatusCode);
        using var outsider = await factory.SignedInClientAsync(SystemRoles.TeamLeaderId);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link")).StatusCode);

        var link = await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}/gitlab-link");
        Assert.Equal(remote.Path, link.GetProperty("pathWithNamespace").GetString());
        var options = await scenario.Tl.GetJsonAsync<JsonElement>("/api/v1/gitlab/connection-options");
        Assert.Contains(options.EnumerateArray(), o => o.GetProperty("id").GetGuid() == link.GetProperty("connectionId").GetGuid());
        var found = await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/gitlab/connections/{link.GetProperty("connectionId").GetGuid()}/projects?search={remote.Path.Split('/')[1]}");
        Assert.Equal(remote.Id, Assert.Single(found.EnumerateArray()).GetProperty("id").GetInt64());

        // A linked connection cannot be deleted; unlinking works and leaves 204 behind.
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        Assert.Equal("gitlab.connection_in_use", await (await admin.DeleteAsync($"/api/v1/gitlab/connections/{link.GetProperty("connectionId").GetGuid()}")).ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Pm.DeleteAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Pm.GetAsync($"/api/v1/projects/{scenario.ProjectId}/gitlab-link")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/gitlab/connections/{link.GetProperty("connectionId").GetGuid()}")).StatusCode);
    }
}

using System.Net;
using System.Text.Json;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Work;

[Collection(ApiCollection.Name)]
public sealed class AlgorithmTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static async Task<Guid> LatestSubmissionAsync(TestClient client, Guid taskId) =>
        Id((await client.GetJsonAsync<JsonElement>($"/api/v1/tasks/{taskId}/algorithm"))[0]);

    [Fact]
    public async Task AC_ALG_1_work_cannot_start_until_the_algorithm_is_approved()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var response = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("task.algorithm_approval_required", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Submit_reject_resubmit_approve_keeps_every_version_and_then_allows_start()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/algorithm";

        var submitted = await scenario.Dev.PostAsync(path, new { content = "1. Check password\n2. Issue cookie" });
        var first = await LatestSubmissionAsync(scenario.Tl, Id(task));
        var rejected = await scenario.Tl.PostAsync($"{path}/{first}/reject", new { comment = "Handle lockout after 5 failures." });
        await scenario.Dev.PostAsync(path, new { content = "1. Check lockout\n2. Check password\n3. Issue cookie" });
        var second = await LatestSubmissionAsync(scenario.Tl, Id(task));
        var approved = await scenario.Tl.PostAsync($"{path}/{second}/approve", new { comment = "Good." });
        var versions = await scenario.Dev.GetJsonAsync<JsonElement>(path);
        var started = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/start");

        Assert.Equal("AlgorithmSubmitted", (await submitted.JsonAsync()).GetProperty("status").GetString());
        Assert.Equal("AwaitingAlgorithm", (await rejected.JsonAsync()).GetProperty("status").GetString());
        var approvedTask = await approved.JsonAsync();
        Assert.Equal("ReadyToStart", approvedTask.GetProperty("status").GetString());
        Assert.True(approvedTask.GetProperty("algorithmApproved").GetBoolean());
        Assert.Equal(
            [(2, "Approved", "Good."), (1, "Rejected", "Handle lockout after 5 failures.")],
            versions.EnumerateArray().Select(v => (v.GetProperty("version").GetInt32(), v.GetProperty("decision").GetString(), v.GetProperty("decisionComment").GetString())));
        Assert.Equal("1. Check password\n2. Issue cookie", versions[1].GetProperty("content").GetString());
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    }

    [Fact]
    public async Task AC_ALG_2_rejecting_without_a_comment_returns_400()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm", new { content = "Plan" });
        var submission = await LatestSubmissionAsync(scenario.Tl, Id(task));

        var response = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm/{submission}/reject", new { comment = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await response.JsonAsync()).GetProperty("errors").TryGetProperty("comment", out _));
    }

    [Fact]
    public async Task AC_ALG_3_a_team_leader_cannot_approve_the_algorithm_of_their_own_task()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.TlMember.Id);
        await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm", new { content = "My own plan" });
        var submission = await LatestSubmissionAsync(scenario.Tl, Id(task));
        var otherLead = await factory.CreateMemberAsync(SystemRoles.TeamLeaderId);
        using var otherLeadClient = factory.CreateTestClient();
        await otherLeadClient.SignInAsync(otherLead.Email, otherLead.Password);
        await scenario.AddMemberAsync(otherLead.Id, SystemRoles.TeamLeaderId);

        var own = await scenario.Tl.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm/{submission}/approve");
        var byManager = await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm/{submission}/approve");
        var byOtherLead = await otherLeadClient.PostAsync($"/api/v1/tasks/{Id(task)}/algorithm/{submission}/approve");

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal("algorithm.own_submission", await own.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byManager.StatusCode); // Q8: TL only by default
        Assert.Equal(HttpStatusCode.OK, byOtherLead.StatusCode);
    }

    [Fact]
    public async Task Only_the_assignee_submits_and_versions_are_visible_to_project_members()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/algorithm";

        var byLead = await scenario.Tl.PostAsync(path, new { content = "Lead's plan" });
        var empty = await scenario.Dev.PostAsync(path, new { content = "" });
        await scenario.Dev.PostAsync(path, new { content = "Plan" });
        var twice = await scenario.Dev.PostAsync(path, new { content = "Another plan" });
        var viewerList = await scenario.Viewer.GetAsync(path);

        Assert.Equal("algorithm.not_assignee", await byLead.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal("task.invalid_transition", await twice.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, viewerList.StatusCode);
    }
}

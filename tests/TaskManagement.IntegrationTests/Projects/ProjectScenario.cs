using System.Net;
using System.Text.Json;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;

namespace TaskManagement.IntegrationTests.Projects;

/// <summary>
/// A project created by a Project Manager, with a Team Leader, two Developers and a Viewer as members, each with a
/// signed-in client. Keys are random so scenarios never collide in the shared test database.
/// </summary>
public sealed class ProjectScenario : IDisposable
{
    private ProjectScenario(ApiFactory factory)
    {
        Factory = factory;
    }

    public ApiFactory Factory { get; }

    public Guid ProjectId { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public required TestClient Pm { get; init; }

    public required TestMember PmMember { get; init; }

    public required TestClient Tl { get; init; }

    public required TestMember TlMember { get; init; }

    public required TestClient Dev { get; init; }

    public required TestMember DevMember { get; init; }

    public required TestClient Dev2 { get; init; }

    public required TestMember Dev2Member { get; init; }

    public required TestClient Viewer { get; init; }

    public required TestMember ViewerMember { get; init; }

    public static string NewKey() => "P" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant().Replace('0', 'Z');

    public static async Task<ProjectScenario> CreateAsync(ApiFactory factory, bool algorithmRequiredDefault = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        async Task<(TestClient, TestMember)> SignedIn(Guid roleId)
        {
            var member = await factory.CreateMemberAsync(roleId);
            var client = factory.CreateTestClient();
            await client.SignInAsync(member.Email, member.Password);
            return (client, member);
        }

        var (pm, pmMember) = await SignedIn(SystemRoles.ProjectManagerId);
        var (tl, tlMember) = await SignedIn(SystemRoles.TeamLeaderId);
        var (dev, devMember) = await SignedIn(SystemRoles.DeveloperId);
        var (dev2, dev2Member) = await SignedIn(SystemRoles.DeveloperId);
        var (viewer, viewerMember) = await SignedIn(SystemRoles.ViewerId);
        var scenario = new ProjectScenario(factory)
        {
            Pm = pm,
            PmMember = pmMember,
            Tl = tl,
            TlMember = tlMember,
            Dev = dev,
            DevMember = devMember,
            Dev2 = dev2,
            Dev2Member = dev2Member,
            Viewer = viewer,
            ViewerMember = viewerMember,
        };

        scenario.Key = NewKey();
        var created = await pm.PostAsync("/api/v1/projects", new
        {
            key = scenario.Key,
            name = "Scenario " + scenario.Key,
            description = "Created by tests",
            algorithmRequiredDefault,
            closeOnApproval = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        scenario.ProjectId = (await created.JsonAsync()).GetProperty("id").GetGuid();

        await scenario.AddMemberAsync(tlMember.Id, SystemRoles.TeamLeaderId);
        await scenario.AddMemberAsync(devMember.Id, SystemRoles.DeveloperId);
        await scenario.AddMemberAsync(dev2Member.Id, SystemRoles.DeveloperId);
        await scenario.AddMemberAsync(viewerMember.Id, SystemRoles.ViewerId);
        return scenario;
    }

    public async Task AddMemberAsync(Guid userId, Guid projectRoleId)
    {
        var response = await Pm.PostAsync($"/api/v1/projects/{ProjectId}/members", new { userId, projectRoleId });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Adding a member returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public async Task<JsonElement> CreateTaskAsync(string title = "A task", object? fields = null, TestClient? client = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["description"] = "Details",
            ["priority"] = "Medium",
            ["type"] = "Feature",
            ["dueDate"] = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14)).ToString("yyyy-MM-dd"),
        };
        foreach (var property in fields?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(fields);
        }

        var response = await (client ?? Pm).PostAsync($"/api/v1/projects/{ProjectId}/tasks", body);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Creating a task returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    public async Task<JsonElement> CreateAssignedTaskAsync(Guid assigneeId, string title = "Assigned task", object? fields = null)
    {
        var task = await CreateTaskAsync(title, fields);
        var response = await Pm.PostAsync($"/api/v1/tasks/{task.GetProperty("id").GetGuid()}/assign", new { assigneeId });
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Assigning returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    public void Dispose()
    {
        Pm.Dispose();
        Tl.Dispose();
        Dev.Dispose();
        Dev2.Dispose();
        Viewer.Dispose();
    }
}

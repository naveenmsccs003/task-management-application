using System.Net;
using System.Text.Json;
using TaskManagement.Domain.Access;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Work;

[Collection(ApiCollection.Name)]
public sealed class ChecklistTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static object Template(Guid? projectId, string name, params (string Text, bool Mandatory)[] items) => new
    {
        projectId,
        name,
        isActive = true,
        items = items.Select(i => new { text = i.Text, isMandatory = i.Mandatory }).ToArray(),
        version = 0u,
    };

    [Fact]
    public async Task Global_templates_need_checklists_manage_and_project_templates_need_manage_project()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);

        var globalByAdmin = await admin.PostAsync("/api/v1/checklist-templates", Template(null, "DoD " + scenario.Key, ("Tests pass", true)));
        var globalByManager = await scenario.Pm.PostAsync("/api/v1/checklist-templates", Template(null, "Sneaky", ("x", false)));
        var projectByManager = await scenario.Pm.PostAsync("/api/v1/checklist-templates", Template(scenario.ProjectId, "Release", ("Changelog", false)));
        var projectByDeveloper = await scenario.Dev.PostAsync("/api/v1/checklist-templates", Template(scenario.ProjectId, "Mine", ("x", false)));
        var empty = await scenario.Pm.PostAsync("/api/v1/checklist-templates", Template(scenario.ProjectId, "Empty"));
        var usable = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/checklist-templates?projectId={scenario.ProjectId}");

        Assert.Equal(HttpStatusCode.Created, globalByAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, globalByManager.StatusCode);
        Assert.Equal(HttpStatusCode.Created, projectByManager.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, projectByDeveloper.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var names = usable.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("Release", names);
        Assert.Contains("DoD " + scenario.Key, names);
    }

    [Fact]
    public async Task FR_CHK_02_template_items_are_copied_and_later_template_changes_do_not_touch_the_task()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var template = await (await scenario.Pm.PostAsync("/api/v1/checklist-templates",
            Template(scenario.ProjectId, "DoD", ("Unit tests", true), ("Docs updated", false)))).JsonAsync();
        var task = await scenario.CreateTaskAsync(fields: new { checklistTemplateId = Id(template) });

        await scenario.Pm.PatchAsync($"/api/v1/checklist-templates/{Id(template)}", new
        {
            name = "DoD v2",
            isActive = true,
            items = new[] { new { text = "Something else", isMandatory = true } },
            version = template.GetProperty("version").GetUInt32(),
        });
        var items = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/checklist");

        Assert.Equal(
            [("Unit tests", true), ("Docs updated", false)],
            items.EnumerateArray().Select(i => (i.GetProperty("text").GetString(), i.GetProperty("isMandatory").GetBoolean())));
        var summary = task.GetProperty("checklist");
        Assert.Equal((2, 1), (summary.GetProperty("total").GetInt32(), summary.GetProperty("mandatoryTotal").GetInt32()));
    }

    [Fact]
    public async Task The_project_default_template_applies_to_new_tasks_and_an_unusable_template_is_rejected()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var other = await ProjectScenario.CreateAsync(factory);
        var template = await (await scenario.Pm.PostAsync("/api/v1/checklist-templates", Template(scenario.ProjectId, "Default", ("Reviewed", true)))).JsonAsync();
        var foreign = await (await other.Pm.PostAsync("/api/v1/checklist-templates", Template(other.ProjectId, "Foreign", ("x", true)))).JsonAsync();
        var project = await scenario.Pm.GetJsonAsync<JsonElement>($"/api/v1/projects/{scenario.ProjectId}");

        var setDefault = await scenario.Pm.PatchAsync($"/api/v1/projects/{scenario.ProjectId}", new
        {
            name = project.GetProperty("name").GetString(),
            description = "",
            algorithmRequiredDefault = false,
            closeOnApproval = false,
            defaultChecklistTemplateId = Id(template),
            version = project.GetProperty("version").GetUInt32(),
        });
        var task = await scenario.CreateTaskAsync();
        var withForeign = await scenario.Pm.PostAsync($"/api/v1/projects/{scenario.ProjectId}/tasks", new { title = "Bad template", checklistTemplateId = Id(foreign) });

        Assert.Equal(Id(template), (await setDefault.JsonAsync()).GetProperty("defaultChecklistTemplateId").GetGuid());
        Assert.Equal(1, task.GetProperty("checklist").GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, withForeign.StatusCode);
    }

    [Fact]
    public async Task Leads_add_items_and_only_the_assignee_ticks_them_once_the_task_is_ready()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateTaskAsync();
        var path = $"/api/v1/tasks/{Id(task)}/checklist";

        var added = await scenario.Tl.PostAsync(path, new { text = "Screenshots attached", isMandatory = true });
        var byDeveloper = await scenario.Dev.PostAsync(path, new { text = "Dev item", isMandatory = false });
        var itemId = Id((await added.JsonAsync())[0]);
        var byManager = await scenario.Pm.PostAsync($"{path}/{itemId}/complete");
        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/assign", new { assigneeId = scenario.DevMember.Id });
        var byLead = await scenario.Tl.PostAsync($"{path}/{itemId}/complete");
        var ticked = await scenario.Dev.PostAsync($"{path}/{itemId}/complete", new { note = "See comment" });
        var view = await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}");
        var unticked = await scenario.Dev.PostAsync($"{path}/{itemId}/uncomplete");

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byDeveloper.StatusCode); // the developer cannot see an unassigned draft
        Assert.Equal("checklist.not_assignee", await byManager.ProblemCodeAsync());
        Assert.Equal("checklist.not_assignee", await byLead.ProblemCodeAsync());
        var item = (await ticked.JsonAsync())[0];
        Assert.True(item.GetProperty("isCompleted").GetBoolean());
        Assert.Equal("See comment", item.GetProperty("note").GetString());
        Assert.Equal("Test Developer", item.GetProperty("completedBy").GetProperty("name").GetString());
        Assert.Equal(1, view.GetProperty("checklist").GetProperty("mandatoryDone").GetInt32());
        Assert.False((await unticked.JsonAsync())[0].GetProperty("isCompleted").GetBoolean());
    }
}

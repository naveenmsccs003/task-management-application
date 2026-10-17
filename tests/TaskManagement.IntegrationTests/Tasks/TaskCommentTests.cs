using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Tasks;

[Collection(ApiCollection.Name)]
public sealed class TaskCommentTests(ApiFactory factory)
{
    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    [Fact]
    public async Task Assignee_and_leads_comment_viewers_only_read()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/comments";

        var byDeveloper = await scenario.Dev.PostAsync(path, new { body = "Starting with the API." });
        var byLead = await scenario.Tl.PostAsync(path, new { body = "Remember the edge cases." });
        var byViewer = await scenario.Viewer.PostAsync(path, new { body = "Can I comment?" });
        var empty = await scenario.Dev.PostAsync(path, new { body = "  " });
        var list = await scenario.Viewer.GetJsonAsync<JsonElement>(path);

        Assert.Equal(HttpStatusCode.Created, byDeveloper.StatusCode);
        Assert.Equal(HttpStatusCode.Created, byLead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byViewer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(
            ["Starting with the API.", "Remember the edge cases."],
            list.EnumerateArray().Select(c => c.GetProperty("body").GetString()));
        Assert.All(list.EnumerateArray(), c => Assert.False(c.GetProperty("canEdit").GetBoolean()));
    }

    [Fact]
    public async Task Authors_edit_within_15_minutes_and_edits_keep_history()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var comment = await (await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/comments", new { body = "Frist draft" })).JsonAsync();
        var commentPath = $"/api/v1/tasks/{Id(task)}/comments/{Id(comment)}";

        var edited = await scenario.Dev.PatchAsync(commentPath, new { body = "First draft" });
        var byOther = await scenario.Tl.PatchAsync(commentPath, new { body = "Not yours" });
        await factory.WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE tasks.task_comments SET created_at = now() - interval '16 minutes' WHERE id = {Id(comment)}",
            TestContext.Current.CancellationToken));
        var tooLate = await scenario.Dev.PatchAsync(commentPath, new { body = "Third draft" });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await edited.JsonAsync();
        Assert.Equal("First draft", body.GetProperty("body").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("editedAt").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, byOther.StatusCode);
        Assert.Equal("comment.edit_window_closed", await tooLate.ProblemCodeAsync());
        var history = await factory.WithDbAsync(db => db.TaskCommentEdits.Where(e => e.CommentId == Id(comment)).Select(e => e.PreviousBody).ToListAsync());
        Assert.Equal(["Frist draft"], history);
    }

    [Fact]
    public async Task Deletion_is_soft_and_allowed_for_the_author_or_a_lead()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}/comments";
        var leadComment = await (await scenario.Tl.PostAsync(path, new { body = "Lead note" })).JsonAsync();
        var devComment = await (await scenario.Dev.PostAsync(path, new { body = "Off-topic" })).JsonAsync();

        var devDeletesLead = await scenario.Dev.DeleteAsync($"{path}/{Id(leadComment)}");
        var pmDeletesDev = await scenario.Pm.DeleteAsync($"{path}/{Id(devComment)}");
        var list = await scenario.Dev.GetJsonAsync<JsonElement>(path);

        Assert.Equal(HttpStatusCode.Forbidden, devDeletesLead.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, pmDeletesDev.StatusCode);
        var deleted = list.EnumerateArray().Single(c => Id(c) == Id(devComment));
        Assert.True(deleted.GetProperty("isDeleted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, deleted.GetProperty("body").ValueKind);
        Assert.True(await factory.WithDbAsync(db => db.TaskComments.AnyAsync(c => c.Id == Id(devComment))));
    }

    [Fact]
    public async Task Cancelled_tasks_take_no_new_comments()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = "Duplicate" });

        var response = await scenario.Dev.PostAsync($"/api/v1/tasks/{Id(task)}/comments", new { body = "Too late" });

        Assert.Equal("task.read_only", await response.ProblemCodeAsync());
    }
}

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Files;
using TaskManagement.Infrastructure.Jobs;
using TaskManagement.IntegrationTests.Infrastructure;
using TaskManagement.IntegrationTests.Projects;

namespace TaskManagement.IntegrationTests.Attachments;

[Collection(ApiCollection.Name)]
public sealed class AttachmentTests(ApiFactory factory)
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << >>\n%%EOF\n");
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 1, 2, 3];

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static async Task<HttpResponseMessage> UploadAsync(TestClient client, Guid taskId, string fileName, byte[] content, string ownerType = "Task")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); // never trusted
        form.Add(file, "file", fileName);
        return await client.Http.PostAsync(
            new Uri($"/api/v1/tasks/{taskId}/attachments?ownerType={ownerType}", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }

    private static async Task<JsonElement> UploadOkAsync(TestClient client, Guid taskId, string fileName, byte[] content, string ownerType = "Task")
    {
        var response = await UploadAsync(client, taskId, fileName, content, ownerType);
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Upload returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.JsonAsync();
    }

    private static byte[] Docx()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "[Content_Types].xml", "word/document.xml" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("<xml/>");
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public async Task AC_ATT_3_a_file_is_held_until_scanned_then_downloaded_with_safe_headers()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);

        var uploaded = await UploadOkAsync(scenario.Dev, Id(task), "../../Design notes.pdf", Pdf);
        Assert.Equal("Design notes.pdf", uploaded.GetProperty("fileName").GetString()); // path parts removed (FR-ATT-04)
        Assert.Equal("application/pdf", uploaded.GetProperty("contentType").GetString());
        Assert.Equal("Pending", uploaded.GetProperty("scanStatus").GetString());
        var download = $"/api/v1/attachments/{Id(uploaded)}/download";
        Assert.Equal("attachment.not_scanned", await (await scenario.Tl.GetAsync(download)).ProblemCodeAsync());

        await factory.ScanAttachmentsAsync();
        var response = await scenario.Tl.GetAsync(download);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Design notes.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));

        var list = await scenario.Viewer.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/attachments");
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("Clean", item.GetProperty("scanStatus").GetString());
        Assert.False(item.GetProperty("canPreview").GetBoolean()); // PDFs are downloaded, not shown inline
        Assert.Equal("attachment.no_preview", await (await scenario.Tl.GetAsync($"/api/v1/attachments/{Id(uploaded)}/preview")).ProblemCodeAsync());

        // FR-AUD-02: upload and download are audited; the stored name is random, not the user's.
        var audit = await factory.WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.EntityId == Id(uploaded).ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("attachment.uploaded", audit);
        Assert.Contains("attachment.downloaded", audit);
        var stored = await factory.WithDbAsync(db => db.Attachments.AsNoTracking().SingleAsync(a => a.Id == Id(uploaded)));
        Assert.DoesNotContain("Design", stored.StorageKey, StringComparison.Ordinal);
        Assert.Equal(32, stored.Sha256.Length);
    }

    [Fact]
    public async Task Images_can_be_previewed_inline_once_scanned()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var image = await UploadOkAsync(scenario.Dev, Id(task), "screen.png", Png);
        await factory.ScanAttachmentsAsync();

        var response = await scenario.Tl.GetAsync($"/api/v1/attachments/{Id(image)}/preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AC_ATT_1_members_who_cannot_see_the_task_get_not_found()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var uploaded = await UploadOkAsync(scenario.Dev, Id(task), "notes.pdf", Pdf);
        await factory.ScanAttachmentsAsync();

        // Dev2 sees only their own tasks; an outsider is in no project.
        using var outsider = await factory.SignedInClientAsync(SystemRoles.DeveloperId);
        foreach (var client in new[] { scenario.Dev2, outsider })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attachments/{Id(uploaded)}/download")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/attachments/{Id(uploaded)}/preview")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/tasks/{Id(task)}/attachments")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/v1/attachments/{Id(uploaded)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(client, Id(task), "x.pdf", Pdf)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Tl.GetAsync($"/api/v1/attachments/{Guid.NewGuid()}/download")).StatusCode);
    }

    [Fact]
    public async Task AC_ATT_2_files_are_checked_by_content_not_by_name()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var before = Directory.Exists(factory.StoragePath) ? Directory.GetFiles(factory.StoragePath, "*", SearchOption.AllDirectories).Length : 0;

        byte[] executable = [(byte)'M', (byte)'Z', 0x90, 0, 3, 0, 0, 0, 4, 0, 0, 0, 0xFF, 0xFF];
        var cases = new (string Name, byte[] Content)[]
        {
            ("report.pdf", executable), // AC-ATT-2
            ("photo.pdf", Png), // real type, wrong extension
            ("drawing.txt", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")),
            ("page.txt", Encoding.UTF8.GetBytes("<!DOCTYPE html><html><body>hi</body></html>")),
            ("data.csv", [0x41, 0x00, 0x42]), // binary pretending to be text
        };
        foreach (var (name, content) in cases)
        {
            var response = await UploadAsync(scenario.Dev, Id(task), name, content);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{name}: {(int)response.StatusCode}");
            Assert.Equal("attachment.type_mismatch", await response.ProblemCodeAsync());
        }

        Assert.Equal("attachment.empty", await (await UploadAsync(scenario.Dev, Id(task), "empty.txt", [])).ProblemCodeAsync());
        Assert.Equal(0, await factory.WithDbAsync(db => db.Attachments.CountAsync(a => a.TaskId == Id(task))));
        var after = Directory.Exists(factory.StoragePath) ? Directory.GetFiles(factory.StoragePath, "*", SearchOption.AllDirectories).Length : 0;
        Assert.Equal(before, after);

        // The same checks let real files through: Office documents are told apart inside their ZIP package.
        var docx = await UploadOkAsync(scenario.Dev, Id(task), "spec.docx", Docx());
        Assert.Equal("docx", docx.GetProperty("fileType").GetString());
        var text = await UploadOkAsync(scenario.Dev, Id(task), "notes.md", Encoding.UTF8.GetBytes("# Notes\n\nÜmlauts are fine.\n"));
        Assert.Equal("text/markdown; charset=utf-8", text.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Type_and_size_limits_come_from_the_global_settings()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        using var admin = await factory.SignedInClientAsync(SystemRoles.AdminId);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var original = (await admin.GetJsonAsync<JsonElement>("/api/v1/settings")).GetProperty("attachments");
        try
        {
            var allowed = original.GetProperty("allowedTypes").EnumerateArray().Select(t => t.GetString()).Where(t => t != "pdf").ToArray();
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/v1/settings/attachments", new { imageMaxMb = 1, documentMaxMb = 25, videoMaxMb = 200, allowedTypes = new[] { "svg" } })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsync("/api/v1/settings/attachments", new { imageMaxMb = 1, documentMaxMb = 25, videoMaxMb = 200, allowedTypes = allowed })).StatusCode);

            var rules = await scenario.Dev.GetJsonAsync<JsonElement>("/api/v1/attachment-rules");
            Assert.DoesNotContain("pdf", rules.GetProperty("allowedTypes").EnumerateArray().Select(t => t.GetProperty("code").GetString()));

            var refused = await UploadAsync(scenario.Dev, Id(task), "notes.pdf", Pdf);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
            Assert.Equal("attachment.type_not_allowed", await refused.ProblemCodeAsync());

            var big = new byte[(1024 * 1024) + 10];
            Png.CopyTo(big, 0);
            var tooLarge = await UploadAsync(scenario.Dev, Id(task), "big.png", big);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            Assert.Equal("attachment.too_large", await tooLarge.ProblemCodeAsync());
            Assert.Equal(0, await factory.WithDbAsync(db => db.Attachments.CountAsync(a => a.TaskId == Id(task))));
        }
        finally
        {
            await admin.PutAsync("/api/v1/settings/attachments", original);
        }
    }

    [Fact]
    public async Task Infected_files_lose_their_content_and_unscannable_files_end_as_failed()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var infected = await UploadOkAsync(scenario.Dev, Id(task), "eicar.txt", Encoding.ASCII.GetBytes(FakeMalwareScanner.EicarMarker + "-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"));
        var unreadable = await UploadOkAsync(scenario.Dev, Id(task), "odd.txt", Encoding.ASCII.GetBytes(FakeMalwareScanner.ErrorMarker));

        for (var run = 0; run < Attachment.MaxScanAttempts; run++)
        {
            await factory.ScanAttachmentsAsync();
        }

        var stored = await factory.WithDbAsync(db => db.Attachments.AsNoTracking().Where(a => a.TaskId == Id(task)).ToDictionaryAsync(a => a.Id));
        Assert.Equal(AttachmentScanStatus.Infected, stored[Id(infected)].ScanStatus);
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, stored[Id(infected)].StorageKey)));
        Assert.Equal(AttachmentScanStatus.Failed, stored[Id(unreadable)].ScanStatus);
        Assert.Equal(Attachment.MaxScanAttempts, stored[Id(unreadable)].ScanAttempts);
        Assert.Equal("attachment.infected", await (await scenario.Tl.GetAsync($"/api/v1/attachments/{Id(infected)}/download")).ProblemCodeAsync());
        Assert.Equal("attachment.scan_failed", await (await scenario.Tl.GetAsync($"/api/v1/attachments/{Id(unreadable)}/download")).ProblemCodeAsync());
        Assert.True(await factory.WithDbAsync(db => db.AuditEntries.AnyAsync(a => a.Action == "attachment.infected" && a.EntityId == Id(infected).ToString())));
    }

    [Fact]
    public async Task Comment_algorithm_and_review_files_are_drafts_until_their_record_is_saved()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory, algorithmRequiredDefault: true);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var path = $"/api/v1/tasks/{Id(task)}";

        // A comment draft is not listed, cannot be seen by others, and is linked when the comment is posted.
        var draft = await UploadOkAsync(scenario.Dev, Id(task), "log.txt", Encoding.UTF8.GetBytes("error at line 3"), "Comment");
        Assert.Equal(JsonValueKind.Null, draft.GetProperty("ownerId").ValueKind);
        Assert.Empty((await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/attachments")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await scenario.Tl.DeleteAsync($"/api/v1/attachments/{Id(draft)}")).StatusCode);

        // Someone else's draft, or an unknown id, cannot be attached.
        Assert.Equal("attachment.not_linkable", await (await scenario.Tl.PostAsync($"{path}/comments", new { body = "Mine now", attachmentIds = new[] { Id(draft) } })).ProblemCodeAsync());
        Assert.Equal("attachment.not_linkable", await (await scenario.Dev.PostAsync($"{path}/comments", new { body = "Ghost", attachmentIds = new[] { Guid.NewGuid() } })).ProblemCodeAsync());
        Assert.Empty(await factory.WithDbAsync(db => db.TaskComments.AsNoTracking().Where(c => c.TaskId == Id(task)).ToListAsync()));

        var comment = await (await scenario.Dev.PostAsync($"{path}/comments", new { body = "See the log.", attachmentIds = new[] { Id(draft) } })).JsonAsync();
        var listed = Assert.Single((await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/attachments")).EnumerateArray());
        Assert.Equal("Comment", listed.GetProperty("ownerType").GetString());
        Assert.Equal(Id(comment), listed.GetProperty("ownerId").GetGuid());
        Assert.Equal("attachment.not_linkable", await (await scenario.Dev.PostAsync($"{path}/comments", new { body = "Again", attachmentIds = new[] { Id(draft) } })).ProblemCodeAsync());

        // Only the assignee attaches algorithm and review files.
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(scenario.Tl, Id(task), "algo.md", Encoding.UTF8.GetBytes("steps"), "Algorithm")).StatusCode);
        var algorithmFile = await UploadOkAsync(scenario.Dev, Id(task), "flow.png", Png, "Algorithm");
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/algorithm", new { content = "1. Validate\n2. Save", attachmentIds = new[] { Id(algorithmFile) } })).StatusCode);
        var submissionId = (await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/algorithm"))[0].GetProperty("id").GetGuid();
        Assert.Equal(submissionId, await factory.WithDbAsync(db => db.Attachments.Where(a => a.Id == Id(algorithmFile)).Select(a => a.OwnerId).SingleAsync()));

        await scenario.Tl.PostAsync($"{path}/algorithm/{submissionId}/approve", new { comment = "OK" });
        Assert.Equal(HttpStatusCode.OK, (await scenario.Dev.PostAsync($"{path}/start")).StatusCode);
        var reviewFile = await UploadOkAsync(scenario.Dev, Id(task), "result.png", Png, "Review");
        var submitted = await scenario.Dev.PostAsync($"{path}/submit-review", new { summary = "Done, screenshot attached.", attachmentIds = new[] { Id(reviewFile) } });
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var owners = (await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/attachments")).EnumerateArray().Select(a => a.GetProperty("ownerType").GetString()).ToList();
        Assert.Equal(["Comment", "Algorithm", "Review"], owners);

        // A deleted comment takes its files with it.
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Dev.DeleteAsync($"{path}/comments/{Id(comment)}")).StatusCode);
        Assert.DoesNotContain("Comment", (await scenario.Tl.GetJsonAsync<JsonElement>($"{path}/attachments")).EnumerateArray().Select(a => a.GetProperty("ownerType").GetString()));
    }

    [Fact]
    public async Task FR_ATT_08_uploaders_and_leads_delete_softly_and_viewers_cannot_upload()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var devFile = await UploadOkAsync(scenario.Dev, Id(task), "a.pdf", Pdf);
        var tlFile = await UploadOkAsync(scenario.Tl, Id(task), "b.pdf", Pdf);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(scenario.Viewer, Id(task), "c.pdf", Pdf)).StatusCode);
        Assert.Contains("attach", (await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}")).GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()));

        var asDev = (await scenario.Dev.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/attachments")).EnumerateArray().ToDictionary(Id, a => a.GetProperty("canDelete").GetBoolean());
        Assert.True(asDev[Id(devFile)]);
        Assert.False(asDev[Id(tlFile)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await scenario.Dev.DeleteAsync($"/api/v1/attachments/{Id(tlFile)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Dev.DeleteAsync($"/api/v1/attachments/{Id(devFile)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await scenario.Tl.DeleteAsync($"/api/v1/attachments/{Id(tlFile)}")).StatusCode);
        Assert.Empty((await scenario.Tl.GetJsonAsync<JsonElement>($"/api/v1/tasks/{Id(task)}/attachments")).EnumerateArray());
        var deleted = await factory.WithDbAsync(db => db.Attachments.AsNoTracking().Where(a => a.TaskId == Id(task)).ToListAsync());
        Assert.All(deleted, a => Assert.NotNull(a.DeletedAt));
        Assert.All(deleted, a => Assert.True(File.Exists(Path.Combine(factory.StoragePath, a.StorageKey))));

        // A cancelled task takes no new files.
        await scenario.Pm.PostAsync($"/api/v1/tasks/{Id(task)}/cancel", new { reason = "No longer needed." });
        Assert.Equal("task.read_only", await (await UploadAsync(scenario.Tl, Id(task), "late.pdf", Pdf)).ProblemCodeAsync());
    }

    [Fact]
    public async Task Drafts_never_attached_are_removed_after_a_day()
    {
        using var scenario = await ProjectScenario.CreateAsync(factory);
        var task = await scenario.CreateAssignedTaskAsync(scenario.DevMember.Id);
        var stale = await UploadOkAsync(scenario.Dev, Id(task), "old.txt", Encoding.UTF8.GetBytes("old"), "Comment");
        var fresh = await UploadOkAsync(scenario.Dev, Id(task), "new.txt", Encoding.UTF8.GetBytes("new"), "Comment");
        var key = await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"UPDATE files.attachments SET uploaded_at = now() - interval '25 hours' WHERE id = {Id(stale)}");
            return await db.Attachments.Where(a => a.Id == Id(stale)).Select(a => a.StorageKey).SingleAsync();
        });

        await factory.Services.GetRequiredService<CleanupWorker>().RunOnceAsync(TestContext.Current.CancellationToken);

        var remaining = await factory.WithDbAsync(db => db.Attachments.Where(a => a.TaskId == Id(task)).Select(a => a.Id).ToListAsync());
        Assert.Equal([Id(fresh)], remaining);
        Assert.False(File.Exists(Path.Combine(factory.StoragePath, key)));
    }
}

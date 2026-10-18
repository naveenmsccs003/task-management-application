using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Checklists;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.GitLab;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.GitLab;

public sealed record GitLabProjectLinkDto(
    Guid Id,
    Guid ConnectionId,
    string ConnectionName,
    long GitLabProjectId,
    string PathWithNamespace,
    string WebUrl,
    bool SyncEnabled,
    DateTimeOffset? LastSyncedAt,
    bool HasWebhookSecret,
    string WebhookPath);

public sealed record LinkGitLabProjectCommand(Guid? ConnectionId, long? GitLabProjectId, bool? SyncEnabled);

public sealed record GitLabIssuePreviewDto(
    long Iid, string Title, bool IsClosed, IReadOnlyList<string> Labels, DateOnly? MilestoneDueDate, string WebUrl, DateTimeOffset UpdatedAt,
    Guid? TaskId, string? TaskKey);

public sealed record GitLabIssuePageDto(IReadOnlyList<GitLabIssuePreviewDto> Items, int Page, bool HasMore);

public sealed record ImportIssuesCommand(IReadOnlyList<long>? IssueIids);

public sealed record ImportRunDto(
    Guid Id, ImportRunKind Kind, UserRef? StartedBy, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, int Created, int Updated, int Skipped,
    int Failed, IReadOnlyList<string> Errors);

/// <summary>Shown once when generated; only its hash is stored.</summary>
public sealed record WebhookSecretDto(string Secret, string Path);

/// <summary>
/// Linking projects to GitLab, importing issues as Draft tasks and keeping them in step (FR-IMP-03..09,
/// task-workflow.md §8). Imports are idempotent (AC-IMP-1). GitLab never changes a task's state: a closed issue only
/// flags the task and tells the Team Leaders (AC-IMP-2). Title and description follow GitLab while the task is a Draft;
/// later changes wait for a TL to accept them.
/// </summary>
public sealed class GitLabImports(
    IAppDbContext db, ICurrentUser currentUser, ProjectAccess projects, TaskAccess tasks, TaskQueries queries, GitLabConnections connections,
    IGitLabClient gitLab, ChecklistTemplates templates, TaskChecklist checklist, Notifier notifier, IAuditLog auditLog, TimeProvider timeProvider)
{
    public const int MaxIssuesPerImport = 100;
    private const int IidsPerRequest = 100;

    public static string WebhookPath(Guid linkId) => $"/api/v1/webhooks/gitlab/{linkId}";

    public async Task<Result<IReadOnlyList<GitLabProjectInfo>>> SearchProjectsAsync(Guid connectionId, string? search, CancellationToken cancellationToken)
    {
        var scope = await projects.ProjectsWithAsync(PermissionCatalog.GitLabImport, cancellationToken);
        if (!scope.Everywhere && scope.ProjectWide.Count == 0)
        {
            return CommonErrors.Forbidden("import GitLab issues");
        }

        var connection = await db.GitLabConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
        {
            return CommonErrors.NotFound("GitLab_connection");
        }

        return await CallAsync(() => gitLab.SearchProjectsAsync(connections.EndpointFor(connection), search?.Trim(), cancellationToken));
    }

    public async Task<Result<GitLabProjectLinkDto?>> GetLinkAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var link = await db.GitLabProjectLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ProjectId == projectId, cancellationToken);
        return link is null ? Result.Success<GitLabProjectLinkDto?>(null) : await ToDtoAsync(link, cancellationToken);
    }

    /// <summary>Links (or re-links) the project; the GitLab project is read first so a wrong ID or token is caught now.</summary>
    public async Task<Result<GitLabProjectLinkDto?>> LinkAsync(Guid projectId, LinkGitLabProjectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        if (access.Value!.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var errors = new FieldErrors();
        if (command.ConnectionId is null)
        {
            errors.Add("connectionId", "Choose a GitLab connection.");
        }

        if (command.GitLabProjectId is not > 0)
        {
            errors.Add("gitLabProjectId", "Choose a GitLab project.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var connection = await db.GitLabConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == command.ConnectionId, cancellationToken);
        if (connection is null)
        {
            return new FieldErrors().Add("connectionId", "Choose a GitLab connection.").ToError();
        }

        var remote = await CallAsync(() => gitLab.GetProjectAsync(connections.EndpointFor(connection), command.GitLabProjectId!.Value, cancellationToken));
        if (remote.IsFailure)
        {
            return remote.Error!;
        }

        var now = timeProvider.GetUtcNow();
        var link = await db.GitLabProjectLinks.FirstOrDefaultAsync(l => l.ProjectId == projectId, cancellationToken);
        if (link is not null && (link.ConnectionId != connection.Id || link.GitLabProjectId != remote.Value!.Id))
        {
            db.GitLabProjectLinks.Remove(link);
            await db.SaveChangesAsync(cancellationToken);
            link = null;
        }

        if (link is null)
        {
            link = GitLabProjectLink.Create(projectId, connection.Id, remote.Value!.Id, remote.Value.PathWithNamespace, remote.Value.WebUrl, currentUser.RequiredUserId, now);
            db.GitLabProjectLinks.Add(link);
        }

        link.SetSyncEnabled(command.SyncEnabled ?? true);
        auditLog.Record("gitlab.project_linked", "Project", projectId.ToString(), new AuditChanges()
            .Set("connection", connection.Name)
            .Set("gitLabProject", link.PathWithNamespace)
            .Set("syncEnabled", link.SyncEnabled), projectId);
        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(link, cancellationToken);
    }

    /// <summary>Removes the link; imported tasks keep their issue links but are no longer synced.</summary>
    public async Task<Result> UnlinkAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var link = await db.GitLabProjectLinks.FirstOrDefaultAsync(l => l.ProjectId == projectId, cancellationToken);
        if (link is not null)
        {
            db.GitLabProjectLinks.Remove(link);
            auditLog.Record("gitlab.project_unlinked", "Project", projectId.ToString(), new AuditChanges().Set("gitLabProject", link.PathWithNamespace), projectId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>A new random webhook secret (FR-IMP-06), replacing any earlier one. It is returned once and stored hashed.</summary>
    public async Task<Result<WebhookSecretDto>> NewWebhookSecretAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var link = await db.GitLabProjectLinks.FirstOrDefaultAsync(l => l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return NotLinked();
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        link.SetWebhookSecretHash(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
        auditLog.Record("gitlab.webhook_secret_generated", "Project", projectId.ToString(), null, projectId);
        await db.SaveChangesAsync(cancellationToken);
        return new WebhookSecretDto(secret, WebhookPath(link.Id));
    }

    /// <summary>Issues of the linked GitLab project, marked when they are already imported.</summary>
    public async Task<Result<GitLabIssuePageDto>> PreviewIssuesAsync(Guid projectId, string? state, string? search, int? page, CancellationToken cancellationToken)
    {
        var loaded = await LoadLinkAsync(projectId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (link, connection) = loaded.Value;
        var issueState = state is "closed" or "all" ? state : "opened";
        var remote = await CallAsync(() => gitLab.ListIssuesAsync(
            connections.EndpointFor(connection), link.GitLabProjectId, issueState, search?.Trim(), Math.Max(page ?? 1, 1), cancellationToken));
        if (remote.IsFailure)
        {
            return remote.Error!;
        }

        var iids = remote.Value!.Items.Select(i => i.Iid).ToList();
        var imported = await db.GitLabIssueLinks.AsNoTracking()
            .Where(l => l.ConnectionId == connection.Id && l.GitLabProjectId == link.GitLabProjectId && iids.Contains(l.IssueIid))
            .Join(db.Tasks, l => l.TaskId, t => t.Id, (l, t) => new { l.IssueIid, t.Id, t.Key })
            .ToDictionaryAsync(x => x.IssueIid, cancellationToken);
        var items = remote.Value.Items.Select(i => new GitLabIssuePreviewDto(
            i.Iid, i.Title, i.IsClosed, i.Labels, i.MilestoneDueDate, i.WebUrl, i.UpdatedAt,
            imported.TryGetValue(i.Iid, out var task) ? task.Id : null, imported.TryGetValue(i.Iid, out var same) ? same.Key : null)).ToList();
        return new GitLabIssuePageDto(items, remote.Value.Page, remote.Value.HasMore);
    }

    /// <summary>
    /// Imports the chosen open issues as Draft tasks (FR-IMP-03/05). An issue imported before is not imported again: its
    /// link is refreshed instead (FR-IMP-04). Assignees are never mapped (Q10).
    /// </summary>
    public async Task<Result<ImportRunDto>> ImportAsync(Guid projectId, ImportIssuesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var iids = (command.IssueIids ?? []).Where(i => i > 0).Distinct().ToList();
        if (iids.Count is 0 or > MaxIssuesPerImport)
        {
            return new FieldErrors().Add("issueIids", $"Choose between 1 and {MaxIssuesPerImport} issues.").ToError();
        }

        var loaded = await LoadLinkAsync(projectId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (link, connection) = loaded.Value;
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, cancellationToken);
        if (!project.AcceptsNewTasks)
        {
            return Error.Conflict("project.not_active", $"Tasks can only be created in active projects. This project is {Project.Describe(project.Status)}.");
        }

        if (!await HasActiveLeadAsync(projectId, cancellationToken))
        {
            return Error.Conflict("project.no_lead", "Add an active Team Leader or Project Manager to the project before importing issues.");
        }

        var userId = currentUser.RequiredUserId;
        var now = timeProvider.GetUtcNow();
        var run = ImportRun.Start(projectId, ImportRunKind.Import, userId, now);
        var fetched = await CallAsync(() => gitLab.GetIssuesAsync(connections.EndpointFor(connection), link.GitLabProjectId, iids, cancellationToken));
        if (fetched.IsFailure)
        {
            run.Failed(fetched.Error!.Message);
            await FinishAsync(run, cancellationToken);
            return fetched.Error!;
        }

        var issues = fetched.Value!.ToDictionary(i => i.Iid);
        var existing = await db.GitLabIssueLinks
            .Where(l => l.ConnectionId == connection.Id && l.GitLabProjectId == link.GitLabProjectId && iids.Contains(l.IssueIid))
            .ToDictionaryAsync(l => l.IssueIid, cancellationToken);
        var template = project.DefaultChecklistTemplateId is { } templateId ? await templates.FindUsableAsync(templateId, projectId, cancellationToken) : null;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        foreach (var iid in iids)
        {
            if (!issues.TryGetValue(iid, out var issue))
            {
                run.Failed($"Issue #{iid} was not found in {link.PathWithNamespace}.");
                continue;
            }

            if (existing.TryGetValue(iid, out var issueLink))
            {
                issueLink.Observe(issue.IsClosed ? GitLabIssueState.Closed : GitLabIssueState.Opened, issue.WebUrl, issueLink.GitLabUpdatedAt, now);
                run.Updated();
                continue;
            }

            if (issue.IsClosed)
            {
                run.Skipped();
                continue;
            }

            var labels = TaskItem.NormalizeLabels(issue.Labels.Select(l => l.Length > TaskItem.LabelMaxLength ? l[..TaskItem.LabelMaxLength] : l))
                .Take(TaskItem.MaxLabels).ToList();
            var details = new TaskDetails(
                CleanTitle(issue),
                CleanDescription(issue.Description),
                labels.Exists(l => l.Equals("bug", StringComparison.OrdinalIgnoreCase)) ? TaskType.Bug : TaskType.Feature,
                TaskPriority.Medium,
                null,
                issue.MilestoneDueDate is { } due && due >= today ? due : null,
                null,
                project.AlgorithmRequiredDefault,
                labels);
            var number = await db.AllocateTaskNumberAsync(projectId, cancellationToken);
            var task = TaskItem.Create(projectId, project.Key, number, details, userId, now);
            db.Tasks.Add(task);
            if (template is not null)
            {
                checklist.Apply(task, template);
            }

            db.GitLabIssueLinks.Add(GitLabIssueLink.Create(
                task.Id, connection.Id, link.GitLabProjectId, issue.Iid, issue.Id, issue.WebUrl, GitLabIssueState.Opened,
                issue.UpdatedAt.AddTicks(-(issue.UpdatedAt.Ticks % 10)), now));
            auditLog.Record("task.imported", "Task", task.Id.ToString(), new AuditChanges()
                .Set("key", task.Key)
                .Set("gitLabIssue", $"{link.PathWithNamespace}#{issue.Iid}"), projectId);
            run.Created();
        }

        await FinishAsync(run, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await RunDtoAsync(run, cancellationToken);
    }

    /// <summary>Manual "sync now" for one project.</summary>
    public async Task<Result<ImportRunDto>> SyncNowAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await LoadLinkAsync(projectId, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var run = await SyncLinkAsync(loaded.Value.Link.Id, ImportRunKind.Sync, currentUser.RequiredUserId, null, cancellationToken);
        return await RunDtoAsync(run, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ImportRunDto>>> RunsAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var runs = await db.ImportRuns.AsNoTracking().Where(r => r.ProjectId == projectId).OrderByDescending(r => r.StartedAt).Take(20).ToListAsync(cancellationToken);
        var result = new List<ImportRunDto>();
        foreach (var run in runs)
        {
            result.Add(await RunDtoAsync(run, cancellationToken));
        }

        return Result.Success<IReadOnlyList<ImportRunDto>>(result);
    }

    /// <summary>Applies the title and description changed in GitLab after the task left Draft (tasks.update).</summary>
    public async Task<Result<TaskDto>> AcceptChangesAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await tasks.LoadAsync(taskId, track: true, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksUpdate))
        {
            return CommonErrors.Forbidden("edit this task");
        }

        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        var issueLink = await db.GitLabIssueLinks.FirstOrDefaultAsync(l => l.TaskId == taskId, cancellationToken);
        if (issueLink?.PendingChanges is not { } changes)
        {
            return Error.Conflict("gitlab.no_changes", "There are no GitLab changes waiting for this task.");
        }

        var task = visible.Task;
        var result = task.UpdateDetails(DetailsOf(task) with { Title = changes.Title ?? task.Title, Description = changes.Description ?? task.Description },
            visible.UserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        issueLink.SetPendingChanges(null);
        auditLog.Record("task.gitlab_changes_accepted", "Task", task.Id.ToString(), new AuditChanges()
            .Set("title", changes.Title is not null)
            .Set("description", changes.Description is not null), task.ProjectId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return CommonErrors.ConcurrencyConflict();
        }

        return await queries.GetAsync(taskId, cancellationToken);
    }

    /// <summary>
    /// The webhook (FR-IMP-06, security.md S18): the secret is compared as a hash in constant time, and the payload only
    /// says which issue to re-read from the GitLab API. Unknown links and wrong secrets look the same (not found).
    /// </summary>
    public async Task<Result> HandleWebhookAsync(Guid linkId, string? secret, long? issueIid, CancellationToken cancellationToken)
    {
        var link = await db.GitLabProjectLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link?.WebhookSecretHash is not { } expected || string.IsNullOrEmpty(secret)
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), expected))
        {
            return Error.NotFound("gitlab.webhook_unknown", "Unknown webhook.");
        }

        if (issueIid is { } iid && link.SyncEnabled
            && await db.GitLabIssueLinks.AnyAsync(l => l.ConnectionId == link.ConnectionId && l.GitLabProjectId == link.GitLabProjectId && l.IssueIid == iid, cancellationToken))
        {
            await SyncLinkAsync(link.Id, ImportRunKind.Webhook, null, [iid], cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Re-reads the linked issues (all, or <paramref name="onlyIids"/>) and applies task-workflow.md §8. Failures are
    /// recorded on the run, never thrown, so one broken project does not stop the scheduled sync of the others.
    /// </summary>
    public async Task<ImportRun> SyncLinkAsync(Guid linkId, ImportRunKind kind, Guid? startedBy, IReadOnlyCollection<long>? onlyIids, CancellationToken cancellationToken)
    {
        var link = await db.GitLabProjectLinks.FirstAsync(l => l.Id == linkId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var run = ImportRun.Start(link.ProjectId, kind, startedBy, now);
        var connection = await db.GitLabConnections.AsNoTracking().FirstAsync(c => c.Id == link.ConnectionId, cancellationToken);
        var issueLinks = await db.GitLabIssueLinks
            .Where(l => l.ConnectionId == link.ConnectionId && l.GitLabProjectId == link.GitLabProjectId)
            .Where(l => onlyIids == null || onlyIids.Contains(l.IssueIid))
            .ToListAsync(cancellationToken);
        var taskIds = issueLinks.Select(l => l.TaskId).ToList();
        var linkedTasks = await db.Tasks.Where(t => taskIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        IReadOnlyList<Guid>? leads = null;

        foreach (var chunk in issueLinks.Chunk(IidsPerRequest))
        {
            var fetched = await CallAsync(() => gitLab.GetIssuesAsync(connections.EndpointFor(connection), link.GitLabProjectId, chunk.Select(l => l.IssueIid).ToList(), cancellationToken));
            if (fetched.IsFailure)
            {
                foreach (var _ in chunk)
                {
                    run.Failed(fetched.Error!.Message);
                }

                continue;
            }

            var issues = fetched.Value!.ToDictionary(i => i.Iid);
            foreach (var issueLink in chunk)
            {
                var task = linkedTasks[issueLink.TaskId];
                issues.TryGetValue(issueLink.IssueIid, out var issue);
                var (changed, notify) = Apply(issueLink, task, issue, startedBy ?? link.CreatedBy, now);
                if (notify is { } type)
                {
                    leads ??= await notifier.ProjectLeadsAsync(link.ProjectId, cancellationToken);
                    var (title, body) = type == NotificationType.GitLabIssueClosed
                        ? ($"GitLab issue of {task.Key} was closed", $"The GitLab issue for “{task.Title}” was closed. The task itself was not changed.")
                        : ($"GitLab issue of {task.Key} is missing", $"The GitLab issue for “{task.Title}” was moved or deleted. The task itself was not changed.");
                    await notifier.NotifyAsync(leads, type, title, body, $"/tasks/{task.Id}", task.ProjectId, task.Id, cancellationToken);
                }

                if (changed)
                {
                    run.Updated();
                }
                else
                {
                    run.Skipped();
                }
            }
        }

        if (run.FailedCount == 0)
        {
            link.MarkSynced(now);
        }

        await FinishAsync(run, cancellationToken);
        return run;
    }

    /// <summary>
    /// One issue's news: whether anything changed, and the notification to send to the Team Leaders, if any.
    /// </summary>
    private static (bool Changed, NotificationType? Notify) Apply(GitLabIssueLink issueLink, TaskItem task, GitLabIssue? issue, Guid actor, DateTimeOffset now)
    {
        if (issue is null)
        {
            // Edge case 29: moved or deleted. The task is flagged (through the link), never deleted.
            var wasMissing = issueLink.Observe(GitLabIssueState.Missing, issueLink.WebUrl, null, now) == GitLabIssueState.Missing;
            return wasMissing ? (false, null) : (true, NotificationType.GitLabIssueMissing);
        }

        // PostgreSQL keeps microseconds; compare at that precision so an unchanged issue never looks newer.
        var lastSeen = issueLink.GitLabUpdatedAt;
        var updatedAt = issue.UpdatedAt.AddTicks(-(issue.UpdatedAt.Ticks % 10));
        var newState = issue.IsClosed ? GitLabIssueState.Closed : GitLabIssueState.Opened;
        var changed = issueLink.Observe(newState, issue.WebUrl, updatedAt, now) != newState;
        NotificationType? notify = null;
        if (task.SetGitLabIssueClosed(issue.IsClosed))
        {
            changed = true;
            if (issue.IsClosed)
            {
                notify = NotificationType.GitLabIssueClosed; // AC-IMP-2: flag and tell the TLs; the state stays as it is
            }
        }

        if (lastSeen is null || updatedAt > lastSeen)
        {
            changed = true;
            var title = CleanTitle(issue);
            var description = CleanDescription(issue.Description);
            if (task.Status == TaskItemStatus.Draft)
            {
                task.UpdateDetails(DetailsOf(task) with { Title = title, Description = description }, actor, now);
                issueLink.SetPendingChanges(null);
            }
            else if (!TaskWorkflow.IsFinal(task.Status))
            {
                issueLink.SetPendingChanges(new GitLabPendingChanges(
                    title != task.Title ? title : null,
                    description != task.Description ? description : null));
            }
        }

        return (changed, notify);
    }

    private static TaskDetails DetailsOf(TaskItem task) =>
        new(task.Title, task.Description, task.Type, task.Priority, task.StartDate, task.DueDate, task.AllocatedMinutes, task.AlgorithmRequired, task.Labels);

    private static string CleanTitle(GitLabIssue issue)
    {
        var title = Clean(issue.Title).ReplaceLineEndings(" ").Trim();
        if (title.Length == 0)
        {
            title = $"GitLab issue #{issue.Iid}";
        }

        return title.Length > TaskItem.TitleMaxLength ? title[..TaskItem.TitleMaxLength] : title;
    }

    /// <summary>Descriptions are kept as text (shown escaped, never as HTML); control characters are dropped and the length capped.</summary>
    private static string CleanDescription(string? description)
    {
        var text = Clean(description ?? string.Empty).ReplaceLineEndings("\n").Trim();
        return text.Length > TaskItem.DescriptionMaxLength ? text[..TaskItem.DescriptionMaxLength] : text;
    }

    private static string Clean(string value) =>
        new(value.Where(c => !char.IsControl(c) || c is '\n' or '\r' or '\t').ToArray());

    private async Task<Result<Project>> LoadProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var loaded = await projects.LoadVisibleAsync(projectId, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.GitLabImport))
        {
            return CommonErrors.Forbidden("import GitLab issues in this project");
        }

        return found.Project;
    }

    private async Task<Result<(GitLabProjectLink Link, GitLabConnection Connection)>> LoadLinkAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var access = await LoadProjectAsync(projectId, cancellationToken);
        if (access.IsFailure)
        {
            return access.Error!;
        }

        var link = await db.GitLabProjectLinks.AsNoTracking().FirstOrDefaultAsync(l => l.ProjectId == projectId, cancellationToken);
        if (link is null)
        {
            return NotLinked();
        }

        var connection = await db.GitLabConnections.AsNoTracking().FirstAsync(c => c.Id == link.ConnectionId, cancellationToken);
        return (link, connection);
    }

    private async Task FinishAsync(ImportRun run, CancellationToken cancellationToken)
    {
        run.Finish(timeProvider.GetUtcNow());
        db.ImportRuns.Add(run);
        auditLog.Record(run.Kind == ImportRunKind.Import ? "gitlab.imported" : "gitlab.synced", "Project", run.ProjectId.ToString(), new AuditChanges()
            .Set("created", run.CreatedCount)
            .Set("updated", run.UpdatedCount)
            .Set("skipped", run.SkippedCount)
            .Set("failed", run.FailedCount), run.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ImportRunDto> RunDtoAsync(ImportRun run, CancellationToken cancellationToken)
    {
        var by = run.StartedBy is { } id
            ? await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => new UserRef(u.Id, u.FullName)).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new ImportRunDto(run.Id, run.Kind, by, run.StartedAt, run.FinishedAt, run.CreatedCount, run.UpdatedCount, run.SkippedCount, run.FailedCount, run.Errors);
    }

    private async Task<GitLabProjectLinkDto?> ToDtoAsync(GitLabProjectLink link, CancellationToken cancellationToken)
    {
        var name = await db.GitLabConnections.AsNoTracking().Where(c => c.Id == link.ConnectionId).Select(c => c.Name).FirstAsync(cancellationToken);
        return new GitLabProjectLinkDto(
            link.Id, link.ConnectionId, name, link.GitLabProjectId, link.PathWithNamespace, link.WebUrl, link.SyncEnabled, link.LastSyncedAt,
            link.WebhookSecretHash is not null, WebhookPath(link.Id));
    }

    private Task<bool> HasActiveLeadAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AnyAsync(
            m => m.ProjectId == projectId
                && m.RemovedAt == null
                && (m.ProjectRoleId == SystemRoles.TeamLeaderId || m.ProjectRoleId == SystemRoles.ProjectManagerId)
                && db.Users.Any(u => u.Id == m.UserId && u.IsActive),
            cancellationToken);

    private static Error NotLinked() =>
        Error.Conflict("gitlab.not_linked", "Link this project to a GitLab project first.");

    /// <summary>GitLab failures become 502 answers with a safe message (FR-IMP-09); they never reach other features.</summary>
    private static async Task<Result<T>> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (GitLabException exception)
        {
            return Error.Upstream($"gitlab.{exception.Failure.ToString().ToLowerInvariant()}", exception.Message);
        }
    }
}

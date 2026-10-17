using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Deadlines;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Tasks;
using TaskManagement.Application.Work;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Requests;

public sealed record ApprovalRequestDto(
    Guid Id,
    Guid TaskId,
    string TaskKey,
    string TaskTitle,
    Guid ProjectId,
    ApprovalRequestType Type,
    ApprovalRequestStatus Status,
    string Reason,
    UserRef? RequestedBy,
    DateTimeOffset RequestedAt,
    UserRef? ProposedAssignee,
    bool AlgorithmReapprovalRequired,
    DateOnly? CurrentDueDate,
    DateOnly? ProposedDueDate,
    DateOnly? ApprovedDueDate,
    UserRef? DecidedBy,
    DateTimeOffset? DecidedAt,
    string? DecisionComment,
    bool CanDecide,
    bool CanWithdraw);

public sealed record RequestReassignmentCommand(Guid NewAssigneeId, string? Reason, bool AlgorithmReapprovalRequired);

public sealed record RequestExtensionCommand(DateOnly? ProposedDueDate, string? Reason);

/// <summary>Approve (optionally with another due date for an extension, FR-DL-02) or reject (comment required).</summary>
public sealed record DecideRequestCommand(string? Comment, DateOnly? ApprovedDueDate);

/// <summary>
/// Reassignment and deadline-extension requests (task-workflow.md §5–6, FR-REV-02..06, FR-DL-01..04). Approver rules
/// (A6/Q7): the requester never decides; a TL's request is decided by a PM and a PM's by a TL — a Global grant (Admin)
/// may decide any request; a developer's extension may be decided by any TL or PM of the project.
/// </summary>
public sealed class RequestService(
    IAppDbContext db,
    TaskAccess access,
    ProjectAccess projects,
    WorkService work,
    Notifier notifier,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private static readonly Error PendingExists =
        Error.Conflict("request.pending_exists", "A request of this kind is already waiting for a decision on this task.");

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> ListForTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        var rows = await db.ApprovalRequests.AsNoTracking().Where(r => r.TaskId == taskId).OrderByDescending(r => r.RequestedAt).ToListAsync(cancellationToken);
        return Result.Success(await ToDtosAsync(rows, cancellationToken));
    }

    /// <summary>The caller's approval queue: requests they may decide, in projects where they hold an approve permission.</summary>
    public async Task<IReadOnlyList<ApprovalRequestDto>> QueueAsync(ApprovalRequestStatus? status, Guid? projectId, CancellationToken cancellationToken)
    {
        var reassign = await projects.ProjectsWithAsync(PermissionCatalog.ReassignmentsApprove, cancellationToken);
        var extend = await projects.ProjectsWithAsync(PermissionCatalog.DeadlinesApprove, cancellationToken);
        var reassignProjects = reassign.ProjectWide.ToList();
        var extendProjects = extend.ProjectWide.ToList();
        var everywhereReassign = reassign.Everywhere;
        var everywhereExtend = extend.Everywhere;
        var wanted = status ?? ApprovalRequestStatus.Pending;

        var query = db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == wanted)
            .Join(db.Tasks, r => r.TaskId, t => t.Id, (r, t) => new { Request = r, t.ProjectId })
            .Where(x => (x.Request.Type == ApprovalRequestType.Reassignment && (everywhereReassign || reassignProjects.Contains(x.ProjectId)))
                || (x.Request.Type == ApprovalRequestType.DeadlineExtension && (everywhereExtend || extendProjects.Contains(x.ProjectId))));
        if (projectId is { } id)
        {
            query = query.Where(x => x.ProjectId == id);
        }

        var rows = await query.OrderBy(x => x.Request.RequestedAt).Take(200).Select(x => x.Request).ToListAsync(cancellationToken);
        var dtos = await ToDtosAsync(rows, cancellationToken);
        return wanted == ApprovalRequestStatus.Pending ? dtos.Where(d => d.CanDecide).ToList() : dtos;
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> RequestReassignmentAsync(
        Guid taskId, RequestReassignmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Permissions.HasProjectWide(PermissionCatalog.TasksAssign))
        {
            return CommonErrors.Forbidden("request a reassignment of this task");
        }

        var task = visible.Task;
        var check = Writable(visible);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (!TaskWorkflow.CanReassign(task.Status))
        {
            return Error.Conflict("task.cannot_reassign", $"Task {task.Key} is '{TaskWorkflow.Describe(task.Status)}' and cannot be reassigned now.");
        }

        var errors = ValidateReason(command.Reason);
        if (command.NewAssigneeId == task.AssigneeId)
        {
            errors.Add("newAssigneeId", "Choose someone other than the current assignee.");
        }
        else if (!await projects.CanWorkOnTasksAsync(task.ProjectId, command.NewAssigneeId, cancellationToken))
        {
            errors.Add("newAssigneeId", "Choose an active project member whose project role can work on tasks.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var request = ApprovalRequest.Reassignment(task.Id, command.NewAssigneeId, command.AlgorithmReapprovalRequired, command.Reason!, visible.UserId, timeProvider.GetUtcNow());
        return await AddAsync(visible, request, NotificationType.ReassignmentRequested, $"Reassignment of {task.Key} needs approval",
            $"A reassignment of “{task.Title}” was requested: {request.Reason}", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> RequestExtensionAsync(
        Guid taskId, RequestExtensionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        // Own scope means the current assignee (FR-DL-01); TL/PM hold it project-wide.
        var scope = visible.Permissions.Scope(PermissionCatalog.DeadlinesRequest);
        if (scope is null || (scope == PermissionScope.Own && !visible.IsAssignee))
        {
            return CommonErrors.Forbidden("request a deadline extension for this task");
        }

        var task = visible.Task;
        var check = Writable(visible);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (task.Status == TaskItemStatus.Draft)
        {
            return Error.Conflict("request.draft", $"Task {task.Key} is still a draft: edit its due date directly.");
        }

        if (!TaskWorkflow.IsOpen(task.Status) || task.DueDate is not { } due)
        {
            return Error.Conflict("request.task_not_open", $"Task {task.Key} has no open deadline to extend.");
        }

        var errors = ValidateReason(command.Reason);
        ValidateNewDueDate(errors, "proposedDueDate", command.ProposedDueDate, due);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var request = ApprovalRequest.DeadlineExtension(task.Id, due, command.ProposedDueDate!.Value, command.Reason!, visible.UserId, timeProvider.GetUtcNow());
        return await AddAsync(visible, request, NotificationType.ExtensionRequested, $"Deadline extension for {task.Key} needs approval",
            $"An extension of “{task.Title}” from {due:yyyy-MM-dd} to {request.ProposedDueDate:yyyy-MM-dd} was requested: {request.Reason}", cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> ApproveAsync(Guid requestId, DecideRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadForDecisionAsync(requestId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var (request, visible) = guard.Value;
        var task = visible.Task;
        var now = timeProvider.GetUtcNow();
        var errors = new FieldErrors().MaxLength("comment", command.Comment?.Trim(), ApprovalRequest.CommentMaxLength, "The comment");
        if (request.Type == ApprovalRequestType.DeadlineExtension && command.ApprovedDueDate is { } chosen)
        {
            // Edge case 25: never a date in the past; it must still extend the current due date.
            ValidateNewDueDate(errors, "approvedDueDate", chosen, task.DueDate ?? chosen.AddDays(-1));
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var decided = request.Approve(visible.UserId, command.Comment, command.ApprovedDueDate, now);
        if (decided.IsFailure)
        {
            return decided.Error!;
        }

        if (request.Type == ApprovalRequestType.Reassignment)
        {
            var applied = await ApplyReassignmentAsync(request, visible, now, cancellationToken);
            if (applied.IsFailure)
            {
                return applied.Error!;
            }
        }
        else
        {
            var newDate = request.ApprovedDueDate!.Value;
            if (task.DueDate is { } current && newDate <= current)
            {
                return new FieldErrors().Add("approvedDueDate", "The new due date must be later than the current one.").ToError();
            }

            task.ExtendDueDate(newDate, visible.UserId, now);
            task.RefreshOverdue(await DueDates.TodayForAssigneeAsync(db, task, now, cancellationToken));
            await notifier.NotifyAsync(
                new[] { request.RequestedBy }.Concat(task.AssigneeId is { } a ? [a] : []), NotificationType.ExtensionDecided,
                $"Deadline of {task.Key} extended", $"“{task.Title}” is now due {newDate:yyyy-MM-dd}.", Link(task), task.ProjectId, task.Id, cancellationToken);
        }

        Audit(request, task, "request.approved");
        await db.SaveChangesAsync(cancellationToken);
        return await ListForTaskAsync(task.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> RejectAsync(Guid requestId, DecideRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var guard = await LoadForDecisionAsync(requestId, cancellationToken);
        if (guard.IsFailure)
        {
            return guard.Error!;
        }

        var (request, visible) = guard.Value;
        var errors = new FieldErrors()
            .Required("comment", command.Comment, "A comment explaining the rejection")
            .MaxLength("comment", command.Comment?.Trim(), ApprovalRequest.CommentMaxLength, "The comment");
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var decided = request.Reject(visible.UserId, command.Comment!, timeProvider.GetUtcNow());
        if (decided.IsFailure)
        {
            return decided.Error!;
        }

        var task = visible.Task;
        await notifier.NotifyAsync(
            [request.RequestedBy],
            request.Type == ApprovalRequestType.Reassignment ? NotificationType.ReassignmentDecided : NotificationType.ExtensionDecided,
            $"{Describe(request.Type)} of {task.Key} was rejected", $"Your request on “{task.Title}” was rejected: {request.DecisionComment}",
            Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(request, task, "request.rejected");
        await db.SaveChangesAsync(cancellationToken);
        return await ListForTaskAsync(task.Id, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<ApprovalRequestDto>>> WithdrawAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var visible = request is null ? null : await access.LoadAsync(request.TaskId, track: false, cancellationToken);
        if (request is null || visible is null)
        {
            return CommonErrors.NotFound("Request");
        }

        var result = request.Withdraw(visible.UserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        Audit(request, visible.Task, "request.withdrawn");
        await db.SaveChangesAsync(cancellationToken);
        return await ListForTaskAsync(request.TaskId, cancellationToken);
    }

    /// <summary>Cancels a task's pending requests (edge case 26). The caller saves.</summary>
    public async Task AutoCancelPendingAsync(Guid taskId, string reason, CancellationToken cancellationToken)
    {
        var pending = await db.ApprovalRequests.Where(r => r.TaskId == taskId && r.Status == ApprovalRequestStatus.Pending).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var request in pending)
        {
            request.AutoCancel(reason, now);
        }
    }

    /// <summary>Cancels every pending request in a project that is being completed or archived (edge case 27). The caller saves.</summary>
    public async Task AutoCancelForProjectAsync(Guid projectId, string reason, CancellationToken cancellationToken)
    {
        var pending = await db.ApprovalRequests
            .Where(r => r.Status == ApprovalRequestStatus.Pending && db.Tasks.Any(t => t.Id == r.TaskId && t.ProjectId == projectId))
            .ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var request in pending)
        {
            request.AutoCancel(reason, now);
        }
    }

    private async Task<Result> ApplyReassignmentAsync(ApprovalRequest request, VisibleTask visible, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var task = visible.Task;
        var newAssignee = request.ProposedAssigneeId!.Value;
        if (!await projects.CanWorkOnTasksAsync(task.ProjectId, newAssignee, cancellationToken))
        {
            return Error.Conflict("request.assignee_unavailable", "The proposed assignee can no longer work on tasks in this project. Reject the request instead.");
        }

        var oldAssignee = task.AssigneeId;
        var result = task.Reassign(newAssignee, request.Reason, request.AlgorithmReapprovalRequired, visible.UserId, now);
        if (result.IsFailure)
        {
            return result;
        }

        // FR-REV-04: the old assignee's running session ends; a pending algorithm version is withdrawn.
        await work.CloseActiveSessionAsync(task.Id, SessionEndReason.Reassigned, now, cancellationToken);
        var pendingAlgorithm = await db.AlgorithmSubmissions.FirstOrDefaultAsync(a => a.TaskId == task.Id && a.Decision == AlgorithmDecision.Pending, cancellationToken);
        pendingAlgorithm?.WithdrawForReassignment(now);

        var link = Link(task);
        await notifier.NotifyAsync(
            [newAssignee], NotificationType.TaskReassigned, $"{task.Key} was reassigned to you",
            $"“{task.Title}” in {visible.Project.Name} is now yours. Due {task.DueDate:yyyy-MM-dd}.", link, task.ProjectId, task.Id, cancellationToken);
        var others = (await LeadersAsync(task.ProjectId, cancellationToken)).Append(request.RequestedBy)
            .Concat(oldAssignee is { } old ? [old] : [])
            .Where(id => id != newAssignee);
        await notifier.NotifyAsync(
            others, NotificationType.ReassignmentDecided, $"{task.Key} was reassigned",
            $"The reassignment of “{task.Title}” was approved.", link, task.ProjectId, task.Id, cancellationToken);
        return Result.Success();
    }

    private async Task<Result<IReadOnlyList<ApprovalRequestDto>>> AddAsync(
        VisibleTask visible, ApprovalRequest request, NotificationType type, string title, string body, CancellationToken cancellationToken)
    {
        if (await db.ApprovalRequests.AnyAsync(r => r.TaskId == request.TaskId && r.Type == request.Type && r.Status == ApprovalRequestStatus.Pending, cancellationToken))
        {
            return PendingExists;
        }

        var task = visible.Task;
        db.ApprovalRequests.Add(request);
        var approvers = await EligibleApproversAsync(task.ProjectId, request, cancellationToken);
        await notifier.NotifyAsync(approvers, type, title, body, Link(task), task.ProjectId, task.Id, cancellationToken);
        Audit(request, task, "request.created");
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            db.DiscardChanges();
            return PendingExists;
        }

        return await ListForTaskAsync(task.Id, cancellationToken);
    }

    /// <summary>404 if the task is not visible; 403 without the approve permission or when the approver rules forbid it.</summary>
    private async Task<Result<(ApprovalRequest Request, VisibleTask Visible)>> LoadForDecisionAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var request = await db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var visible = request is null ? null : await access.LoadAsync(request.TaskId, track: true, cancellationToken);
        if (request is null || visible is null)
        {
            return CommonErrors.NotFound("Request");
        }

        var check = Writable(visible);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (!request.IsPending)
        {
            return Error.Conflict("request.not_pending", $"This request is already {request.Status.ToString().ToLowerInvariant()}.");
        }

        var requesterRole = await ProjectRoleOfAsync(visible.Task.ProjectId, request.RequestedBy, cancellationToken);
        var denial = DecisionDenial(request, visible.Permissions, visible.UserId, requesterRole);
        return denial is null ? (request, visible) : denial;
    }

    /// <summary>Why the member may not decide the request, or null when they may (A6/Q7).</summary>
    private static Error? DecisionDenial(ApprovalRequest request, ProjectPermissions permissions, Guid userId, Guid? requesterRole)
    {
        var code = request.Type == ApprovalRequestType.Reassignment ? PermissionCatalog.ReassignmentsApprove : PermissionCatalog.DeadlinesApprove;
        var scope = permissions.Scope(code);
        if (scope is null or PermissionScope.Own)
        {
            return CommonErrors.Forbidden("decide this request");
        }

        if (request.RequestedBy == userId)
        {
            return Error.Forbidden("request.own_request", "You cannot decide on your own request.");
        }

        var leadRoles = new[] { SystemRoles.TeamLeaderId, SystemRoles.ProjectManagerId };
        if (scope != PermissionScope.Global && requesterRole is { } role && leadRoles.Contains(role) && permissions.ProjectRoleId == role)
        {
            return Error.Forbidden(
                "request.same_role",
                role == SystemRoles.TeamLeaderId
                    ? "A Team Leader's request is decided by a Project Manager (or an Admin)."
                    : "A Project Manager's request is decided by a Team Leader (or an Admin).");
        }

        return null;
    }

    /// <summary>TLs and PMs of the project who may decide the request (not the requester, not the requester's own lead role).</summary>
    private async Task<IReadOnlyList<Guid>> EligibleApproversAsync(Guid projectId, ApprovalRequest request, CancellationToken cancellationToken)
    {
        var requesterRole = await ProjectRoleOfAsync(projectId, request.RequestedBy, cancellationToken);
        var leads = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.RemovedAt == null
                && (m.ProjectRoleId == SystemRoles.TeamLeaderId || m.ProjectRoleId == SystemRoles.ProjectManagerId))
            .Select(m => new { m.UserId, m.ProjectRoleId })
            .ToListAsync(cancellationToken);
        return leads.Where(m => m.UserId != request.RequestedBy && m.ProjectRoleId != requesterRole).Select(m => m.UserId).ToList();
    }

    private Task<List<Guid>> LeadersAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.RemovedAt == null
                && (m.ProjectRoleId == SystemRoles.TeamLeaderId || m.ProjectRoleId == SystemRoles.ProjectManagerId))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

    private Task<Guid?> ProjectRoleOfAsync(Guid projectId, Guid userId, CancellationToken cancellationToken) =>
        db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.UserId == userId && m.RemovedAt == null)
            .Select(m => (Guid?)m.ProjectRoleId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<IReadOnlyList<ApprovalRequestDto>> ToDtosAsync(IReadOnlyList<ApprovalRequest> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var taskIds = rows.Select(r => r.TaskId).Distinct().ToList();
        var tasks = await db.Tasks.AsNoTracking().Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Key, t.Title, t.ProjectId })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var userIds = rows.SelectMany(r => new[] { (Guid?)r.RequestedBy, r.ProposedAssigneeId, r.DecidedBy }).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        UserRef? Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;

        var permissionsByProject = new Dictionary<Guid, ProjectPermissions>();
        var result = new List<ApprovalRequestDto>(rows.Count);
        var me = access.CurrentUserId;
        foreach (var r in rows)
        {
            var task = tasks[r.TaskId];
            if (!permissionsByProject.TryGetValue(task.ProjectId, out var permissionsHere))
            {
                permissionsHere = await projects.ForProjectAsync(task.ProjectId, cancellationToken);
                permissionsByProject[task.ProjectId] = permissionsHere;
            }

            var canDecide = r.IsPending && DecisionDenial(r, permissionsHere, me, await ProjectRoleOfAsync(task.ProjectId, r.RequestedBy, cancellationToken)) is null;
            result.Add(new ApprovalRequestDto(
                r.Id, r.TaskId, task.Key, task.Title, task.ProjectId, r.Type, r.Status, r.Reason, Name(r.RequestedBy), r.RequestedAt,
                Name(r.ProposedAssigneeId), r.AlgorithmReapprovalRequired, r.CurrentDueDate, r.ProposedDueDate, r.ApprovedDueDate,
                Name(r.DecidedBy), r.DecidedAt, r.DecisionComment, canDecide, r.IsPending && r.RequestedBy == me));
        }

        return result;
    }

    private static FieldErrors ValidateReason(string? reason)
    {
        var errors = new FieldErrors()
            .Required("reason", reason, "A reason")
            .MaxLength("reason", reason?.Trim(), ApprovalRequest.ReasonMaxLength, "The reason");
        if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length < ApprovalRequest.ReasonMinLength)
        {
            errors.Add("reason", $"Explain the reason in at least {ApprovalRequest.ReasonMinLength} characters.");
        }

        return errors;
    }

    private void ValidateNewDueDate(FieldErrors errors, string field, DateOnly? date, DateOnly currentDue)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (date is not { } value)
        {
            errors.Add(field, "Choose the new due date.");
        }
        else if (value < today)
        {
            errors.Add(field, "The new due date cannot be in the past.");
        }
        else if (value <= currentDue)
        {
            errors.Add(field, $"The new due date must be later than the current one ({currentDue:yyyy-MM-dd}).");
        }
    }

    private static Result Writable(VisibleTask visible)
    {
        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        return TaskWorkflow.IsFinal(visible.Task.Status)
            ? Error.Conflict("task.read_only", $"Task {visible.Task.Key} is {TaskWorkflow.Describe(visible.Task.Status).ToLowerInvariant()}.")
            : Result.Success();
    }

    private void Audit(ApprovalRequest request, TaskItem task, string action) =>
        auditLog.Record(action, "ApprovalRequest", request.Id.ToString(), new AuditChanges()
            .Set("taskId", task.Id)
            .Set("type", request.Type)
            .Set("status", request.Status)
            .Set("proposedAssigneeId", request.ProposedAssigneeId)
            .Set("proposedDueDate", request.ProposedDueDate)
            .Set("approvedDueDate", request.ApprovedDueDate)
            .Set("comment", request.DecisionComment), task.ProjectId);

    private static string Describe(ApprovalRequestType type) => type == ApprovalRequestType.Reassignment ? "Reassignment" : "Deadline extension";

    private static string Link(TaskItem task) => $"/tasks/{task.Id}";
}

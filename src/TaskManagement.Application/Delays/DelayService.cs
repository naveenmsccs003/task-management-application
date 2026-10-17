using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Tasks;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Notifications;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Delays;

public sealed record DelayReportDto(Guid Id, int Revision, DelayCategory Category, string Description, UserRef? ReportedBy, DateTimeOffset ReportedAt);

public sealed record ReportDelayCommand(DelayCategory? Category, string? Description);

/// <summary>Delay reports (FR-DL-06): the assignee explains a delay on an open task; TLs and PMs are told.</summary>
public sealed class DelayService(IAppDbContext db, TaskAccess access, Notifier notifier, IAuditLog auditLog, TimeProvider timeProvider)
{
    public async Task<Result<IReadOnlyList<DelayReportDto>>> ListAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.Allows(PermissionCatalog.DelaysView))
        {
            return CommonErrors.Forbidden("view this task's delay reports");
        }

        var reports = await db.DelayReports.AsNoTracking().Where(d => d.TaskId == taskId).OrderByDescending(d => d.ReportedAt).ToListAsync(cancellationToken);
        var ids = reports.Select(r => r.ReportedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new UserRef(u.Id, u.FullName), cancellationToken);
        IReadOnlyList<DelayReportDto> result = reports
            .Select(r => new DelayReportDto(r.Id, r.Revision, r.Category, r.Description, names.GetValueOrDefault(r.ReportedBy), r.ReportedAt))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<IReadOnlyList<DelayReportDto>>> ReportAsync(Guid taskId, ReportDelayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var visible = await access.LoadAsync(taskId, track: false, cancellationToken);
        if (visible is null)
        {
            return CommonErrors.NotFound("Task");
        }

        if (!visible.IsAssignee || !visible.Allows(PermissionCatalog.DelaysReport))
        {
            return Error.Forbidden("delay.not_assignee", "Only the task's assignee can report a delay.");
        }

        var task = visible.Task;
        if (visible.Project.IsReadOnly)
        {
            return Project.ReadOnlyError();
        }

        if (!TaskWorkflow.IsOpen(task.Status))
        {
            return Error.Conflict("delay.task_not_open", $"Task {task.Key} is no longer open.");
        }

        var errors = new FieldErrors()
            .Required("description", command.Description, "A description")
            .MaxLength("description", command.Description?.Trim(), DelayReport.DescriptionMaxLength, "The description");
        if (command.Category is not { } category || !Enum.IsDefined(category))
        {
            errors.Add("category", "Choose a category.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var report = DelayReport.Create(task.Id, task.CurrentRevision, command.Category!.Value, command.Description!, visible.UserId, timeProvider.GetUtcNow());
        db.DelayReports.Add(report);
        var leaders = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.ProjectId == task.ProjectId && m.RemovedAt == null
                && (m.ProjectRoleId == SystemRoles.TeamLeaderId || m.ProjectRoleId == SystemRoles.ProjectManagerId))
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        await notifier.NotifyAsync(
            leaders, NotificationType.DelayReported, $"Delay reported on {task.Key}",
            $"{report.Category}: {report.Description}", $"/tasks/{task.Id}", task.ProjectId, task.Id, cancellationToken);
        auditLog.Record("task.delay_reported", "DelayReport", report.Id.ToString(), new AuditChanges()
            .Set("taskId", task.Id)
            .Set("category", report.Category), task.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return await ListAsync(taskId, cancellationToken);
    }
}

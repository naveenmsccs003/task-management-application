using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Projects;

/// <summary>Creating, editing and changing the status of projects (FR-PROJ-01, FR-PROJ-04).</summary>
public sealed class ProjectCommands(
    IAppDbContext db,
    ICurrentUser currentUser,
    ProjectAccess access,
    ProjectQueries queries,
    PermissionService permissions,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Only callers with a Global projects.create grant reach this (checked by the endpoint). A creator who cannot see
    /// every project (e.g. a PM) becomes the project's PM member, so they can manage what they created.
    /// </summary>
    public async Task<Result<ProjectDto>> CreateAsync(SaveProjectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var key = command.Key?.Trim().ToUpperInvariant();
        var errors = Validate(command);
        if (!Project.IsValidKey(key))
        {
            errors.Add("key", $"Use {Project.KeyMinLength}–{Project.KeyMaxLength} letters or digits, starting with a letter (e.g. TMA).");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        if (await db.Projects.AnyAsync(p => p.Key == key, cancellationToken))
        {
            return Error.Conflict("project.key_taken", $"The key {key} is already used by another project.");
        }

        var userId = currentUser.RequiredUserId;
        var now = timeProvider.GetUtcNow();
        var project = Project.Create(
            key!, command.Name!, command.Description, command.StartDate, command.TargetEndDate,
            command.AlgorithmRequiredDefault, command.CloseOnApproval, userId, now);
        db.Projects.Add(project);

        var global = await permissions.GetAsync(userId, cancellationToken);
        var creatorJoins = !global.Has(PermissionCatalog.ProjectsView, PermissionScope.Global);
        if (creatorJoins)
        {
            db.ProjectMembers.Add(ProjectMember.Create(project.Id, userId, SystemRoles.ProjectManagerId, userId, now));
        }

        auditLog.Record("project.created", "Project", project.Id.ToString(), new AuditChanges()
            .Set("key", project.Key)
            .Set("name", project.Name)
            .Set("startDate", project.StartDate)
            .Set("targetEndDate", project.TargetEndDate)
            .Set("algorithmRequiredDefault", project.AlgorithmRequiredDefault)
            .Set("closeOnApproval", project.CloseOnApproval)
            .Set("creatorAddedAsProjectManager", creatorJoins), project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(project.Id, cancellationToken);
    }

    public async Task<Result<ProjectDto>> UpdateAsync(Guid id, SaveProjectCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await access.LoadVisibleAsync(id, track: true, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.ProjectsUpdate))
        {
            return CommonErrors.Forbidden("edit this project");
        }

        var errors = Validate(command);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var project = found.Project;
        db.ExpectVersion(project, command.Version);
        var changes = new AuditChanges()
            .Change("name", project.Name, command.Name!.Trim())
            .Change("description", project.Description, command.Description?.Trim() ?? string.Empty)
            .Change("startDate", project.StartDate, command.StartDate)
            .Change("targetEndDate", project.TargetEndDate, command.TargetEndDate)
            .Change("algorithmRequiredDefault", project.AlgorithmRequiredDefault, command.AlgorithmRequiredDefault)
            .Change("closeOnApproval", project.CloseOnApproval, command.CloseOnApproval);
        var result = project.UpdateDetails(
            command.Name, command.Description, command.StartDate, command.TargetEndDate,
            command.AlgorithmRequiredDefault, command.CloseOnApproval, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        if (!changes.IsEmpty)
        {
            auditLog.Record("project.updated", "Project", project.Id.ToString(), changes, project.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(project.Id, cancellationToken);
    }

    /// <summary>
    /// Hold, complete, archive or reactivate. Completing or archiving with open tasks needs confirmation; the tasks stay
    /// as they are but become read-only (FR-PROJ-04).
    /// </summary>
    public async Task<Result<ProjectDto>> ChangeStatusAsync(
        Guid id, ProjectStatus target, ProjectStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await access.LoadVisibleAsync(id, track: true, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.ProjectsUpdate))
        {
            return CommonErrors.Forbidden("change this project's status");
        }

        var project = found.Project;
        if (target is ProjectStatus.Completed or ProjectStatus.Archived && !command.ConfirmOpenTasks)
        {
            var openTasks = await db.Tasks.CountAsync(
                t => t.ProjectId == id
                    && t.Status != TaskItemStatus.Approved && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled,
                cancellationToken);
            if (openTasks > 0)
            {
                return Error.Conflict(
                    "project.has_open_tasks",
                    $"This project has {openTasks} open task{(openTasks == 1 ? string.Empty : "s")}. They will stay as they are but become read-only. Confirm to continue.");
            }
        }

        var previous = project.Status;
        var result = project.ChangeStatus(target, currentUser.RequiredUserId, timeProvider.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        auditLog.Record("project.status_changed", "Project", project.Id.ToString(), new AuditChanges().Change("status", previous, target), project.Id);
        await db.SaveChangesAsync(cancellationToken);
        return await queries.GetAsync(project.Id, cancellationToken);
    }

    private static FieldErrors Validate(SaveProjectCommand command)
    {
        var errors = new FieldErrors()
            .Required("name", command.Name, "Name")
            .MaxLength("name", command.Name?.Trim(), Project.NameMaxLength, "Name")
            .MaxLength("description", command.Description, Project.DescriptionMaxLength, "Description");
        if (command.StartDate is { } start && command.TargetEndDate is { } end && end < start)
        {
            errors.Add("targetEndDate", "The target end date must be on or after the start date.");
        }

        return errors;
    }
}

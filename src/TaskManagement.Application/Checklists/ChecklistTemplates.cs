using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Checklists;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Projects;

namespace TaskManagement.Application.Checklists;

public sealed record ChecklistItemDto(Guid Id, int SortOrder, string Text, bool IsMandatory);

public sealed record ChecklistTemplateDto(
    Guid Id, Guid? ProjectId, string Name, bool IsActive, IReadOnlyList<ChecklistItemDto> Items, bool CanEdit, DateTimeOffset UpdatedAt, uint Version);

public sealed record ChecklistItemInput(string? Text, bool IsMandatory);

/// <summary>Create (with <see cref="ProjectId"/> for a project template, null for global) or update a template.</summary>
public sealed record SaveChecklistTemplateCommand(Guid? ProjectId, string? Name, bool IsActive, IReadOnlyList<ChecklistItemInput>? Items, uint Version);

/// <summary>
/// Checklist templates (FR-CHK-01): global ones need checklists.manage (Global); project ones need
/// checklists.manage-project in that project. Templates are never deleted — deactivate them instead.
/// </summary>
public sealed class ChecklistTemplates(
    IAppDbContext db, ICurrentUser currentUser, PermissionService permissions, ProjectAccess projects, IAuditLog auditLog, TimeProvider timeProvider)
{
    /// <summary>
    /// Without <paramref name="projectId"/>: global templates, for members who manage them. With it: the active global
    /// templates plus the project's own, for members who can see the project (to pick one for a task or as its default).
    /// </summary>
    public async Task<Result<IReadOnlyList<ChecklistTemplateDto>>> ListAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        var canManageGlobal = await CanManageGlobalAsync(cancellationToken);
        IQueryable<ChecklistTemplate> query;
        var canManageProject = false;
        if (projectId is { } id)
        {
            var loaded = await projects.LoadVisibleAsync(id, track: false, cancellationToken);
            if (loaded is not { } found)
            {
                return CommonErrors.NotFound("Project");
            }

            canManageProject = found.Permissions.HasProjectWide(PermissionCatalog.ChecklistsManageProject);
            query = db.ChecklistTemplates.Where(t => t.ProjectId == id || (t.ProjectId == null && t.IsActive));
        }
        else
        {
            if (!canManageGlobal)
            {
                return CommonErrors.Forbidden("manage global checklist templates");
            }

            query = db.ChecklistTemplates.Where(t => t.ProjectId == null);
        }

        var templates = await query.AsNoTracking().Include(t => t.Items).OrderBy(t => t.ProjectId == null).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        IReadOnlyList<ChecklistTemplateDto> result = templates
            .Select(t => ToDto(t, t.ProjectId is null ? canManageGlobal : canManageProject))
            .ToList();
        return Result.Success(result);
    }

    public async Task<Result<ChecklistTemplateDto>> CreateAsync(SaveChecklistTemplateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var allowed = await AuthorizeAsync(command.ProjectId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var errors = Validate(command);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var template = ChecklistTemplate.Create(command.ProjectId, command.Name!, ToDrafts(command), currentUser.RequiredUserId, timeProvider.GetUtcNow());
        db.ChecklistTemplates.Add(template);
        auditLog.Record("checklist_template.created", "ChecklistTemplate", template.Id.ToString(), new AuditChanges()
            .Set("name", template.Name)
            .Set("items", template.Items.Count), template.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(template, true);
    }

    public async Task<Result<ChecklistTemplateDto>> UpdateAsync(Guid id, SaveChecklistTemplateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var template = await db.ChecklistTemplates.Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return CommonErrors.NotFound("Checklist_template");
        }

        var allowed = await AuthorizeAsync(template.ProjectId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error!;
        }

        var errors = Validate(command);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        db.ExpectVersion(template, command.Version);
        var changes = new AuditChanges()
            .Change("name", template.Name, command.Name!.Trim())
            .Change("isActive", template.IsActive, command.IsActive)
            .Change("items", template.Items.Count, command.Items?.Count ?? 0);
        template.Update(command.Name, command.IsActive, ToDrafts(command), currentUser.RequiredUserId, timeProvider.GetUtcNow());
        auditLog.Record("checklist_template.updated", "ChecklistTemplate", template.Id.ToString(), changes, template.ProjectId);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(template, true);
    }

    /// <summary>A template a new task in <paramref name="projectId"/> may use: active, and global or the project's own.</summary>
    public Task<ChecklistTemplate?> FindUsableAsync(Guid templateId, Guid projectId, CancellationToken cancellationToken) =>
        db.ChecklistTemplates.AsNoTracking()
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == templateId && t.IsActive && (t.ProjectId == null || t.ProjectId == projectId), cancellationToken);

    private async Task<Result> AuthorizeAsync(Guid? projectId, CancellationToken cancellationToken)
    {
        if (projectId is not { } id)
        {
            return await CanManageGlobalAsync(cancellationToken) ? Result.Success() : CommonErrors.Forbidden("manage global checklist templates");
        }

        var loaded = await projects.LoadVisibleAsync(id, track: false, cancellationToken);
        if (loaded is not { } found)
        {
            return CommonErrors.NotFound("Project");
        }

        if (!found.Permissions.HasProjectWide(PermissionCatalog.ChecklistsManageProject))
        {
            return CommonErrors.Forbidden("manage this project's checklist templates");
        }

        return found.Project.IsReadOnly ? Project.ReadOnlyError() : Result.Success();
    }

    private async Task<bool> CanManageGlobalAsync(CancellationToken cancellationToken) =>
        (await permissions.GetAsync(currentUser.RequiredUserId, cancellationToken)).Has(PermissionCatalog.ChecklistsManage, PermissionScope.Global);

    private static FieldErrors Validate(SaveChecklistTemplateCommand command)
    {
        var errors = new FieldErrors()
            .Required("name", command.Name, "Name")
            .MaxLength("name", command.Name?.Trim(), ChecklistTemplate.NameMaxLength, "Name");
        var items = command.Items ?? [];
        if (items.Count == 0)
        {
            errors.Add("items", "Add at least one item.");
        }
        else if (items.Count > ChecklistTemplate.MaxItems)
        {
            errors.Add("items", $"Use at most {ChecklistTemplate.MaxItems} items.");
        }

        if (items.Any(i => string.IsNullOrWhiteSpace(i.Text)))
        {
            errors.Add("items", "Every item needs text.");
        }

        if (items.Any(i => i.Text?.Trim().Length > ChecklistTemplate.ItemTextMaxLength))
        {
            errors.Add("items", $"Each item must be at most {ChecklistTemplate.ItemTextMaxLength} characters.");
        }

        return errors;
    }

    private static List<ChecklistItemDraft> ToDrafts(SaveChecklistTemplateCommand command) =>
        (command.Items ?? []).Select(i => new ChecklistItemDraft(i.Text!, i.IsMandatory)).ToList();

    private static ChecklistTemplateDto ToDto(ChecklistTemplate template, bool canEdit) =>
        new(
            template.Id,
            template.ProjectId,
            template.Name,
            template.IsActive,
            template.Items.OrderBy(i => i.SortOrder).Select(i => new ChecklistItemDto(i.Id, i.SortOrder, i.Text, i.IsMandatory)).ToList(),
            canEdit,
            template.UpdatedAt,
            template.Version);
}

using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Domain.Projects;

namespace TaskManagement.Application.Projects;

public sealed record ProjectDto(
    Guid Id,
    string Key,
    string Name,
    string Description,
    ProjectStatus Status,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    bool AlgorithmRequiredDefault,
    bool CloseOnApproval,
    Guid? DefaultChecklistTemplateId,
    int MemberCount,
    int OpenTaskCount,
    RoleRef? MyRole,
    IReadOnlyList<PermissionGrantDto> MyPermissions,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record ProjectListItemDto(
    Guid Id,
    string Key,
    string Name,
    ProjectStatus Status,
    DateOnly? TargetEndDate,
    int MemberCount,
    int OpenTaskCount,
    RoleRef? MyRole);

public sealed record ProjectListQuery(string? Search, ProjectStatus? Status, PageRequest Page);

/// <summary>Create and update share this shape. <see cref="Key"/> is used only on create; <see cref="Version"/> only on update.</summary>
public sealed record SaveProjectCommand(
    string? Key,
    string? Name,
    string? Description,
    DateOnly? StartDate,
    DateOnly? TargetEndDate,
    bool AlgorithmRequiredDefault,
    bool CloseOnApproval,
    uint Version,
    Guid? DefaultChecklistTemplateId = null);

/// <summary>Completing or archiving a project with open tasks needs <see cref="ConfirmOpenTasks"/> (FR-PROJ-04).</summary>
public sealed record ProjectStatusCommand(bool ConfirmOpenTasks);

public sealed record ProjectMemberDto(
    Guid UserId,
    string FullName,
    string Email,
    bool IsActive,
    RoleRef ProjectRole,
    bool CanWorkOnTasks,
    int OpenTaskCount,
    DateTimeOffset AddedAt);

public sealed record AddProjectMemberCommand(Guid UserId, Guid ProjectRoleId);

public sealed record ChangeProjectMemberCommand(Guid ProjectRoleId);

/// <summary>An active account that can be added to a project (minimal fields; full member details need members.view).</summary>
public sealed record MemberCandidateDto(Guid Id, string FullName, string Email);

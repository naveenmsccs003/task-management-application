using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;

namespace TaskManagement.Application.Members;

public sealed record MemberDto(
    Guid Id,
    string Email,
    string FullName,
    string TimeZone,
    string? EmployeeCode,
    RoleRef GlobalRole,
    bool IsActive,
    bool HasPassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignInAt,
    DateTimeOffset? DeactivatedAt,
    uint Version);

public sealed record MemberListQuery(string? Search, bool? IsActive, Guid? RoleId, PageRequest Page);

public sealed record CreateMemberCommand(string? Email, string? FullName, Guid GlobalRoleId, string? TimeZone, string? EmployeeCode);

public sealed record UpdateMemberCommand(string? FullName, string? TimeZone, string? EmployeeCode, Guid GlobalRoleId, uint Version);

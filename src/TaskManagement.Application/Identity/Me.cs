using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Identity;

public sealed record RoleRef(Guid Id, string Name);

public sealed record PermissionGrantDto(string Code, PermissionScope Scope);

public sealed record MeDto(
    Guid Id,
    string Email,
    string FullName,
    string TimeZone,
    RoleRef GlobalRole,
    IReadOnlyList<PermissionGrantDto> Permissions);

public sealed record UpdateMyProfileCommand(string? FullName, string? TimeZone);

/// <summary>The signed-in member's profile and effective permissions (used by Angular for display only).</summary>
public sealed class MeHandler(IAppDbContext db, ICurrentUser currentUser, PermissionService permissions, IAuditLog auditLog)
{
    public async Task<Result<MeDto>> GetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var me = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.FullName,
                u.TimeZone,
                Role = db.Roles.Where(r => r.Id == u.GlobalRoleId).Select(r => new RoleRef(r.Id, r.Name)).First(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (me is null)
        {
            return CommonErrors.NotFound("User");
        }

        var effective = await permissions.GetAsync(userId, cancellationToken);
        var grants = effective.Grants
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new PermissionGrantDto(g.Key, g.Value))
            .ToList();
        return new MeDto(me.Id, me.Email, me.FullName, me.TimeZone, me.Role, grants);
    }

    public async Task<Result<MeDto>> UpdateProfileAsync(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = new FieldErrors()
            .Required("fullName", command.FullName, "Full name")
            .MaxLength("fullName", command.FullName, Domain.Identity.User.FullNameMaxLength, "Full name");
        if (!InputRules.IsValidTimeZone(command.TimeZone))
        {
            errors.Add("timeZone", "Choose a valid time zone, for example Asia/Kolkata.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var user = await db.Users.FindAsync([currentUser.RequiredUserId], cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("User");
        }

        var changes = new AuditChanges()
            .Change("fullName", user.FullName, command.FullName!.Trim())
            .Change("timeZone", user.TimeZone, command.TimeZone);
        user.UpdateProfile(command.FullName, command.TimeZone!);
        if (!changes.IsEmpty)
        {
            auditLog.Record("member.profile_updated", "User", user.Id.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(cancellationToken);
    }
}

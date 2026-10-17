using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Identity;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Time;

namespace TaskManagement.Application.Members;

/// <summary>Member management (FR-MEM-01..06). Only callers with the members.* permissions reach these methods.</summary>
public sealed class MemberCommands(
    IAppDbContext db,
    ICurrentUser currentUser,
    MemberQueries queries,
    PasswordTokenIssuer tokenIssuer,
    AccountEmails emails,
    SettingsService settingsService,
    SessionValidator sessions,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    private static readonly Error LastAdmin =
        Error.Conflict("member.last_admin", "This is the last active Admin. Make another member an Admin first.");

    private static readonly Error SelfChange =
        Error.Conflict("member.self_change", "You cannot change your own role or deactivate yourself.");

    public async Task<Result<MemberDto>> CreateAsync(CreateMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = ValidateProfile(command.FullName, command.TimeZone, command.EmployeeCode);
        if (!InputRules.IsValidEmail(command.Email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (!await db.Roles.AnyAsync(r => r.Id == command.GlobalRoleId, cancellationToken))
        {
            errors.Add("globalRoleId", "Choose an existing role.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var normalizedEmail = User.NormalizeEmail(command.Email!);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken))
        {
            return Error.Conflict("member.email_taken", "A member with this email already exists.");
        }

        if (await EmployeeCodeTakenAsync(command.EmployeeCode, null, cancellationToken))
        {
            return Error.Conflict("member.employee_code_taken", "Another member already has this employee code.");
        }

        var now = timeProvider.GetUtcNow();
        var user = User.Create(command.Email!, command.FullName!, command.TimeZone!, command.EmployeeCode, command.GlobalRoleId, currentUser.UserId, now);
        db.Users.Add(user);

        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var lifetime = TimeSpan.FromHours(settings.InviteTokenHours);
        var token = await tokenIssuer.IssueAsync(user, PasswordTokenPurpose.Invite, lifetime, cancellationToken);
        emails.QueueInvite(user, token, lifetime);

        auditLog.Record("member.created", "User", user.Id.ToString(), new AuditChanges()
            .Set("email", user.Email)
            .Set("fullName", user.FullName)
            .Set("globalRoleId", user.GlobalRoleId)
            .Set("timeZone", user.TimeZone)
            .Set("employeeCode", user.EmployeeCode));
        await db.SaveChangesAsync(cancellationToken);
        PermissionService.InvalidateAll();
        return await queries.GetAsync(user.Id, cancellationToken);
    }

    public async Task<Result<MemberDto>> UpdateAsync(Guid id, UpdateMemberCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = ValidateProfile(command.FullName, command.TimeZone, command.EmployeeCode);
        if (!await db.Roles.AnyAsync(r => r.Id == command.GlobalRoleId, cancellationToken))
        {
            errors.Add("globalRoleId", "Choose an existing role.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("Member");
        }

        db.ExpectVersion(user, command.Version);

        var roleChanges = user.GlobalRoleId != command.GlobalRoleId;
        if (roleChanges && user.Id == currentUser.UserId)
        {
            return SelfChange;
        }

        if (roleChanges && user.GlobalRoleId == SystemRoles.AdminId && user.IsActive && !await AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
        {
            return LastAdmin;
        }

        if (await EmployeeCodeTakenAsync(command.EmployeeCode, user.Id, cancellationToken))
        {
            return Error.Conflict("member.employee_code_taken", "Another member already has this employee code.");
        }

        var changes = new AuditChanges()
            .Change("fullName", user.FullName, command.FullName!.Trim())
            .Change("timeZone", user.TimeZone, command.TimeZone)
            .Change("employeeCode", user.EmployeeCode, string.IsNullOrWhiteSpace(command.EmployeeCode) ? null : command.EmployeeCode.Trim())
            .Change("globalRoleId", user.GlobalRoleId, command.GlobalRoleId);

        user.UpdateProfile(command.FullName, command.TimeZone!);
        user.SetEmployeeCode(command.EmployeeCode);
        user.ChangeGlobalRole(command.GlobalRoleId);

        if (!changes.IsEmpty)
        {
            auditLog.Record(roleChanges ? "member.role_changed" : "member.updated", "User", user.Id.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (roleChanges)
        {
            sessions.Evict(user.Id);
            PermissionService.InvalidateAll();
        }

        return await queries.GetAsync(user.Id, cancellationToken);
    }

    public async Task<Result<MemberDto>> DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("Member");
        }

        if (user.Id == currentUser.UserId)
        {
            return SelfChange;
        }

        if (!user.IsActive)
        {
            return await queries.GetAsync(user.Id, cancellationToken);
        }

        if (user.GlobalRoleId == SystemRoles.AdminId && !await AnotherActiveAdminExistsAsync(user.Id, cancellationToken))
        {
            return LastAdmin;
        }

        var now = timeProvider.GetUtcNow();
        user.Deactivate(currentUser.RequiredUserId, now);
        await tokenIssuer.RevokeAllAsync(user.Id, now, cancellationToken);

        // Edge case 3: a running work session ends and its task stops; it then needs reassigning.
        var session = await db.WorkSessions.FirstOrDefaultAsync(s => s.UserId == user.Id && s.EndedAt == null, cancellationToken);
        if (session is not null)
        {
            session.End(now, SessionEndReason.MemberDeactivated);
            var task = await db.Tasks.FirstAsync(t => t.Id == session.TaskId, cancellationToken);
            task.StopBySystem("The assignee's account was deactivated.", now);
        }

        auditLog.Record("member.deactivated", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        sessions.Evict(user.Id);
        PermissionService.InvalidateAll();
        return await queries.GetAsync(user.Id, cancellationToken);
    }

    public async Task<Result<MemberDto>> ReactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("Member");
        }

        if (!user.IsActive)
        {
            user.Reactivate();
            auditLog.Record("member.reactivated", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
            sessions.Evict(user.Id);
            PermissionService.InvalidateAll();
        }

        return await queries.GetAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Admin-initiated reset: ends the member's sessions and emails a reset link (or a fresh invite link if they
    /// never set a password). The admin never sees the link (FR-AUTH-09).
    /// </summary>
    public async Task<Result> SendPasswordResetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return CommonErrors.NotFound("Member");
        }

        if (!user.IsActive)
        {
            return Error.Conflict("member.inactive", "Reactivate the member before sending a password link.");
        }

        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var isInvite = user.PasswordHash is null;
        var lifetime = isInvite ? TimeSpan.FromHours(settings.InviteTokenHours) : TimeSpan.FromMinutes(settings.ResetTokenMinutes);
        var token = await tokenIssuer.IssueAsync(user, isInvite ? PasswordTokenPurpose.Invite : PasswordTokenPurpose.Reset, lifetime, cancellationToken);
        if (isInvite)
        {
            emails.QueueInvite(user, token, lifetime);
        }
        else
        {
            emails.QueuePasswordReset(user, token, lifetime);
        }

        user.RevokeSessions();
        auditLog.Record(isInvite ? "member.invite_resent" : "member.password_reset_sent", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        sessions.Evict(user.Id);
        return Result.Success();
    }

    private static FieldErrors ValidateProfile(string? fullName, string? timeZone, string? employeeCode)
    {
        var errors = new FieldErrors()
            .Required("fullName", fullName, "Full name")
            .MaxLength("fullName", fullName, User.FullNameMaxLength, "Full name")
            .MaxLength("employeeCode", employeeCode, User.EmployeeCodeMaxLength, "Employee code");
        if (!InputRules.IsValidTimeZone(timeZone))
        {
            errors.Add("timeZone", "Choose a valid time zone, for example Asia/Kolkata.");
        }

        return errors;
    }

    private Task<bool> AnotherActiveAdminExistsAsync(Guid exceptUserId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id != exceptUserId && u.IsActive && u.GlobalRoleId == SystemRoles.AdminId, cancellationToken);

    private Task<bool> EmployeeCodeTakenAsync(string? employeeCode, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return Task.FromResult(false);
        }

        var code = employeeCode.Trim();
        return db.Users.AnyAsync(u => u.EmployeeCode == code && u.Id != exceptUserId, cancellationToken);
    }
}

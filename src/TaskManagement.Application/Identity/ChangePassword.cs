using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Identity;

public sealed record ChangePasswordCommand(string? CurrentPassword, string? NewPassword);

/// <summary>FR-AUTH-10. Returns the new security stamp so the API can keep the current session while ending all others.</summary>
public sealed class ChangePasswordHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPasswordHasher passwordHasher,
    PasswordTokenIssuer tokenIssuer,
    SettingsService settingsService,
    SessionValidator sessions,
    AccountEmails emails,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<SignedInUser>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await db.Users.FindAsync([currentUser.RequiredUserId], cancellationToken);
        if (user is null || !user.IsActive || user.PasswordHash is null)
        {
            return CommonErrors.NotFound("User");
        }

        if (string.IsNullOrEmpty(command.CurrentPassword)
            || passwordHasher.Verify(user.PasswordHash, command.CurrentPassword) == PasswordCheck.Failed)
        {
            return new FieldErrors().Add("currentPassword", "Current password is incorrect.").ToError();
        }

        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var errors = PasswordPolicy.Validate(command.NewPassword, user.Email, settings.PasswordMinLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        var now = timeProvider.GetUtcNow();
        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword!), now);
        await tokenIssuer.RevokeAllAsync(user.Id, now, cancellationToken);
        auditLog.Record("auth.password_changed", "User", user.Id.ToString());
        emails.QueuePasswordChanged(user);
        await db.SaveChangesAsync(cancellationToken);
        sessions.Evict(user.Id);
        return new SignedInUser(user.Id, user.SecurityStamp);
    }
}

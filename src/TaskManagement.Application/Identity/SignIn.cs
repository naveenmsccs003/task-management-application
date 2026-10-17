using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Application.Identity;

public sealed record SignInCommand(string? Email, string? Password);

/// <summary>What the API needs to issue the session cookie.</summary>
public sealed record SignedInUser(Guid UserId, string SecurityStamp);

public sealed class SignInHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    ISignInAttemptStore attempts,
    SettingsService settingsService,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result<SignedInUser>> HandleAsync(SignInCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrEmpty(command.Password))
        {
            return new FieldErrors()
                .Required("email", command.Email, "Email")
                .Required("password", command.Password, "Password")
                .ToError();
        }

        var now = timeProvider.GetUtcNow();
        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var normalizedEmail = User.NormalizeEmail(command.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        if (user is null || user.PasswordHash is null || !user.IsActive)
        {
            passwordHasher.VerifyAgainstDummy(command.Password);
            var reason = user is null ? "unknown_account" : !user.IsActive ? "inactive" : "no_password_set";
            return await FailAsync(user?.Id, reason, cancellationToken);
        }

        if (user.IsLockedOut(now))
        {
            passwordHasher.VerifyAgainstDummy(command.Password);
            return await FailAsync(user.Id, "locked_out", cancellationToken);
        }

        var check = passwordHasher.Verify(user.PasswordHash, command.Password);
        if (check == PasswordCheck.Failed)
        {
            var lockedUntil = await attempts.RecordFailureAsync(
                user.Id, settings.MaxFailedSignInAttempts, now.AddMinutes(settings.LockoutMinutes), cancellationToken);
            if (lockedUntil is not null)
            {
                auditLog.Record("auth.locked_out", "User", user.Id.ToString(), new AuditChanges().Set("lockedUntil", lockedUntil));
            }

            return await FailAsync(user.Id, "wrong_password", cancellationToken);
        }

        if (check == PasswordCheck.SuccessRehashNeeded)
        {
            // Saved before the atomic success update below, which changes the row version.
            user.RehashPassword(passwordHasher.Hash(command.Password));
            await db.SaveChangesAsync(cancellationToken);
        }

        await attempts.RecordSuccessAsync(user.Id, now, cancellationToken);
        auditLog.Record("auth.signed_in", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return new SignedInUser(user.Id, user.SecurityStamp);
    }

    private async Task<Result<SignedInUser>> FailAsync(Guid? userId, string reason, CancellationToken cancellationToken)
    {
        // The attempted email is not stored for unknown accounts: it may be personal data of someone else.
        auditLog.Record("auth.sign_in_failed", "User", userId?.ToString() ?? "unknown", new AuditChanges().Set("reason", reason));
        await db.SaveChangesAsync(cancellationToken);
        return IdentityErrors.InvalidCredentials;
    }
}

using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Common;

namespace TaskManagement.Application.Identity;

public sealed record ResetPasswordCommand(string? Token, string? NewPassword);

/// <summary>Completes a reset or invite link: single use, expiring, signs out every other session (FR-AUTH-07/08/09).</summary>
public sealed class ResetPasswordHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    PasswordTokenIssuer tokenIssuer,
    SettingsService settingsService,
    SessionValidator sessions,
    IAuditLog auditLog,
    TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > 100)
        {
            return IdentityErrors.InvalidToken;
        }

        var now = timeProvider.GetUtcNow();
        var hash = PasswordTokens.Hash(command.Token);
        var token = await db.PasswordTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        var user = token is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == token.UserId, cancellationToken);
        if (token is null || user is null || !token.IsUsable(now) || !user.IsActive)
        {
            return IdentityErrors.InvalidToken;
        }

        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var errors = PasswordPolicy.Validate(command.NewPassword, user.Email, settings.PasswordMinLength);
        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        user.SetPasswordHash(passwordHasher.Hash(command.NewPassword!), now);
        await tokenIssuer.RevokeAllAsync(user.Id, now, cancellationToken);
        token.MarkUsed(now);
        auditLog.Record("auth.password_set", "User", user.Id.ToString(), new AuditChanges().Set("via", token.Purpose.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        sessions.Evict(user.Id);
        return Result.Success();
    }
}

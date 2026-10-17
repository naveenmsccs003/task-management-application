using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Application.Settings;
using TaskManagement.Domain.Identity;

namespace TaskManagement.Application.Identity;

public sealed record RequestPasswordResetCommand(string? Email);

/// <summary>
/// "Forgot password". Always succeeds from the caller's point of view, whether or not the email exists
/// (FR-AUTH-06); an email is queued only for an active account.
/// </summary>
public sealed class RequestPasswordResetHandler(
    IAppDbContext db,
    PasswordTokenIssuer tokenIssuer,
    AccountEmails emails,
    SettingsService settingsService,
    IAuditLog auditLog)
{
    public async Task HandleAsync(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!InputRules.IsValidEmail(command.Email))
        {
            return;
        }

        var normalizedEmail = User.NormalizeEmail(command.Email!);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.IsActive, cancellationToken);
        if (user is null)
        {
            return;
        }

        var settings = await settingsService.GetAuthAsync(cancellationToken);
        var lifetime = TimeSpan.FromMinutes(settings.ResetTokenMinutes);
        var token = await tokenIssuer.IssueAsync(user, PasswordTokenPurpose.Reset, lifetime, cancellationToken);
        emails.QueuePasswordReset(user, token, lifetime);
        auditLog.Record("auth.password_reset_requested", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }
}

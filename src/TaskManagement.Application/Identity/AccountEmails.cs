using System.Net;
using Microsoft.Extensions.Options;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Identity;
using TaskManagement.Domain.Notifications;

namespace TaskManagement.Application.Identity;

/// <summary>Builds and queues account emails. All user-supplied text is HTML-encoded (security.md S24).</summary>
public sealed class AccountEmails(IAppDbContext db, IOptions<AppOptions> options, TimeProvider timeProvider)
{
    public void QueueInvite(User user, string token, TimeSpan lifetime) =>
        Queue(
            user,
            $"Your {options.Value.ProductName} account",
            $"An account has been created for you. Set your password using the link below.",
            "Set your password",
            LinkFor("set-password", token),
            lifetime);

    public void QueuePasswordReset(User user, string token, TimeSpan lifetime) =>
        Queue(
            user,
            $"Reset your {options.Value.ProductName} password",
            "We received a request to reset your password. If you did not ask for this, you can ignore this email.",
            "Reset your password",
            LinkFor("reset-password", token),
            lifetime);

    private string LinkFor(string path, string token) =>
        $"{options.Value.PublicBaseUrl.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";

    private void Queue(User user, string subject, string intro, string action, string link, TimeSpan lifetime)
    {
        var expiry = lifetime.TotalHours >= 1 ? $"{lifetime.TotalHours:0} hours" : $"{lifetime.TotalMinutes:0} minutes";
        var text = $"""
            Hello {user.FullName},

            {intro}

            {action}: {link}

            This link can be used once and expires in {expiry}.

            {options.Value.ProductName}
            """;
        var html = $"""
            <p>Hello {WebUtility.HtmlEncode(user.FullName)},</p>
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(action)}</a></p>
            <p>This link can be used once and expires in {expiry}.</p>
            <p>{WebUtility.HtmlEncode(options.Value.ProductName)}</p>
            """;

        db.EmailDeliveries.Add(EmailDelivery.Queue(user.Email, subject, text, html, containsSecret: true, timeProvider.GetUtcNow()));
    }
}

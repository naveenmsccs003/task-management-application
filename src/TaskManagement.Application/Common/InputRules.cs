using System.Net.Mail;

namespace TaskManagement.Application.Common;

internal static class InputRules
{
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > Domain.Identity.User.EmailMaxLength)
        {
            return false;
        }

        // MailAddress accepts display names ("Name <a@b>"); require the plain address only.
        return MailAddress.TryCreate(email.Trim(), out var address) && address.Address == email.Trim();
    }

    /// <summary>Accepts IANA ids such as <c>Asia/Kolkata</c> or <c>UTC</c> known to the server's time-zone database.</summary>
    public static bool IsValidTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone)
        && timeZone.Length <= Domain.Identity.User.TimeZoneMaxLength
        && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
        && zone.HasIanaId;
}

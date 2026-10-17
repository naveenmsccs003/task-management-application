using TaskManagement.Application.Common;

namespace TaskManagement.Application.Identity;

/// <summary>
/// Length-based password rules following NIST SP 800-63B (FR-AUTH-04): a minimum length from settings,
/// a 128-character maximum, no composition rules, and a block-list of very common passwords.
/// </summary>
public static class PasswordPolicy
{
    public const int MaxLength = 128;

    // A small built-in list of the most common passwords that meet a 12+ character minimum. A full
    // breached-password check (e.g. Have I Been Pwned range API) is listed in docs/known-issues.md.
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012", "1234567890123", "12345678901234", "123456789123", "111111111111", "000000000000",
        "qwertyuiop12", "qwertyuiop123", "qwertyuiopasdf", "1q2w3e4r5t6y", "1qaz2wsx3edc", "qazwsxedcrfv",
        "password1234", "password12345", "password123!", "passwordpassword", "Password1234", "Password@123",
        "Password123!", "P@ssw0rd1234", "P@ssword1234", "iloveyou1234", "welcome12345", "Welcome@1234",
        "letmein12345", "administrator", "Administrator1", "admin1234567", "changeme1234", "abcdefghijkl",
        "abc123456789", "aaaaaaaaaaaa", "trustno12345", "sunshine1234", "princess1234", "football1234",
        "baseball1234", "superman1234", "monkey123456", "dragon123456", "master123456", "qwerty123456",
        "asdfghjkl123", "zxcvbnm12345", "taskmanagement", "taskmanager1",
    };

    public static FieldErrors Validate(string? password, string email, int minLength, string field = "newPassword")
    {
        ArgumentNullException.ThrowIfNull(email);
        var errors = new FieldErrors();
        if (string.IsNullOrEmpty(password))
        {
            return errors.Add(field, "Password is required.");
        }

        if (password.Length < minLength)
        {
            errors.Add(field, $"Password must be at least {minLength} characters.");
        }

        if (password.Length > MaxLength)
        {
            errors.Add(field, $"Password must be at most {MaxLength} characters.");
        }

        if (CommonPasswords.Contains(password) || password.Distinct().Count() <= 2)
        {
            errors.Add(field, "This password is too common or too simple. Choose a less predictable one.");
        }

        var localPart = email.Split('@')[0];
        if (localPart.Length >= 4 && password.Contains(localPart, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(field, "Password must not contain your email name.");
        }

        return errors;
    }
}

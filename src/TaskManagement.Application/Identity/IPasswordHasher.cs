namespace TaskManagement.Application.Identity;

public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct, but hashed with older parameters; store a fresh hash.</summary>
    SuccessRehashNeeded,
}

/// <summary>Slow, salted password hashing (FR-AUTH-03). Implemented with ASP.NET Core Identity's PBKDF2 hasher.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordCheck Verify(string passwordHash, string password);

    /// <summary>Spends the same time as a real check, so unknown accounts cannot be detected by timing (FR-AUTH-06).</summary>
    void VerifyAgainstDummy(string password);
}

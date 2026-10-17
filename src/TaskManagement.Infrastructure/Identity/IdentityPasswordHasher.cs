using TaskManagement.Application.Identity;
using AspNetPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using AspNetVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace TaskManagement.Infrastructure.Identity;

/// <summary>ASP.NET Core Identity's PBKDF2 (HMAC-SHA512, 100 000 iterations, random salt) password hasher.</summary>
internal sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly object HashUser = new();
    private static readonly AspNetPasswordHasher Hasher = new();
    private static readonly string DummyHash = Hasher.HashPassword(HashUser, Guid.NewGuid().ToString());

    public string Hash(string password) => Hasher.HashPassword(HashUser, password);

    public PasswordCheck Verify(string passwordHash, string password) =>
        Hasher.VerifyHashedPassword(HashUser, passwordHash, password) switch
        {
            AspNetVerificationResult.Success => PasswordCheck.Success,
            AspNetVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };

    public void VerifyAgainstDummy(string password) => Hasher.VerifyHashedPassword(HashUser, DummyHash, password);
}

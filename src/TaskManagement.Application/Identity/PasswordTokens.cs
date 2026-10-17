using System.Security.Cryptography;
using System.Text;

namespace TaskManagement.Application.Identity;

/// <summary>Generates 256-bit random tokens for email links; only their SHA-256 hash is stored (FR-AUTH-07).</summary>
public static class PasswordTokens
{
    public static (string Token, byte[] Hash) Generate()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

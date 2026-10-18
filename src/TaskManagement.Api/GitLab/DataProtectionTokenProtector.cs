using System.Text;
using Microsoft.AspNetCore.DataProtection;
using TaskManagement.Application.GitLab;

namespace TaskManagement.Api.GitLab;

/// <summary>
/// Encrypts GitLab tokens with ASP.NET Core Data Protection (FR-IMP-02, security.md S13). The keys live outside the
/// database (DataProtection:KeysPath), so a database dump alone cannot reveal a token (AC-AUTH-6).
/// </summary>
internal sealed class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("TaskManagement.GitLab.Token.v1");

    public byte[] Protect(string token) => _protector.Protect(Encoding.UTF8.GetBytes(token));

    public string Unprotect(byte[] ciphertext) => Encoding.UTF8.GetString(_protector.Unprotect(ciphertext));
}

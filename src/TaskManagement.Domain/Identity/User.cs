namespace TaskManagement.Domain.Identity;

/// <summary>
/// A member with a login account (FR-MEM-02). Members are deactivated, never deleted, so history stays intact.
/// Every security-relevant change (password, role, deactivation) issues a new <see cref="SecurityStamp"/>,
/// which signs the member out of all existing sessions.
/// </summary>
public sealed class User
{
    public const int EmailMaxLength = 256;
    public const int FullNameMaxLength = 200;
    public const int TimeZoneMaxLength = 64;
    public const int EmployeeCodeMaxLength = 50;

    private User()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        FullName = string.Empty;
        TimeZone = string.Empty;
        SecurityStamp = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public string NormalizedEmail { get; private set; }

    public string FullName { get; private set; }

    /// <summary>IANA time-zone id, e.g. <c>Asia/Kolkata</c>.</summary>
    public string TimeZone { get; private set; }

    public string? EmployeeCode { get; private set; }

    public Guid GlobalRoleId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>Null until the member sets a password through their invite link.</summary>
    public string? PasswordHash { get; private set; }

    public string SecurityStamp { get; private set; }

    public int FailedSignInCount { get; private set; }

    public DateTimeOffset? LockoutEndsAt { get; private set; }

    public DateTimeOffset? LastSignInAt { get; private set; }

    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? DeactivatedAt { get; private set; }

    public Guid? DeactivatedBy { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>).</summary>
    public uint Version { get; private set; }

    public static User Create(
        string email, string fullName, string timeZone, string? employeeCode, Guid globalRoleId, Guid? createdBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);

        var trimmedEmail = email.Trim();
        return new User
        {
            Id = Guid.CreateVersion7(now),
            Email = trimmedEmail,
            NormalizedEmail = NormalizeEmail(trimmedEmail),
            FullName = fullName.Trim(),
            TimeZone = timeZone,
            EmployeeCode = string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode.Trim(),
            GlobalRoleId = globalRoleId,
            IsActive = true,
            SecurityStamp = NewStamp(),
            CreatedAt = now,
            CreatedBy = createdBy,
        };
    }

    /// <summary>Emails are unique case-insensitively (FR-MEM-02).</summary>
    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Failed attempts are counted with one atomic SQL update (see <c>ISignInAttemptStore</c>) so parallel
    /// guesses cannot bypass the lockout; this entity only reads the result.
    /// </summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } end && end > now;

    /// <summary>Sets a new password hash and signs the member out everywhere (FR-AUTH-08).</summary>
    public void SetPasswordHash(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        PasswordChangedAt = now;
        FailedSignInCount = 0;
        LockoutEndsAt = null;
        SecurityStamp = NewStamp();
    }

    /// <summary>Upgrades the stored hash after a successful sign-in, without signing anyone out.</summary>
    public void RehashPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string fullName, string timeZone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZone);
        FullName = fullName.Trim();
        TimeZone = timeZone;
    }

    public void SetEmployeeCode(string? employeeCode) =>
        EmployeeCode = string.IsNullOrWhiteSpace(employeeCode) ? null : employeeCode.Trim();

    public void ChangeGlobalRole(Guid roleId)
    {
        if (roleId == GlobalRoleId)
        {
            return;
        }

        GlobalRoleId = roleId;
        SecurityStamp = NewStamp();
    }

    public void Deactivate(Guid by, DateTimeOffset now)
    {
        IsActive = false;
        DeactivatedAt = now;
        DeactivatedBy = by;
        SecurityStamp = NewStamp();
    }

    public void Reactivate()
    {
        IsActive = true;
        DeactivatedAt = null;
        DeactivatedBy = null;
        FailedSignInCount = 0;
        LockoutEndsAt = null;
    }

    /// <summary>Forces sign-out of every session (e.g. after an admin-initiated password reset).</summary>
    public void RevokeSessions() => SecurityStamp = NewStamp();

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}

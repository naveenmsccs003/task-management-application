using TaskManagement.Domain.Access;

namespace TaskManagement.Application.Access;

/// <summary>
/// The permissions a member holds through their global role. Project-role permissions (Phase 5) are
/// resolved per project on top of these.
/// </summary>
public sealed class EffectivePermissions(IReadOnlyDictionary<string, PermissionScope> grants)
{
    public static EffectivePermissions None { get; } = new(new Dictionary<string, PermissionScope>());

    public IReadOnlyDictionary<string, PermissionScope> Grants { get; } = grants;

    /// <summary>True when the permission is granted with at least <paramref name="minimumScope"/>.</summary>
    public bool Has(string code, PermissionScope minimumScope) =>
        Grants.TryGetValue(code, out var scope) && scope >= minimumScope;
}

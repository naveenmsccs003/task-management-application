namespace TaskManagement.Domain.Access;

/// <summary>A permission code that roles can be granted. Rows are seeded from <see cref="PermissionCatalog"/>.</summary>
public sealed class Permission
{
    public const int CodeMaxLength = 64;

    private Permission()
    {
        Code = string.Empty;
        Module = string.Empty;
        Description = string.Empty;
    }

    public Permission(string code, string module, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Module = module;
        Description = description;
    }

    public string Code { get; private set; }

    public string Module { get; private set; }

    public string Description { get; private set; }

    public void UpdateDescription(string module, string description)
    {
        Module = module;
        Description = description;
    }
}

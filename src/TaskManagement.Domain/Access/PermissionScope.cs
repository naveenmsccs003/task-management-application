namespace TaskManagement.Domain.Access;

/// <summary>Where a granted permission applies (docs/permissions-matrix.md "How permissions work").</summary>
public enum PermissionScope
{
    /// <summary>Only records assigned to, or created by, the member.</summary>
    Own = 0,

    /// <summary>Only projects where the member belongs with the granting project role.</summary>
    Project = 1,

    /// <summary>Everywhere.</summary>
    Global = 2,
}

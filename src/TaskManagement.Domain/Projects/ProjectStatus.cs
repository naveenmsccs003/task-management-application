namespace TaskManagement.Domain.Projects;

/// <summary>FR-PROJ-01. Completed and Archived projects are read-only (FR-PROJ-04).</summary>
public enum ProjectStatus
{
    Active,
    OnHold,
    Completed,
    Archived,
}

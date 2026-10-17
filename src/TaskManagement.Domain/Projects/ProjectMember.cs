namespace TaskManagement.Domain.Projects;

/// <summary>
/// A member's place in a project, with the project role that decides their permissions there (FR-PROJ-02).
/// Rows are never deleted: removing a member sets <see cref="RemovedAt"/>, and adding them again creates a new row,
/// so membership history is kept.
/// </summary>
public sealed class ProjectMember
{
    private ProjectMember()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ProjectRoleId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public Guid? AddedBy { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public Guid? RemovedBy { get; private set; }

    public bool IsCurrent => RemovedAt is null;

    public static ProjectMember Create(Guid projectId, Guid userId, Guid projectRoleId, Guid? addedBy, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ProjectId = projectId,
            UserId = userId,
            ProjectRoleId = projectRoleId,
            AddedAt = now,
            AddedBy = addedBy,
        };

    public void ChangeRole(Guid projectRoleId) => ProjectRoleId = projectRoleId;

    public void Remove(Guid by, DateTimeOffset now)
    {
        if (RemovedAt is not null)
        {
            return;
        }

        RemovedAt = now;
        RemovedBy = by;
    }
}

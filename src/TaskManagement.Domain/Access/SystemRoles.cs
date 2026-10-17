namespace TaskManagement.Domain.Access;

/// <summary>Seed roles with fixed ids, so code and data can refer to them reliably.</summary>
public static class SystemRoles
{
    public static readonly Guid AdminId = new("0192f2a0-0000-7000-8000-000000000001");
    public static readonly Guid TeamLeaderId = new("0192f2a0-0000-7000-8000-000000000002");
    public static readonly Guid ProjectManagerId = new("0192f2a0-0000-7000-8000-000000000003");
    public static readonly Guid DeveloperId = new("0192f2a0-0000-7000-8000-000000000004");
    public static readonly Guid ViewerId = new("0192f2a0-0000-7000-8000-000000000005");

    public static IReadOnlyList<(Guid Id, string Name, string Description)> All { get; } =
    [
        (AdminId, "Admin", "Runs the system: accounts, roles, settings, integrations and audit."),
        (TeamLeaderId, "Team Leader", "Technical lead of a project: creates and assigns tasks, approves algorithms, reviews work."),
        (ProjectManagerId, "Project Manager", "Manages a project: members, schedule, tasks, deadlines, closure and reports."),
        (DeveloperId, "Developer", "Works on assigned tasks: algorithm, time tracking, checklist and submission."),
        (ViewerId, "Viewer", "Read-only access to projects where they are a member."),
    ];
}

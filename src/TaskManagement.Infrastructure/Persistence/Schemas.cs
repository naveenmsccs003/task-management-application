namespace TaskManagement.Infrastructure.Persistence;

/// <summary>One PostgreSQL schema per module (docs/database-design.md §1).</summary>
public static class Schemas
{
    public const string Identity = "identity";
    public const string Access = "access";
    public const string Settings = "settings";
    public const string Projects = "projects";
    public const string Tasks = "tasks";
    public const string Time = "time";
    public const string Notifications = "notifications";
    public const string Files = "files";
    public const string GitLab = "gitlab";
    public const string Audit = "audit";
    public const string Ops = "ops";
}

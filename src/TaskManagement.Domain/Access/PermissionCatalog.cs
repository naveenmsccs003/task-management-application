namespace TaskManagement.Domain.Access;

/// <summary>
/// Every permission code, with the seed-role defaults from docs/permissions-matrix.md.
/// The seeder inserts missing codes and grants their defaults once; after that, Admins own the grants.
/// </summary>
public static class PermissionCatalog
{
    // Codes referenced from code. Keep in sync with the entries below.
    public const string MembersView = "members.view";
    public const string MembersCreate = "members.create";
    public const string MembersUpdate = "members.update";
    public const string MembersResetPassword = "members.reset-password";
    public const string ProfileManage = "profile.manage";
    public const string RolesView = "roles.view";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string AuditView = "audit.view";
    public const string ProjectsView = "projects.view";
    public const string ProjectsCreate = "projects.create";
    public const string ProjectsUpdate = "projects.update";
    public const string ProjectsMembersManage = "projects.members.manage";
    public const string TasksView = "tasks.view";
    public const string TasksCreate = "tasks.create";
    public const string TasksUpdate = "tasks.update";
    public const string TasksAssign = "tasks.assign";
    public const string TasksCancel = "tasks.cancel";
    public const string TasksDeleteDraft = "tasks.delete-draft";
    public const string TasksComment = "tasks.comment";
    public const string TasksWork = "tasks.work";
    public const string AlgorithmsView = "algorithms.view";
    public const string AlgorithmsSubmit = "algorithms.submit";
    public const string AlgorithmsApprove = "algorithms.approve";
    public const string ChecklistsManage = "checklists.manage";
    public const string ChecklistsManageProject = "checklists.manage-project";
    public const string ChecklistsComplete = "checklists.complete";
    public const string TimeView = "time.view";
    public const string TimeAdjust = "time.adjust";
    public const string NotificationsView = "notifications.view";
    public const string TasksSubmit = "tasks.submit";
    public const string TasksReview = "tasks.review";
    public const string TasksClose = "tasks.close";
    public const string TasksReopen = "tasks.reopen";
    public const string DeadlinesRequest = "deadlines.request";
    public const string DeadlinesApprove = "deadlines.approve";
    public const string ReassignmentsApprove = "reassignments.approve";
    public const string DelaysReport = "delays.report";
    public const string DelaysView = "delays.view";
    public const string AttachmentsUpload = "attachments.upload";
    public const string AttachmentsDelete = "attachments.delete";
    public const string GitLabConnectionsManage = "gitlab.connections.manage";
    public const string GitLabImport = "gitlab.import";
    public const string DashboardView = "dashboard.view";
    public const string ReportsView = "reports.view";
    public const string ReportsExport = "reports.export";
    public const string SystemHealth = "system.health";

    /// <summary>Permissions the Admin role can never lose (FR-ROLE-03).</summary>
    public static IReadOnlyList<string> AdminProtected { get; } = [RolesManage, MembersCreate];

    public sealed record Entry(
        string Code,
        string Module,
        string Description,
        PermissionScope? Admin,
        PermissionScope? TeamLeader,
        PermissionScope? ProjectManager,
        PermissionScope? Developer,
        PermissionScope? Viewer)
    {
        public PermissionScope? DefaultFor(Guid roleId) =>
            roleId == SystemRoles.AdminId ? Admin
            : roleId == SystemRoles.TeamLeaderId ? TeamLeader
            : roleId == SystemRoles.ProjectManagerId ? ProjectManager
            : roleId == SystemRoles.DeveloperId ? Developer
            : roleId == SystemRoles.ViewerId ? Viewer
            : null;
    }

    private const PermissionScope G = PermissionScope.Global;
    private const PermissionScope P = PermissionScope.Project;
    private const PermissionScope O = PermissionScope.Own;

    public static IReadOnlyList<Entry> All { get; } =
    [
        // Accounts, roles, settings
        new(MembersView, "Members", "List and view members", G, P, P, null, null),
        new(MembersCreate, "Members", "Create login accounts", G, null, null, null, null),
        new(MembersUpdate, "Members", "Edit members, change global role, deactivate or reactivate", G, null, null, null, null),
        new(MembersResetPassword, "Members", "Send a password reset email to a member", G, null, null, null, null),
        new(ProfileManage, "Members", "Edit own profile, password and notification preferences", O, O, O, O, O),
        new(RolesView, "Roles", "View roles and permissions", G, null, null, null, null),
        new(RolesManage, "Roles", "Create and edit roles and their permissions", G, null, null, null, null),
        new(SettingsManage, "Settings", "Edit global settings", G, null, null, null, null),

        // Projects
        new(ProjectsView, "Projects", "View project details", G, P, P, P, P),
        new(ProjectsCreate, "Projects", "Create projects", G, null, G, null, null),
        new(ProjectsUpdate, "Projects", "Edit project details and status, archive", G, null, P, null, null),
        new(ProjectsMembersManage, "Projects", "Add and remove project members and set project roles", G, null, P, null, null),

        // Tasks
        new(TasksView, "Tasks", "View tasks", G, P, P, O, P),
        new(TasksCreate, "Tasks", "Create tasks", null, P, P, null, null),
        new(TasksUpdate, "Tasks", "Edit task fields", null, P, P, null, null),
        new(TasksAssign, "Tasks", "Assign tasks and request reassignment", null, P, P, null, null),
        new(TasksCancel, "Tasks", "Cancel a task with a reason", null, P, P, null, null),
        new(TasksDeleteDraft, "Tasks", "Delete a draft task without history", null, P, P, null, null),
        new(TasksComment, "Tasks", "Add comments", null, P, P, O, null),
        new(TasksWork, "Tasks", "Start, pause, resume and stop own work", null, O, null, O, null),
        new(TasksSubmit, "Tasks", "Submit own task for review", null, O, null, O, null),
        new(TasksReview, "Tasks", "Approve review or request revision", null, P, null, null, null),
        new(TasksClose, "Tasks", "Close a task after TL approval", null, P, P, null, null),
        new(TasksReopen, "Tasks", "Reopen a closed task", null, P, P, null, null),

        // Algorithm, checklist
        new(AlgorithmsView, "Algorithms", "View algorithm submissions", G, P, P, O, P),
        new(AlgorithmsSubmit, "Algorithms", "Submit an algorithm for own task", null, O, null, O, null),
        new(AlgorithmsApprove, "Algorithms", "Approve or reject algorithms", null, P, null, null, null),
        new(ChecklistsManage, "Checklists", "Manage global checklist templates", G, null, null, null, null),
        new(ChecklistsManageProject, "Checklists", "Manage project templates and task-specific items", null, P, P, null, null),
        new(ChecklistsComplete, "Checklists", "Tick checklist items on own task", null, O, null, O, null),

        // Time
        new(TimeView, "Time", "View work sessions and totals", G, P, P, O, null),
        new(TimeAdjust, "Time", "Add time adjustments with a reason", null, P, P, null, null),

        // Requests
        new(DeadlinesRequest, "Requests", "Request a deadline extension", null, P, P, O, null),
        new(DeadlinesApprove, "Requests", "Approve or reject deadline extensions", G, P, P, null, null),
        new(ReassignmentsApprove, "Requests", "Approve or reject reassignments", G, P, P, null, null),
        new(DelaysReport, "Requests", "File a delay report", null, O, null, O, null),
        new(DelaysView, "Requests", "View delay reports", G, P, P, O, null),

        // Attachments (viewing follows the parent record's permission)
        new(AttachmentsUpload, "Attachments", "Upload attachments", null, P, P, O, null),
        new(AttachmentsDelete, "Attachments", "Delete attachments", null, P, P, O, null),

        // Notifications, reports, audit, integration, administration
        new(NotificationsView, "Notifications", "View own notifications", O, O, O, O, O),
        new(DashboardView, "Reports", "View the role dashboard", G, P, P, O, P),
        new(ReportsView, "Reports", "View reports", G, P, P, O, P),
        new(ReportsExport, "Reports", "Export reports as CSV", G, P, P, null, null),
        new(AuditView, "Audit", "View the audit log", G, null, null, null, null),
        new("audit.view-project", "Audit", "View a project's audit entries", G, P, P, null, null),
        new(GitLabConnectionsManage, "GitLab", "Configure GitLab connections and tokens", G, null, null, null, null),
        new(GitLabImport, "GitLab", "Link projects and import GitLab issues", null, P, P, null, null),
        new(SystemHealth, "Administration", "View detailed health, job and email status", G, null, null, null, null),
    ];

    public static bool Contains(string code) => All.Any(e => e.Code == code);
}

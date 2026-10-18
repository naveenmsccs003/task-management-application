using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Common;
using TaskManagement.Application.Notifications;
using TaskManagement.Application.Tasks;
using TaskManagement.Application.Work;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.GitLab;
using TaskManagement.Domain.Projects;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Reporting;

public sealed record DashboardTaskDto(Guid Id, string Key, string Title, TaskItemStatus Status, DateOnly? DueDate, UserRef? Assignee);

/// <summary>The first few items of a list and how many there are in all.</summary>
public sealed record DashboardList(int Total, IReadOnlyList<DashboardTaskDto> Items);

public sealed record StatusCountDto(TaskItemStatus Status, int Count);

/// <summary>FR-RPT-01: the member's own work.</summary>
public sealed record MyWorkDto(
    IReadOnlyList<StatusCountDto> ByStatus,
    ActiveSessionDto? ActiveSession,
    long SecondsThisWeek,
    DashboardList DueSoon,
    DashboardList Overdue,
    DashboardList WaitingForFeedback,
    DashboardList RevisionRequested);

public sealed record OverAllocatedTaskDto(Guid Id, string Key, string Title, UserRef? Assignee, long AllocatedSeconds, long SpentSeconds);

public sealed record WorkloadDto(UserRef Member, int OpenTasks, int InProgress, long SecondsThisWeek);

/// <summary>FR-RPT-02: what waits for a Team Leader in the projects they lead.</summary>
public sealed record TeamDto(
    DashboardList AlgorithmApprovals,
    DashboardList Reviews,
    int OverAllocatedTotal,
    IReadOnlyList<OverAllocatedTaskDto> OverAllocated,
    DashboardList Overdue,
    int PendingRequests,
    IReadOnlyList<WorkloadDto> Workload);

public sealed record WeekCountDto(DateOnly WeekStart, int Count);

/// <summary>FR-RPT-03: one project's progress.</summary>
public sealed record ProjectProgressDto(
    Guid Id,
    string Key,
    string Name,
    ProjectStatus Status,
    IReadOnlyList<StatusCountDto> ByStatus,
    long AllocatedSeconds,
    long SpentSeconds,
    int Overdue,
    IReadOnlyList<WeekCountDto> OverdueTrend,
    int ExtensionsApproved,
    int RevisionsRequested);

public sealed record ProjectsDto(int Total, IReadOnlyList<ProjectProgressDto> Items);

public sealed record GitLabStatusDto(Guid Id, string Name, GitLabConnectionStatus Status, DateTimeOffset? LastCheckedAt, string? LastError);

/// <summary>FR-RPT-04: members, sign-in failures, integrations, background jobs and email.</summary>
public sealed record AdminDashboardDto(
    int ActiveMembers,
    int InvitedMembers,
    int InactiveMembers,
    int LockedMembers,
    int FailedSignIns24Hours,
    int FailedSignIns7Days,
    IReadOnlyList<GitLabStatusDto> GitLab,
    IReadOnlyList<JobRunDto> Jobs,
    int EmailsPending,
    int EmailsFailed,
    DateTimeOffset? OldestPendingEmailAt);

/// <summary>
/// GET /dashboard. Sections appear by what the member may do, not by role name, so custom roles work too: My work for
/// members who work on tasks, Team for project-wide algorithm approvers or reviewers, Projects for project-wide
/// dashboard.view (Admins: all projects), Administration for system.health. Null sections are not shown.
/// </summary>
public sealed record DashboardDto(DateTimeOffset ServerTime, string TimeZone, MyWorkDto? MyWork, TeamDto? Team, ProjectsDto? Projects, AdminDashboardDto? Admin);

/// <summary>Role dashboards (FR-RPT-01..04). Read-only queries across modules, as architecture.md §4 allows for Reporting.</summary>
public sealed class DashboardService(
    IAppDbContext db, ICurrentUser currentUser, ProjectAccess projects, WorkService work, JobRecorder jobs, TimeProvider timeProvider)
{
    public const int ListSize = 10;
    public const int MaxProjects = 12;
    public const int TrendWeeks = 8;
    public const int DueSoonDays = 3;

    public async Task<DashboardDto> GetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.RequiredUserId;
        var now = timeProvider.GetUtcNow();
        var timeZone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZone).FirstAsync(cancellationToken);
        var zone = LocalTime.Zone(timeZone);
        var today = LocalTime.DateOf(now, zone);
        var weekStart = LocalTime.StartOfDay(LocalTime.StartOfWeek(today), zone);
        var clock = new Clock(now, today, weekStart, zone);

        var working = await projects.ProjectsWithAsync(PermissionCatalog.TasksWork, cancellationToken);
        var myWork = working.Everywhere || working.ProjectWide.Count + working.OwnOnly.Count > 0
            ? await MyWorkAsync(userId, clock, cancellationToken)
            : null;

        var approvals = await ProjectWideAsync(PermissionCatalog.AlgorithmsApprove, cancellationToken);
        var reviews = await ProjectWideAsync(PermissionCatalog.TasksReview, cancellationToken);
        var team = approvals.Count + reviews.Count > 0 ? await TeamAsync(userId, approvals, reviews, clock, cancellationToken) : null;

        var dashboard = await projects.ProjectsWithAsync(PermissionCatalog.DashboardView, cancellationToken);
        var projectSection = dashboard.Everywhere || dashboard.ProjectWide.Count > 0
            ? await ProjectsAsync(dashboard.Everywhere ? null : dashboard.ProjectWide, clock, cancellationToken)
            : null;

        var admin = (await projects.ProjectsWithAsync(PermissionCatalog.SystemHealth, cancellationToken)).Everywhere
            ? await AdminAsync(now, cancellationToken)
            : null;

        return new DashboardDto(now, zone.Id, myWork, team, projectSection, admin);
    }

    private async Task<MyWorkDto> MyWorkAsync(Guid userId, Clock clock, CancellationToken cancellationToken)
    {
        var mine = db.Tasks.AsNoTracking().Where(t => t.AssigneeId == userId && t.Status != TaskItemStatus.Closed && t.Status != TaskItemStatus.Cancelled);
        var byStatus = await mine.GroupBy(t => t.Status).Select(g => new StatusCountDto(g.Key, g.Count())).ToListAsync(cancellationToken);
        var open = TaskWorkflow.OpenStatuses;
        var soon = clock.Today.AddDays(DueSoonDays);
        var today = clock.Today;

        var weekStart = clock.WeekStart;
        var sessions = await db.WorkSessions.AsNoTracking()
            .Where(s => s.UserId == userId && (s.EndedAt == null || s.EndedAt > weekStart))
            .Select(s => new { s.StartedAt, s.EndedAt })
            .ToListAsync(cancellationToken);
        var adjusted = await db.TimeAdjustments.AsNoTracking()
            .Where(a => a.UserId == userId && a.CreatedAt >= weekStart)
            .SumAsync(a => (long?)a.Minutes, cancellationToken) ?? 0;
        var seconds = sessions.Sum(s => Overlap(s.StartedAt, s.EndedAt ?? clock.Now, weekStart, clock.Now)) + (adjusted * 60);

        return new MyWorkDto(
            byStatus.OrderBy(s => s.Status).ToList(),
            await work.ActiveSessionAsync(userId, cancellationToken),
            Math.Max(0, seconds),
            await ListAsync(mine.Where(t => open.Contains(t.Status) && !t.IsOverdue && t.DueDate >= today && t.DueDate <= soon), byDueDate: true, cancellationToken),
            await ListAsync(mine.Where(t => open.Contains(t.Status) && t.IsOverdue), byDueDate: true, cancellationToken),
            await ListAsync(mine.Where(t => t.Status == TaskItemStatus.AlgorithmSubmitted || t.Status == TaskItemStatus.InReview), byDueDate: false, cancellationToken),
            await ListAsync(mine.Where(t => t.Status == TaskItemStatus.RevisionRequested), byDueDate: true, cancellationToken));
    }

    private async Task<TeamDto> TeamAsync(
        Guid userId, IReadOnlyList<Guid> approvalProjects, IReadOnlyList<Guid> reviewProjects, Clock clock, CancellationToken cancellationToken)
    {
        var lead = approvalProjects.Union(reviewProjects).ToList();
        var tasks = db.Tasks.AsNoTracking();
        var open = TaskWorkflow.OpenStatuses;

        var candidates = await tasks
            .Where(t => lead.Contains(t.ProjectId) && open.Contains(t.Status) && t.AllocatedMinutes != null)
            .Select(t => new { t.Id, t.Key, t.Title, t.AssigneeId, Allocated = t.AllocatedMinutes!.Value * 60L })
            .ToListAsync(cancellationToken);
        var spent = await SpentTime.ForTasksAsync(db, candidates.Select(t => t.Id).ToList(), clock.Now, cancellationToken);
        var over = candidates.Where(t => spent.GetValueOrDefault(t.Id) > t.Allocated)
            .OrderByDescending(t => (double)spent.GetValueOrDefault(t.Id) / t.Allocated)
            .ToList();
        var overNames = await NamesAsync(over.Take(ListSize).Select(t => t.AssigneeId), cancellationToken);

        var pendingRequests = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalRequestStatus.Pending && r.RequestedBy != userId)
            .Join(tasks.Where(t => lead.Contains(t.ProjectId)), r => r.TaskId, t => t.Id, (r, _) => r.Id)
            .CountAsync(cancellationToken);

        return new TeamDto(
            await ListAsync(tasks.Where(t => approvalProjects.Contains(t.ProjectId) && t.Status == TaskItemStatus.AlgorithmSubmitted && t.AssigneeId != userId),
                byDueDate: true, cancellationToken),
            await ListAsync(tasks.Where(t => reviewProjects.Contains(t.ProjectId) && t.Status == TaskItemStatus.InReview && t.AssigneeId != userId),
                byDueDate: true, cancellationToken),
            over.Count,
            over.Take(ListSize)
                .Select(t => new OverAllocatedTaskDto(t.Id, t.Key, t.Title, Ref(overNames, t.AssigneeId), t.Allocated, spent.GetValueOrDefault(t.Id)))
                .ToList(),
            await ListAsync(tasks.Where(t => lead.Contains(t.ProjectId) && open.Contains(t.Status) && t.IsOverdue), byDueDate: true, cancellationToken),
            pendingRequests,
            await WorkloadAsync(lead, clock, cancellationToken));
    }

    /// <summary>Open and in-progress tasks per assignee in the lead's projects, with hours on those projects this week.</summary>
    private async Task<IReadOnlyList<WorkloadDto>> WorkloadAsync(IReadOnlyList<Guid> lead, Clock clock, CancellationToken cancellationToken)
    {
        var open = TaskWorkflow.OpenStatuses;
        var counts = await db.Tasks.AsNoTracking()
            .Where(t => lead.Contains(t.ProjectId) && t.AssigneeId != null && open.Contains(t.Status))
            .GroupBy(t => t.AssigneeId!.Value)
            .Select(g => new { UserId = g.Key, Open = g.Count(), InProgress = g.Count(t => t.Status == TaskItemStatus.InProgress) })
            .ToListAsync(cancellationToken);
        var weekStart = clock.WeekStart;
        var sessions = await db.WorkSessions.AsNoTracking()
            .Where(s => s.EndedAt == null || s.EndedAt > weekStart)
            .Join(db.Tasks.Where(t => lead.Contains(t.ProjectId)), s => s.TaskId, t => t.Id, (s, _) => new { s.UserId, s.StartedAt, s.EndedAt })
            .ToListAsync(cancellationToken);
        var adjusted = await db.TimeAdjustments.AsNoTracking()
            .Where(a => a.CreatedAt >= weekStart)
            .Join(db.Tasks.Where(t => lead.Contains(t.ProjectId)), a => a.TaskId, t => t.Id, (a, _) => new { a.UserId, a.Minutes })
            .ToListAsync(cancellationToken);
        var seconds = sessions.GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(s => Overlap(s.StartedAt, s.EndedAt ?? clock.Now, weekStart, clock.Now)));
        foreach (var a in adjusted)
        {
            seconds[a.UserId] = seconds.GetValueOrDefault(a.UserId) + (a.Minutes * 60L);
        }

        var members = counts.Select(c => c.UserId).Union(seconds.Keys).ToList();
        var names = await NamesAsync(members.Select(m => (Guid?)m), cancellationToken);
        return members
            .Select(m =>
            {
                var c = counts.Find(x => x.UserId == m);
                return new WorkloadDto(new UserRef(m, names.GetValueOrDefault(m, "Unknown")), c?.Open ?? 0, c?.InProgress ?? 0, Math.Max(0, seconds.GetValueOrDefault(m)));
            })
            .OrderByDescending(w => w.OpenTasks).ThenByDescending(w => w.SecondsThisWeek).ThenBy(w => w.Member.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Active and on-hold projects (all of them for a Global grant), by key.</summary>
    private async Task<ProjectsDto> ProjectsAsync(IReadOnlyList<Guid>? scope, Clock clock, CancellationToken cancellationToken)
    {
        var query = db.Projects.AsNoTracking().Where(p => p.Status == ProjectStatus.Active || p.Status == ProjectStatus.OnHold);
        if (scope is not null)
        {
            query = query.Where(p => scope.Contains(p.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var list = await query.OrderBy(p => p.Key).Take(MaxProjects).Select(p => new { p.Id, p.Key, p.Name, p.Status }).ToListAsync(cancellationToken);
        var ids = list.Select(p => p.Id).ToList();
        var tasks = db.Tasks.AsNoTracking().Where(t => ids.Contains(t.ProjectId));
        var open = TaskWorkflow.OpenStatuses;

        var byStatus = await tasks.GroupBy(t => new { t.ProjectId, t.Status }).Select(g => new { g.Key.ProjectId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var allocated = await tasks.Where(t => t.Status != TaskItemStatus.Cancelled && t.AllocatedMinutes != null)
            .GroupBy(t => t.ProjectId).Select(g => new { ProjectId = g.Key, Minutes = g.Sum(t => (long)t.AllocatedMinutes!.Value) })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Minutes * 60, cancellationToken);
        var closedSeconds = await db.WorkSessions.AsNoTracking().Where(s => s.DurationSeconds != null)
            .Join(tasks, s => s.TaskId, t => t.Id, (s, t) => new { t.ProjectId, s.DurationSeconds })
            .GroupBy(x => x.ProjectId).Select(g => new { ProjectId = g.Key, Seconds = g.Sum(x => (long?)x.DurationSeconds) ?? 0 })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Seconds, cancellationToken);
        var activeSessions = await db.WorkSessions.AsNoTracking().Where(s => s.EndedAt == null)
            .Join(tasks, s => s.TaskId, t => t.Id, (s, t) => new { t.ProjectId, s.StartedAt })
            .ToListAsync(cancellationToken);
        var adjustedMinutes = await db.TimeAdjustments.AsNoTracking()
            .Join(tasks, a => a.TaskId, t => t.Id, (a, t) => new { t.ProjectId, a.Minutes })
            .GroupBy(x => x.ProjectId).Select(g => new { ProjectId = g.Key, Minutes = g.Sum(x => (long)x.Minutes) })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Minutes, cancellationToken);
        var overdue = await tasks.Where(t => t.IsOverdue && open.Contains(t.Status)).GroupBy(t => t.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);
        var extensions = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Type == ApprovalRequestType.DeadlineExtension && r.Status == ApprovalRequestStatus.Approved)
            .Join(tasks, r => r.TaskId, t => t.Id, (_, t) => t.ProjectId)
            .GroupBy(p => p).Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);
        var revisions = await db.ReviewSubmissions.AsNoTracking()
            .Where(r => r.Decision == ReviewDecision.RevisionRequested)
            .Join(tasks, r => r.TaskId, t => t.Id, (_, t) => t.ProjectId)
            .GroupBy(p => p).Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count, cancellationToken);
        var trend = await OverdueTrendAsync(tasks, clock, cancellationToken);

        var items = list.Select(p => new ProjectProgressDto(
                p.Id,
                p.Key,
                p.Name,
                p.Status,
                byStatus.Where(s => s.ProjectId == p.Id).OrderBy(s => s.Status).Select(s => new StatusCountDto(s.Status, s.Count)).ToList(),
                allocated.GetValueOrDefault(p.Id),
                Math.Max(0, closedSeconds.GetValueOrDefault(p.Id)
                    + activeSessions.Where(s => s.ProjectId == p.Id).Sum(s => Math.Max(0, (long)(clock.Now - s.StartedAt).TotalSeconds))
                    + (adjustedMinutes.GetValueOrDefault(p.Id) * 60)),
                overdue.GetValueOrDefault(p.Id),
                trend(p.Id),
                extensions.GetValueOrDefault(p.Id),
                revisions.GetValueOrDefault(p.Id)))
            .ToList();
        return new ProjectsDto(total, items);
    }

    /// <summary>
    /// Overdue tasks at the end of each of the last weeks (the current week up to today). Uses each task's current due
    /// date, so a task whose deadline was later extended counts as on time for the weeks before the extension too
    /// (known-issues.md L43); there is no history of the Overdue flag.
    /// </summary>
    private async Task<Func<Guid, IReadOnlyList<WeekCountDto>>> OverdueTrendAsync(IQueryable<TaskItem> tasks, Clock clock, CancellationToken cancellationToken)
    {
        var weeks = Enumerable.Range(0, TrendWeeks).Select(i => LocalTime.StartOfWeek(clock.Today).AddDays(-7 * (TrendWeeks - 1 - i))).ToList();
        var since = LocalTime.StartOfDay(weeks[0], clock.Zone);
        var today = clock.Today;
        var candidates = await tasks
            .Where(t => t.DueDate != null && t.DueDate < today
                && (t.ClosedAt == null || t.ClosedAt >= since) && (t.CancelledAt == null || t.CancelledAt >= since))
            .Select(t => new { t.ProjectId, Due = t.DueDate!.Value, t.CreatedAt, t.ClosedAt, t.CancelledAt })
            .ToListAsync(cancellationToken);
        return projectId => weeks.Select(week =>
            {
                var day = week.AddDays(6) < today ? week.AddDays(6) : today;
                var endOfDay = LocalTime.StartOfDay(day.AddDays(1), clock.Zone);
                var count = candidates.Count(t => t.ProjectId == projectId && t.Due < day && t.CreatedAt < endOfDay
                    && (t.ClosedAt == null || t.ClosedAt >= endOfDay) && (t.CancelledAt == null || t.CancelledAt >= endOfDay));
                return new WeekCountDto(week, count);
            })
            .ToList();
    }

    private async Task<AdminDashboardDto> AdminAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var members = await db.Users.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Active = g.Count(u => u.IsActive && u.PasswordHash != null),
                Invited = g.Count(u => u.IsActive && u.PasswordHash == null),
                Inactive = g.Count(u => !u.IsActive),
                Locked = g.Count(u => u.IsActive && u.LockoutEndsAt > now),
            })
            .FirstOrDefaultAsync(cancellationToken);
        var dayAgo = now.AddDays(-1);
        var weekAgo = now.AddDays(-7);
        var failed = await db.AuditEntries.AsNoTracking()
            .Where(e => e.Action == "auth.sign_in_failed" && e.OccurredAt >= weekAgo)
            .GroupBy(_ => 1)
            .Select(g => new { Week = g.Count(), Day = g.Count(e => e.OccurredAt >= dayAgo) })
            .FirstOrDefaultAsync(cancellationToken);
        var gitLab = await db.GitLabConnections.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new GitLabStatusDto(c.Id, c.Name, c.Status, c.LastCheckedAt, c.LastError))
            .ToListAsync(cancellationToken);
        var status = await jobs.StatusAsync(cancellationToken);
        return new AdminDashboardDto(
            members?.Active ?? 0,
            members?.Invited ?? 0,
            members?.Inactive ?? 0,
            members?.Locked ?? 0,
            failed?.Day ?? 0,
            failed?.Week ?? 0,
            gitLab,
            status.Jobs,
            status.Email.Pending,
            status.Email.Failed,
            status.Email.OldestPendingAt);
    }

    private async Task<IReadOnlyList<Guid>> ProjectWideAsync(string code, CancellationToken cancellationToken)
    {
        var scope = await projects.ProjectsWithAsync(code, cancellationToken);
        return scope.Everywhere
            ? await db.Projects.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)
            : scope.ProjectWide;
    }

    private async Task<DashboardList> ListAsync(IQueryable<TaskItem> query, bool byDueDate, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var ordered = byDueDate
            ? query.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.Key)
            : query.OrderBy(t => t.UpdatedAt);
        var items = await ordered.Take(ListSize)
            .Select(t => new { t.Id, t.Key, t.Title, t.Status, t.DueDate, t.AssigneeId })
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(items.Select(t => t.AssigneeId), cancellationToken);
        return new DashboardList(total, items.Select(t => new DashboardTaskDto(t.Id, t.Key, t.Title, t.Status, t.DueDate, Ref(names, t.AssigneeId))).ToList());
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.OfType<Guid>().Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    private static UserRef? Ref(Dictionary<Guid, string> names, Guid? userId) =>
        userId is { } id && names.TryGetValue(id, out var name) ? new UserRef(id, name) : null;

    /// <summary>Seconds of [start, end) that fall within [from, until).</summary>
    private static long Overlap(DateTimeOffset start, DateTimeOffset end, DateTimeOffset from, DateTimeOffset until)
    {
        var a = start > from ? start : from;
        var b = end < until ? end : until;
        return b > a ? (long)(b - a).TotalSeconds : 0;
    }

    private sealed record Clock(DateTimeOffset Now, DateOnly Today, DateTimeOffset WeekStart, TimeZoneInfo Zone);
}

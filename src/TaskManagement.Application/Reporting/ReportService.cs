using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Access;
using TaskManagement.Application.Audit;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Access;
using TaskManagement.Domain.Common;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Reporting;

/// <summary>Filters for every report. Dates are calendar dates in the viewer's time zone, both inclusive.</summary>
public sealed record ReportQuery(DateOnly? From, DateOnly? To, Guid? ProjectId, Guid? UserId, string? GroupBy);

/// <summary>A report the member may run, for the report picker.</summary>
public sealed record ReportInfoDto(string Name, string Title, string Description, bool UsesDateRange, IReadOnlyList<string> GroupBy, bool CanExport);

public sealed record ReportFile(string FileName, byte[] Content);

/// <summary>
/// Reports (FR-RPT-05..07, FR-TIME-10). Each report needs reports.view <em>and</em> the permission behind its data
/// (tasks.view, plus time.view or delays.view), intersected per project, so a report never shows more than the
/// member could see on the task pages (FR-RPT-06): Own scope means tasks the member is or was assigned to. CSV export
/// additionally needs reports.export there, is audited, and escapes formulas (S20).
/// </summary>
public sealed partial class ReportService(
    IAppDbContext db, ICurrentUser currentUser, ProjectAccess projects, IAuditLog auditLog, TimeProvider timeProvider)
{
    public const int MaxRows = 5_000;
    public const int MaxDays = 366;
    public const int DefaultDays = 30;

    /// <summary>More work sessions than this in one time report means the range or filters are too wide.</summary>
    public const int MaxSessions = 100_000;

    private static readonly ReportDefinition[] Definitions =
    [
        new("time", "Time spent", "Hours worked by member, project, task, revision or day (timesheet), including time adjustments.", true,
            ["task", "member", "project", "revision", "day"], PermissionCatalog.TimeView),
        new("allocated-vs-actual", "Allocated vs actual", "Tasks with allocated hours: time spent, overrun and percentage used.", true, [], PermissionCatalog.TimeView),
        new("overdue", "Overdue tasks", "Tasks that are overdue now, oldest due date first.", false, [], null),
        new("extensions", "Deadline extensions", "Deadline extension requests made in the period and their outcome.", true, [], null),
        new("revisions", "Revisions", "Tasks sent back for revision in the period, most revisions first.", true, [], null),
        new("delays", "Delay reports", "Delay reports filed in the period.", true, [], PermissionCatalog.DelaysView),
    ];

    /// <summary>The reports the member may run, with whether they may export each.</summary>
    public async Task<IReadOnlyList<ReportInfoDto>> ListAsync(CancellationToken cancellationToken)
    {
        var result = new List<ReportInfoDto>();
        foreach (var definition in Definitions)
        {
            var scope = await ScopeAsync(definition, export: false, cancellationToken);
            if (IsEmpty(scope))
            {
                continue;
            }

            var exportScope = await ScopeAsync(definition, export: true, cancellationToken);
            result.Add(new ReportInfoDto(definition.Name, definition.Title, definition.Description, definition.UsesDateRange, definition.GroupBy, !IsEmpty(exportScope)));
        }

        return result;
    }

    public async Task<Result<ReportTable>> RunAsync(string name, ReportQuery query, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(name, query, export: false, cancellationToken);
        return context.IsFailure ? context.Error! : await BuildAsync(context.Value!, cancellationToken);
    }

    /// <summary>The same table as CSV (FR-RPT-07); every export is audited (FR-AUD-02).</summary>
    public async Task<Result<ReportFile>> ExportAsync(string name, ReportQuery query, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(name, query, export: true, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var built = await BuildAsync(context.Value!, cancellationToken);
        if (built.IsFailure)
        {
            return built.Error!;
        }

        var table = built.Value!;
        var c = context.Value!;
        auditLog.Record("report.exported", "Report", c.Definition.Name, new AuditChanges()
            .Set("from", c.Definition.UsesDateRange ? c.From.ToString("yyyy-MM-dd") : null)
            .Set("to", c.Definition.UsesDateRange ? c.To.ToString("yyyy-MM-dd") : null)
            .Set("projectId", c.ProjectId?.ToString())
            .Set("userId", c.FilterUserId?.ToString())
            .Set("groupBy", c.GroupBy)
            .Set("rows", table.Rows.Count), c.ProjectId);
        await db.SaveChangesAsync(cancellationToken);

        var period = c.Definition.UsesDateRange ? $"-{c.From:yyyy-MM-dd}-to-{c.To:yyyy-MM-dd}" : $"-{c.Today:yyyy-MM-dd}";
        return new ReportFile($"{c.Definition.Name}{period}.csv", ReportCsv.Write(table, c.Zone));
    }

    /// <summary>"InProgress" → "In progress", for statuses, categories and request types.</summary>
    public static string Words(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var words = WordBoundary().Replace(value.ToString(), " $1").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private async Task<Result<ReportContext>> ContextAsync(string name, ReportQuery query, bool export, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var definition = Array.Find(Definitions, d => d.Name == name);
        if (definition is null)
        {
            return CommonErrors.NotFound("Report");
        }

        if (IsEmpty(await ScopeAsync(definition, export: false, cancellationToken)))
        {
            return CommonErrors.Forbidden("view this report");
        }

        var scope = await ScopeAsync(definition, export, cancellationToken);
        if (IsEmpty(scope))
        {
            return CommonErrors.Forbidden("export this report");
        }

        if (query.ProjectId is { } projectId && !scope.Covers(projectId))
        {
            return CommonErrors.NotFound("Project");
        }

        var userId = currentUser.RequiredUserId;
        var timeZone = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZone).FirstAsync(cancellationToken);
        var zone = LocalTime.Zone(timeZone);
        var now = timeProvider.GetUtcNow();
        var today = LocalTime.DateOf(now, zone);
        var to = query.To ?? today;
        var from = query.From ?? to.AddDays(1 - DefaultDays);
        var errors = new FieldErrors();
        if (definition.UsesDateRange && from > to)
        {
            errors.Add("from", "The start date must be on or before the end date.");
        }
        else if (definition.UsesDateRange && to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            errors.Add("from", $"Choose a period of at most {MaxDays} days.");
        }

        var groupBy = definition.GroupBy.Count == 0 ? null : query.GroupBy ?? definition.GroupBy[0];
        if (groupBy is not null && !definition.GroupBy.Contains(groupBy))
        {
            errors.Add("groupBy", $"Group by one of: {string.Join(", ", definition.GroupBy)}.");
        }

        if (errors.HasErrors)
        {
            return errors.ToError();
        }

        return new ReportContext(
            definition, scope, userId, zone, zone.Id, from, to, LocalTime.StartOfDay(from, zone), LocalTime.StartOfDay(to.AddDays(1), zone),
            today, now, query.ProjectId, query.UserId, groupBy);
    }

    private async Task<ScopedProjects> ScopeAsync(ReportDefinition definition, bool export, CancellationToken cancellationToken)
    {
        var scope = (await projects.ProjectsWithAsync(PermissionCatalog.ReportsView, cancellationToken))
            .Intersect(await projects.ProjectsWithAsync(PermissionCatalog.TasksView, cancellationToken));
        if (definition.DataPermission is { } data)
        {
            scope = scope.Intersect(await projects.ProjectsWithAsync(data, cancellationToken));
        }

        return export ? scope.Intersect(await projects.ProjectsWithAsync(PermissionCatalog.ReportsExport, cancellationToken)) : scope;
    }

    private static bool IsEmpty(ScopedProjects scope) => !scope.Everywhere && scope.ProjectWide.Count == 0 && scope.OwnOnly.Count == 0;

    private Task<Result<ReportTable>> BuildAsync(ReportContext c, CancellationToken cancellationToken) =>
        c.Definition.Name switch
        {
            "time" => TimeAsync(c, cancellationToken),
            "allocated-vs-actual" => AllocatedVsActualAsync(c, cancellationToken),
            "overdue" => OverdueAsync(c, cancellationToken),
            "extensions" => ExtensionsAsync(c, cancellationToken),
            "revisions" => RevisionsAsync(c, cancellationToken),
            _ => DelaysAsync(c, cancellationToken),
        };

    /// <summary>Tasks in the report's scope (as TaskAccess.VisibleTasksAsync, for the intersected scope) and project filter.</summary>
    private IQueryable<TaskItem> Tasks(ReportContext c)
    {
        var tasks = db.Tasks.AsNoTracking();
        if (c.ProjectId is { } projectId)
        {
            tasks = tasks.Where(t => t.ProjectId == projectId);
        }

        if (c.Scope.Everywhere)
        {
            return tasks;
        }

        var me = c.UserId;
        var projectWide = c.Scope.ProjectWide;
        var ownOnly = c.Scope.OwnOnly;
        return tasks.Where(t => projectWide.Contains(t.ProjectId)
            || (ownOnly.Contains(t.ProjectId)
                && (t.AssigneeId == me || db.TaskAssignments.Any(a => a.TaskId == t.Id && a.ToUserId == me))));
    }

    /// <summary>FR-TIME-05/10: sessions clipped to the period (an active one counts up to now) plus adjustments made in it.</summary>
    private async Task<Result<ReportTable>> TimeAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, member) = (c.FromUtc, c.ToUtc, c.FilterUserId);
        var tasks = Tasks(c);
        var sessions = await (
                from s in db.WorkSessions.AsNoTracking()
                join t in tasks on s.TaskId equals t.Id
                where s.StartedAt < toUtc && (s.EndedAt == null || s.EndedAt > fromUtc) && (member == null || s.UserId == member)
                select new { s.TaskId, s.UserId, s.Revision, s.StartedAt, s.EndedAt })
            .Take(MaxSessions + 1)
            .ToListAsync(cancellationToken);
        if (sessions.Count > MaxSessions)
        {
            return new FieldErrors().Add("from", "There is too much time in this period to report at once. Choose a shorter period or a project.").ToError();
        }

        var adjustments = await (
                from a in db.TimeAdjustments.AsNoTracking()
                join t in tasks on a.TaskId equals t.Id
                where a.CreatedAt >= fromUtc && a.CreatedAt < toUtc && (member == null || a.UserId == member)
                select new { a.TaskId, a.UserId, a.Revision, a.CreatedAt, a.Minutes })
            .ToListAsync(cancellationToken);

        var byDay = c.GroupBy == "day";
        var entries = new List<TimeEntry>();
        foreach (var s in sessions)
        {
            var start = s.StartedAt > fromUtc ? s.StartedAt : fromUtc;
            var end = s.EndedAt ?? c.Now;
            end = end < toUtc ? end : toUtc;
            if (end <= start)
            {
                continue;
            }

            if (byDay)
            {
                entries.AddRange(LocalTime.SplitByDay(start, end, c.Zone).Select(d => new TimeEntry(s.TaskId, s.UserId, s.Revision, d.Day, d.Seconds)));
            }
            else
            {
                entries.Add(new TimeEntry(s.TaskId, s.UserId, s.Revision, default, (long)(end - start).TotalSeconds));
            }
        }

        entries.AddRange(adjustments.Select(a => new TimeEntry(a.TaskId, a.UserId, a.Revision, LocalTime.DateOf(a.CreatedAt, c.Zone), a.Minutes * 60L)));

        var lookup = await LookupAsync(entries.Select(e => e.TaskId), entries.Select(e => (Guid?)e.UserId), cancellationToken);
        IEnumerable<(object?[] Row, long Seconds, string Sort)> rows = c.GroupBy switch
        {
            "project" => entries.GroupBy(e => lookup.Tasks[e.TaskId].ProjectId)
                .Select(g => (new object?[] { lookup.Project(g.Key), g.Sum(e => e.Seconds) }, g.Sum(e => e.Seconds), string.Empty)),
            "member" => entries.GroupBy(e => e.UserId)
                .Select(g => (new object?[] { lookup.Name(g.Key), g.Sum(e => e.Seconds) }, g.Sum(e => e.Seconds), string.Empty)),
            "revision" => entries.GroupBy(e => (e.TaskId, e.Revision))
                .Select(g => (new object?[] { lookup.ProjectOf(g.Key.TaskId), lookup.Task(g.Key.TaskId), lookup.Tasks[g.Key.TaskId].Title, g.Key.Revision, g.Sum(e => e.Seconds) },
                    g.Sum(e => e.Seconds), string.Empty)),
            "day" => entries.GroupBy(e => (e.Day, e.UserId, e.TaskId))
                .Select(g => (new object?[]
                {
                    g.Key.Day, lookup.Name(g.Key.UserId), lookup.ProjectOf(g.Key.TaskId), lookup.Task(g.Key.TaskId), lookup.Tasks[g.Key.TaskId].Title, g.Sum(e => e.Seconds),
                }, g.Sum(e => e.Seconds), $"{g.Key.Day:yyyy-MM-dd}|{lookup.Name(g.Key.UserId)}|{lookup.Tasks[g.Key.TaskId].Key}")),
            _ => entries.GroupBy(e => e.TaskId)
                .Select(g => (new object?[] { lookup.ProjectOf(g.Key), lookup.Task(g.Key), lookup.Tasks[g.Key].Title, g.Sum(e => e.Seconds) }, g.Sum(e => e.Seconds), string.Empty)),
        };

        var ordered = byDay
            ? rows.Where(r => r.Seconds != 0).OrderBy(r => r.Sort, StringComparer.Ordinal)
            : rows.Where(r => r.Seconds != 0).OrderByDescending(r => r.Seconds);
        ReportColumn[] columns = c.GroupBy switch
        {
            "project" => [Text("project", "Project"), Duration("time", "Time")],
            "member" => [Text("member", "Member"), Duration("time", "Time")],
            "revision" => [Text("project", "Project"), TaskColumn(), Text("title", "Title"), Number("revision", "Revision"), Duration("time", "Time")],
            "day" => [new("date", "Date", ReportColumnKind.Date), Text("member", "Member"), Text("project", "Project"), TaskColumn(), Text("title", "Title"), Duration("time", "Time")],
            _ => [Text("project", "Project"), TaskColumn(), Text("title", "Title"), Duration("time", "Time")],
        };
        return Table(c, columns, ordered.Select(r => r.Row));
    }

    /// <summary>FR-TIME-08: tasks with an allocation that were open at some point in the period, most used first.</summary>
    private async Task<Result<ReportTable>> AllocatedVsActualAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, member) = (c.FromUtc, c.ToUtc, c.FilterUserId);
        var tasks = await Tasks(c)
            .Where(t => t.AllocatedMinutes != null && t.Status != TaskItemStatus.Cancelled && t.CreatedAt < toUtc && (t.ClosedAt == null || t.ClosedAt >= fromUtc))
            .Where(t => member == null || t.AssigneeId == member)
            .OrderBy(t => t.CreatedAt)
            .Take(MaxRows + 1)
            .Select(t => new { t.Id, t.AssigneeId, t.Status, Allocated = t.AllocatedMinutes!.Value })
            .ToListAsync(cancellationToken);
        var spent = await SpentTime.ForTasksAsync(db, tasks.Select(t => t.Id).ToList(), c.Now, cancellationToken);
        var lookup = await LookupAsync(tasks.Select(t => t.Id), tasks.Select(t => t.AssigneeId), cancellationToken);
        var rows = tasks
            .Select(t =>
            {
                var allocated = t.Allocated * 60L;
                var used = spent.GetValueOrDefault(t.Id);
                var percent = allocated > 0 ? (int)(used * 100 / allocated) : 0;
                return (Row: new object?[]
                {
                    lookup.ProjectOf(t.Id), lookup.Task(t.Id), lookup.Tasks[t.Id].Title, lookup.Name(t.AssigneeId), Words(t.Status), allocated, used,
                    Math.Max(0, used - allocated), percent,
                }, Percent: percent);
            })
            .OrderByDescending(r => r.Percent)
            .Select(r => r.Row);
        return Table(c,
            [
                Text("project", "Project"), TaskColumn(), Text("title", "Title"), Text("assignee", "Assignee"), Text("status", "Status"),
                Duration("allocated", "Allocated"), Duration("spent", "Spent"), Duration("overrun", "Overrun"), Number("percent", "% used"),
            ],
            rows,
            tasks.Count > MaxRows);
    }

    /// <summary>FR-DL-05: the stored Overdue flag (kept in the assignee's time zone by the due-date job), a snapshot.</summary>
    private async Task<Result<ReportTable>> OverdueAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var member = c.FilterUserId;
        var open = TaskWorkflow.OpenStatuses;
        var tasks = await Tasks(c)
            .Where(t => t.IsOverdue && open.Contains(t.Status) && (member == null || t.AssigneeId == member))
            .OrderBy(t => t.DueDate).ThenBy(t => t.Key)
            .Take(MaxRows + 1)
            .Select(t => new
            {
                t.Id,
                t.AssigneeId,
                t.Status,
                t.DueDate,
                ExtensionPending = db.ApprovalRequests.Any(r =>
                    r.TaskId == t.Id && r.Type == ApprovalRequestType.DeadlineExtension && r.Status == ApprovalRequestStatus.Pending),
            })
            .ToListAsync(cancellationToken);
        var lookup = await LookupAsync(tasks.Select(t => t.Id), tasks.Select(t => t.AssigneeId), cancellationToken);
        var rows = tasks.Select(t => new object?[]
        {
            lookup.ProjectOf(t.Id), lookup.Task(t.Id), lookup.Tasks[t.Id].Title, lookup.Name(t.AssigneeId), Words(t.Status), t.DueDate,
            t.DueDate is { } due ? Math.Max(0, c.Today.DayNumber - due.DayNumber) : null, t.ExtensionPending ? "Yes" : "No",
        });
        return Table(c,
            [
                Text("project", "Project"), TaskColumn(), Text("title", "Title"), Text("assignee", "Assignee"), Text("status", "Status"),
                new("dueDate", "Due date", ReportColumnKind.Date), Number("daysOverdue", "Days overdue"), Text("extensionPending", "Extension pending"),
            ],
            rows,
            tasks.Count > MaxRows);
    }

    private async Task<Result<ReportTable>> ExtensionsAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, member) = (c.FromUtc, c.ToUtc, c.FilterUserId);
        var requests = await (
                from r in db.ApprovalRequests.AsNoTracking()
                join t in Tasks(c) on r.TaskId equals t.Id
                where r.Type == ApprovalRequestType.DeadlineExtension && r.RequestedAt >= fromUtc && r.RequestedAt < toUtc
                    && (member == null || r.RequestedBy == member)
                orderby r.RequestedAt descending
                select new { r.TaskId, r.RequestedBy, r.RequestedAt, r.CurrentDueDate, r.ProposedDueDate, r.ApprovedDueDate, r.Status, r.DecidedBy, r.DecidedAt })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var lookup = await LookupAsync(requests.Select(r => r.TaskId), requests.SelectMany(r => new[] { (Guid?)r.RequestedBy, r.DecidedBy }), cancellationToken);
        var rows = requests.Select(r => new object?[]
        {
            lookup.ProjectOf(r.TaskId), lookup.Task(r.TaskId), lookup.Tasks[r.TaskId].Title, lookup.Name(r.RequestedBy), r.RequestedAt, r.CurrentDueDate,
            r.ProposedDueDate, r.ApprovedDueDate, Words(r.Status), lookup.Name(r.DecidedBy), r.DecidedAt,
        });
        return Table(c,
            [
                Text("project", "Project"), TaskColumn(), Text("title", "Title"), Text("requestedBy", "Requested by"),
                new("requestedAt", "Requested at", ReportColumnKind.DateTime), new("dueDate", "Due date then", ReportColumnKind.Date),
                new("proposedDueDate", "Proposed", ReportColumnKind.Date), new("approvedDueDate", "Approved", ReportColumnKind.Date), Text("status", "Status"),
                Text("decidedBy", "Decided by"), new("decidedAt", "Decided at", ReportColumnKind.DateTime),
            ],
            rows,
            requests.Count > MaxRows);
    }

    /// <summary>Revision requests (FR-RVW-02) decided in the period, counted per task.</summary>
    private async Task<Result<ReportTable>> RevisionsAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, member) = (c.FromUtc, c.ToUtc, c.FilterUserId);
        var counts = await (
                from r in db.ReviewSubmissions.AsNoTracking()
                join t in Tasks(c) on r.TaskId equals t.Id
                where r.Decision == ReviewDecision.RevisionRequested && r.ReviewedAt >= fromUtc && r.ReviewedAt < toUtc
                    && (member == null || r.SubmittedBy == member)
                group r by r.TaskId into g
                orderby g.Count() descending
                select new { TaskId = g.Key, Count = g.Count(), Last = g.Max(r => r.ReviewedAt) })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var ids = counts.Select(r => r.TaskId).ToList();
        var states = await db.Tasks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new { t.Id, t.AssigneeId, t.Status, t.CurrentRevision })
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var lookup = await LookupAsync(ids, states.Values.Select(t => t.AssigneeId), cancellationToken);
        var rows = counts.Select(r => new object?[]
        {
            lookup.ProjectOf(r.TaskId), lookup.Task(r.TaskId), lookup.Tasks[r.TaskId].Title, lookup.Name(states[r.TaskId].AssigneeId), r.Count,
            states[r.TaskId].CurrentRevision, Words(states[r.TaskId].Status), r.Last,
        });
        return Table(c,
            [
                Text("project", "Project"), TaskColumn(), Text("title", "Title"), Text("assignee", "Assignee"), Number("revisions", "Revisions requested"),
                Number("currentRevision", "Current revision"), Text("status", "Status"), new("lastRequestedAt", "Last requested", ReportColumnKind.DateTime),
            ],
            rows,
            counts.Count > MaxRows);
    }

    private async Task<Result<ReportTable>> DelaysAsync(ReportContext c, CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, member) = (c.FromUtc, c.ToUtc, c.FilterUserId);
        var reports = await (
                from d in db.DelayReports.AsNoTracking()
                join t in Tasks(c) on d.TaskId equals t.Id
                where d.ReportedAt >= fromUtc && d.ReportedAt < toUtc && (member == null || d.ReportedBy == member)
                orderby d.ReportedAt descending
                select new { d.TaskId, d.ReportedBy, d.ReportedAt, d.Category, d.Revision, d.Description })
            .Take(MaxRows + 1)
            .ToListAsync(cancellationToken);
        var lookup = await LookupAsync(reports.Select(r => r.TaskId), reports.Select(r => (Guid?)r.ReportedBy), cancellationToken);
        var rows = reports.Select(r => new object?[]
        {
            lookup.ProjectOf(r.TaskId), lookup.Task(r.TaskId), lookup.Tasks[r.TaskId].Title, lookup.Name(r.ReportedBy), r.ReportedAt, Words(r.Category),
            r.Revision, r.Description,
        });
        return Table(c,
            [
                Text("project", "Project"), TaskColumn(), Text("title", "Title"), Text("reportedBy", "Reported by"),
                new("reportedAt", "Reported at", ReportColumnKind.DateTime), Text("category", "Category"), Number("revision", "Revision"),
                Text("description", "Description"),
            ],
            rows,
            reports.Count > MaxRows);
    }

    private static ReportTable Table(ReportContext c, IReadOnlyList<ReportColumn> columns, IEnumerable<object?[]> rows, bool truncated = false)
    {
        var list = rows.Take(MaxRows + 1).ToList();
        truncated |= list.Count > MaxRows;
        return new ReportTable(
            c.Definition.Name,
            c.Definition.Title,
            c.Definition.UsesDateRange ? c.From : null,
            c.Definition.UsesDateRange ? c.To : null,
            c.ZoneId,
            columns,
            truncated ? list.Take(MaxRows).ToList() : list,
            truncated,
            c.Now);
    }

    private async Task<Lookup> LookupAsync(IEnumerable<Guid> taskIds, IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var tasks = taskIds.Distinct().ToList();
        var users = userIds.OfType<Guid>().Distinct().ToList();
        var taskInfo = await db.Tasks.AsNoTracking().Where(t => tasks.Contains(t.Id))
            .Select(t => new TaskInfo(t.Id, t.Key, t.Title, t.ProjectId))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var projectIds = taskInfo.Values.Select(t => t.ProjectId).Distinct().ToList();
        var projectNames = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => $"{p.Key} · {p.Name}", cancellationToken);
        var names = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        return new Lookup(taskInfo, projectNames, names);
    }

    private static ReportColumn Text(string key, string label) => new(key, label, ReportColumnKind.Text);

    private static ReportColumn Number(string key, string label) => new(key, label, ReportColumnKind.Number);

    private static ReportColumn Duration(string key, string label) => new(key, label, ReportColumnKind.Duration);

    private static ReportColumn TaskColumn() => new("task", "Task", ReportColumnKind.Task);

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex WordBoundary();

    private sealed record ReportDefinition(
        string Name, string Title, string Description, bool UsesDateRange, IReadOnlyList<string> GroupBy, string? DataPermission);

    private sealed record ReportContext(
        ReportDefinition Definition,
        ScopedProjects Scope,
        Guid UserId,
        TimeZoneInfo Zone,
        string ZoneId,
        DateOnly From,
        DateOnly To,
        DateTimeOffset FromUtc,
        DateTimeOffset ToUtc,
        DateOnly Today,
        DateTimeOffset Now,
        Guid? ProjectId,
        Guid? FilterUserId,
        string? GroupBy);

    private sealed record TimeEntry(Guid TaskId, Guid UserId, int Revision, DateOnly Day, long Seconds);

    private sealed record TaskInfo(Guid Id, string Key, string Title, Guid ProjectId);

    private sealed record Lookup(IReadOnlyDictionary<Guid, TaskInfo> Tasks, IReadOnlyDictionary<Guid, string> Projects, IReadOnlyDictionary<Guid, string> Names)
    {
        public ReportTaskRef Task(Guid taskId) => new(taskId, Tasks[taskId].Key);

        public string? Project(Guid projectId) => Projects.GetValueOrDefault(projectId);

        public string? ProjectOf(Guid taskId) => Project(Tasks[taskId].ProjectId);

        public string? Name(Guid? userId) => userId is { } id ? Names.GetValueOrDefault(id) : null;
    }
}

using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;

namespace TaskManagement.Application.Reporting;

/// <summary>Total time per task for many tasks at once, by the same rule as TimeService (FR-TIME-05).</summary>
internal static class SpentTime
{
    public static async Task<Dictionary<Guid, long>> ForTasksAsync(IAppDbContext db, IReadOnlyCollection<Guid> taskIds, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var totals = new Dictionary<Guid, long>();
        if (taskIds.Count == 0)
        {
            return totals;
        }

        var closed = await db.WorkSessions.AsNoTracking()
            .Where(s => taskIds.Contains(s.TaskId) && s.DurationSeconds != null)
            .GroupBy(s => s.TaskId)
            .Select(g => new { TaskId = g.Key, Seconds = g.Sum(s => (long?)s.DurationSeconds) ?? 0 })
            .ToListAsync(cancellationToken);
        var active = await db.WorkSessions.AsNoTracking()
            .Where(s => taskIds.Contains(s.TaskId) && s.EndedAt == null)
            .Select(s => new { s.TaskId, s.StartedAt })
            .ToListAsync(cancellationToken);
        var adjusted = await db.TimeAdjustments.AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId))
            .GroupBy(a => a.TaskId)
            .Select(g => new { TaskId = g.Key, Minutes = g.Sum(a => (long)a.Minutes) })
            .ToListAsync(cancellationToken);

        foreach (var c in closed)
        {
            totals[c.TaskId] = c.Seconds;
        }

        foreach (var a in active)
        {
            totals[a.TaskId] = totals.GetValueOrDefault(a.TaskId) + Math.Max(0, (long)(now - a.StartedAt).TotalSeconds);
        }

        foreach (var a in adjusted)
        {
            totals[a.TaskId] = totals.GetValueOrDefault(a.TaskId) + (a.Minutes * 60);
        }

        foreach (var id in totals.Keys.ToList())
        {
            totals[id] = Math.Max(0, totals[id]);
        }

        return totals;
    }
}

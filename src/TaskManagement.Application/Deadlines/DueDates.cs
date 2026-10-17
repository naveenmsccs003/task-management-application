using Microsoft.EntityFrameworkCore;
using TaskManagement.Application.Common;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Deadlines;

/// <summary>
/// "Today" for due dates is the assignee's local date (their profile time zone), so a task due on the 20th is not overdue
/// in Kolkata while it is still the 20th there. Unassigned tasks and unknown zones use UTC.
/// </summary>
public static class DueDates
{
    public static DateOnly TodayFor(string? timeZone, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone))
        {
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        }

        return DateOnly.FromDateTime(now.UtcDateTime);
    }

    public static async Task<DateOnly> TodayForAssigneeAsync(IAppDbContext db, TaskItem task, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(task);
        var zone = task.AssigneeId is { } assignee
            ? await db.Users.AsNoTracking().Where(u => u.Id == assignee).Select(u => u.TimeZone).FirstOrDefaultAsync(cancellationToken)
            : null;
        return TodayFor(zone, now);
    }
}

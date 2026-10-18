namespace TaskManagement.Application.Reporting;

/// <summary>
/// Calendar arithmetic in a member's time zone (FR-TIME-10: reports use the viewer's time zone). Weeks start on Monday.
/// </summary>
public static class LocalTime
{
    /// <summary>The member's zone, or UTC when the stored id is unknown on this server.</summary>
    public static TimeZoneInfo Zone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateOnly DateOf(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }

    /// <summary>The instant the local day begins. If midnight does not exist (a daylight-saving gap), the first valid minute after it.</summary>
    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(15);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public static DateOnly StartOfWeek(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary>Splits [start, end) at local midnights; days with no time are left out.</summary>
    public static IEnumerable<(DateOnly Day, long Seconds)> SplitByDay(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var cursor = start;
        while (cursor < end)
        {
            var day = DateOf(cursor, zone);
            var next = StartOfDay(day.AddDays(1), zone);
            var until = next < end ? next : end;
            var seconds = (long)(until - cursor).TotalSeconds;
            if (seconds > 0)
            {
                yield return (day, seconds);
            }

            cursor = until;
        }
    }
}

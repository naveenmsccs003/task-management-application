using TaskManagement.Application.Deadlines;

namespace TaskManagement.Application.Tests.Deadlines;

public sealed class DueDatesTests
{
    // 2026-10-20 20:30 UTC: already the 21st in Kolkata (UTC+5:30) and in Kiritimati (UTC+14), still the 20th in New York.
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 20, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Asia/Kolkata", "2026-10-21")]
    [InlineData("Pacific/Kiritimati", "2026-10-21")]
    [InlineData("America/New_York", "2026-10-20")]
    [InlineData("UTC", "2026-10-20")]
    [InlineData(null, "2026-10-20")]
    [InlineData("Not/AZone", "2026-10-20")]
    public void Today_is_the_assignees_local_date_with_UTC_as_the_fallback(string? timeZone, string expected) =>
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DueDates.TodayFor(timeZone, Now));
}

using System.Text;
using TaskManagement.Application.Access;
using TaskManagement.Application.Reporting;
using TaskManagement.Domain.Tasks;

namespace TaskManagement.Application.Tests.Reporting;

public sealed class ReportingTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Theory]
    [InlineData("=SUM(A1:A2)", "'=SUM(A1:A2)")]
    [InlineData("+1", "'+1")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@risk", "'@risk")]
    [InlineData("\tTabbed", "'\tTabbed")]
    [InlineData("=HYPERLINK(\"http://x\",\"y\")", "\"'=HYPERLINK(\"\"http://x\"\",\"\"y\"\")\"")]
    [InlineData("Plain, with comma", "\"Plain, with comma\"")]
    [InlineData("Line\nbreak", "\"Line\nbreak\"")]
    [InlineData("Normal", "Normal")]
    [InlineData("", "")]
    public void S20_text_cells_cannot_become_formulas(string input, string expected)
    {
        Assert.Equal(expected, ReportCsv.EscapeText(input));
    }

    [Fact]
    public void Csv_writes_hours_dates_and_times_in_the_viewers_zone_with_a_byte_order_mark()
    {
        var table = new ReportTable(
            "time", "Time", null, null, Berlin.Id,
            [
                new("task", "Task", ReportColumnKind.Task), new("date", "Date", ReportColumnKind.Date), new("at", "At", ReportColumnKind.DateTime),
                new("time", "Time", ReportColumnKind.Duration), new("n", "Count", ReportColumnKind.Number), new("text", "Note", ReportColumnKind.Text),
            ],
            [
                [new ReportTaskRef(Guid.NewGuid(), "WEB-7"), new DateOnly(2026, 10, 18), new DateTimeOffset(2026, 10, 18, 22, 30, 0, TimeSpan.Zero), 5400L, -3, null],
            ],
            false,
            DateTimeOffset.UnixEpoch);

        var bytes = ReportCsv.Write(table, Berlin);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal(
            "Task,Date,At,Time (hours),Count,Note\r\nWEB-7,2026-10-18,2026-10-19 00:30,1.50,-3,\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void FR_TIME_10_time_is_split_at_local_midnight()
    {
        // 21:00–23:30 UTC on 17 October is 23:00–01:30 in Berlin (summer time, UTC+2).
        var parts = LocalTime.SplitByDay(
            new DateTimeOffset(2026, 10, 17, 21, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 23, 30, 0, TimeSpan.Zero), Berlin).ToList();

        Assert.Equal([(new DateOnly(2026, 10, 17), 3600L), (new DateOnly(2026, 10, 18), 5400L)], parts);
    }

    [Fact]
    public void A_day_with_a_daylight_saving_change_has_its_real_length()
    {
        // Berlin leaves summer time on 25 October 2026: that day has 25 hours.
        var start = LocalTime.StartOfDay(new DateOnly(2026, 10, 25), Berlin);
        var end = LocalTime.StartOfDay(new DateOnly(2026, 10, 26), Berlin);

        Assert.Equal(new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(TimeSpan.FromHours(25), end - start);
        Assert.Equal([(new DateOnly(2026, 10, 25), 25 * 3600L)], LocalTime.SplitByDay(start, end, Berlin));
    }

    [Theory]
    [InlineData("2026-10-18", "2026-10-12")] // Sunday
    [InlineData("2026-10-19", "2026-10-19")] // Monday
    [InlineData("2026-10-21", "2026-10-19")]
    public void Weeks_start_on_monday(string date, string monday)
    {
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture),
            LocalTime.StartOfWeek(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Unknown_time_zones_fall_back_to_utc()
    {
        Assert.Equal(TimeZoneInfo.Utc, LocalTime.Zone("Mars/Olympus_Mons"));
        Assert.Equal(TimeZoneInfo.Utc, LocalTime.Zone(null));
    }

    [Fact]
    public void FR_RPT_06_scopes_intersect_per_project()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();
        var reports = new ScopedProjects(false, [a, b], [c]);
        var time = new ScopedProjects(false, [a], [b, d]);

        var both = reports.Intersect(time);

        Assert.False(both.Everywhere);
        Assert.Equal([a], both.ProjectWide); // project-wide only where both are
        Assert.Equal([b], both.OwnOnly); // Own where either is only Own; c and d are in one scope only
        Assert.Same(time, new ScopedProjects(true, [], []).Intersect(time));
        Assert.True(both.Covers(b));
        Assert.False(both.Covers(c));
    }

    [Theory]
    [InlineData(TaskItemStatus.InProgress, "In progress")]
    [InlineData(TaskItemStatus.RevisionRequested, "Revision requested")]
    [InlineData(TaskItemStatus.Draft, "Draft")]
    [InlineData(DelayCategory.BlockedByDependency, "Blocked by dependency")]
    public void Enum_values_read_as_words(object value, string expected)
    {
        Assert.Equal(expected, ReportService.Words((Enum)value));
    }
}

using System.Globalization;
using System.Text;

namespace TaskManagement.Application.Reporting;

/// <summary>How a report column's cells are shown and exported.</summary>
public enum ReportColumnKind
{
    Text,
    Number,

    /// <summary>Seconds; shown as hours and minutes, exported as decimal hours.</summary>
    Duration,

    /// <summary>A calendar date (<see cref="DateOnly"/>).</summary>
    Date,

    /// <summary>An instant, shown and exported in the viewer's time zone.</summary>
    DateTime,

    /// <summary>A <see cref="ReportTaskRef"/>: shown as a link, exported as the task key.</summary>
    Task,
}

public sealed record ReportColumn(string Key, string Label, ReportColumnKind Kind);

public sealed record ReportTaskRef(Guid Id, string Key);

/// <summary>
/// One report's result (FR-RPT-05): columns plus rows of cells in column order. The same table is returned as JSON and
/// written as CSV, so both always show the same data.
/// </summary>
public sealed record ReportTable(
    string Report,
    string Title,
    DateOnly? From,
    DateOnly? To,
    string TimeZone,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    bool Truncated,
    DateTimeOffset GeneratedAt);

/// <summary>
/// RFC 4180 CSV with a UTF-8 byte order mark (so spreadsheets detect the encoding). Text cells that a spreadsheet would
/// read as a formula — starting with = + - @, tab or carriage return — get a leading apostrophe (security.md S20,
/// FR-RPT-07, edge case 38). Numbers and durations are written by us, never from user input, and are not escaped.
/// </summary>
public static class ReportCsv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static byte[] Write(ReportTable table, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(zone);
        var csv = new StringBuilder();
        AppendLine(csv, table.Columns.Select(c => EscapeText(c.Kind == ReportColumnKind.Duration ? $"{c.Label} (hours)" : c.Label)));
        foreach (var row in table.Rows)
        {
            AppendLine(csv, table.Columns.Select((column, i) => Cell(column.Kind, row[i], zone)));
        }

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(csv.ToString());
        return [.. preamble, .. body];
    }

    /// <summary>Neutralizes formulas and quotes the cell when needed.</summary>
    public static string EscapeText(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && FormulaStarts.Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    private static string Cell(ReportColumnKind kind, object? value, TimeZoneInfo zone) =>
        (kind, value) switch
        {
            (_, null) => string.Empty,
            (ReportColumnKind.Duration, long seconds) => (seconds / 3600m).ToString("0.00", CultureInfo.InvariantCulture),
            (ReportColumnKind.Number, IFormattable number) => number.ToString(null, CultureInfo.InvariantCulture),
            (ReportColumnKind.Date, DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            (ReportColumnKind.DateTime, DateTimeOffset instant) =>
                TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            (ReportColumnKind.Task, ReportTaskRef task) => EscapeText(task.Key),
            _ => EscapeText(Convert.ToString(value, CultureInfo.InvariantCulture)),
        };

    private static void AppendLine(StringBuilder csv, IEnumerable<string> cells)
    {
        csv.AppendJoin(',', cells);
        csv.Append("\r\n");
    }
}

using System.Globalization;
using System.Reflection;
using System.Text;
using RigMetrics.Core.Models;

namespace RigMetrics.Core.Export;

/// <summary>A column in the results table/exports: a display header bound to a
/// <see cref="SessionSummaryRow"/> property, with an optional number format.</summary>
public sealed record ResultsColumn(string Header, string Property, string? Format);

/// <summary>
/// Turns sessions into a spreadsheet-friendly results table. One column set is
/// shared by the in-app grid, the CSV export, the running master file, and the
/// Excel export, so they never drift apart.
/// </summary>
public static class ResultsExporter
{
    public static readonly IReadOnlyList<ResultsColumn> Columns = new[]
    {
        new ResultsColumn("Date", nameof(SessionSummaryRow.Date), null),
        new ResultsColumn("Label", nameof(SessionSummaryRow.Label), null),
        new ResultsColumn("Avg FPS", nameof(SessionSummaryRow.AvgFps), "0.0"),
        new ResultsColumn("1% Low", nameof(SessionSummaryRow.Low1Fps), "0.0"),
        new ResultsColumn("0.1% Low", nameof(SessionSummaryRow.Low01Fps), "0.0"),
        new ResultsColumn("Min FPS", nameof(SessionSummaryRow.MinFps), "0.0"),
        new ResultsColumn("Max FPS", nameof(SessionSummaryRow.MaxFps), "0.0"),
        new ResultsColumn("Median FPS", nameof(SessionSummaryRow.MedianFps), "0.0"),
        new ResultsColumn("Avg Frame ms", nameof(SessionSummaryRow.AvgFrameTimeMs), "0.00"),
        new ResultsColumn("Frame ms StdDev", nameof(SessionSummaryRow.FrameTimeStdDevMs), "0.00"),
        new ResultsColumn("Stutters", nameof(SessionSummaryRow.Stutters), "0"),
        new ResultsColumn("Avg CPU %", nameof(SessionSummaryRow.AvgCpuLoad), "0"),
        new ResultsColumn("Max CPU C", nameof(SessionSummaryRow.MaxCpuTempC), "0"),
        new ResultsColumn("Avg GPU %", nameof(SessionSummaryRow.AvgGpuLoad), "0"),
        new ResultsColumn("Max GPU C", nameof(SessionSummaryRow.MaxGpuTempC), "0"),
        new ResultsColumn("Avg VRAM MB", nameof(SessionSummaryRow.AvgGpuMemMb), "0"),
        new ResultsColumn("Avg RAM GB", nameof(SessionSummaryRow.AvgRamGb), "0.0"),
        new ResultsColumn("Max RAM %", nameof(SessionSummaryRow.MaxRamLoad), "0"),
        new ResultsColumn("Duration s", nameof(SessionSummaryRow.DurationSec), "0"),
        new ResultsColumn("Frames", nameof(SessionSummaryRow.FrameCount), "0"),
        new ResultsColumn("Notes", nameof(SessionSummaryRow.Notes), null),
    };

    private static readonly Dictionary<string, PropertyInfo> Properties =
        typeof(SessionSummaryRow).GetProperties().ToDictionary(p => p.Name);

    public static object? GetValue(SessionSummaryRow row, ResultsColumn column)
        => Properties[column.Property].GetValue(row);

    /// <summary>Value formatted for a spreadsheet cell (invariant culture, no CSV quoting).</summary>
    public static string FormatValue(object? value, string? format)
    {
        switch (value)
        {
            case null:
                return "";
            case double d:
                return d.ToString(format ?? "0.###", CultureInfo.InvariantCulture);
            case int i:
                return i.ToString(format ?? "0", CultureInfo.InvariantCulture);
            default:
                return value.ToString() ?? "";
        }
    }

    /// <summary>Writes a consolidated CSV of all the given sessions (Google Sheets-ready).</summary>
    public static void ExportCsv(IEnumerable<SessionSummaryRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", Columns.Select(c => CsvEscape(c.Header))));
        foreach (var row in rows)
            sb.AppendLine(RowToCsv(row));
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Appends a single session to the running master CSV, writing the header
    /// first if the file doesn't exist yet. This is the always-up-to-date log.
    /// </summary>
    public static void AppendToMaster(SessionSummaryRow row, string path)
    {
        bool needHeader = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true);
        if (needHeader)
            writer.WriteLine(string.Join(",", Columns.Select(c => CsvEscape(c.Header))));
        writer.WriteLine(RowToCsv(row));
    }

    private static string RowToCsv(SessionSummaryRow row)
        => string.Join(",", Columns.Select(c => CsvEscape(FormatValue(GetValue(row, c), c.Format))));

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

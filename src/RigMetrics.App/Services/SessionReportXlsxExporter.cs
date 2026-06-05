using ClosedXML.Excel;
using RigMetrics.Core.Export;
using RigMetrics.Core.Models;

namespace RigMetrics.App.Services;

/// <summary>
/// Writes a single per-session report workbook combining FPS and sensors:
///   - "Summary": whole-test FPS averages and sensor (load/temp) averages.
///   - "FPS over time": adaptive 5-min / 30-s buckets (fine while frames drop).
///   - "Sensors over time": adaptive 5-min / 30-s buckets (fine while temps are dangerous).
/// </summary>
public static class SessionReportXlsxExporter
{
    public static void Export(SessionRecording session, ReportOptions options, string path)
    {
        using var workbook = new XLWorkbook();

        WriteSummary(workbook, session);
        WriteFpsOverTime(workbook, session, options);
        WriteSensorsOverTime(workbook, session, options);

        workbook.SaveAs(path);
    }

    private static void WriteSummary(XLWorkbook wb, SessionRecording session)
    {
        var ws = wb.Worksheets.Add("Summary");
        var s = session.Stats;
        int dropped = SessionReportBuilder.CountDroppedFrames(session.Frames);
        var sensors = SessionReportBuilder.BuildSensorAverages(session.Sensors);

        ws.Cell(1, 1).Value = "Session";
        ws.Cell(1, 2).Value = string.IsNullOrWhiteSpace(session.Label) ? "(unlabeled)" : session.Label;
        ws.Range(1, 1, 1, 2).Style.Font.Bold = true;

        int r = 3;
        ws.Cell(r, 1).Value = "FPS — whole-test average";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        r = Pair(ws, r, "Avg FPS", s?.AvgFps, "0.0");
        r = Pair(ws, r, "1% Low FPS", s?.Percentile1LowFps, "0.0");
        r = Pair(ws, r, "0.1% Low FPS", s?.Percentile01LowFps, "0.0");
        r = Pair(ws, r, "Min FPS", s?.MinFps, "0.0");
        r = Pair(ws, r, "Max FPS", s?.MaxFps, "0.0");
        r = Pair(ws, r, "Median FPS", s?.MedianFps, "0.0");
        r = Pair(ws, r, "Avg Frame Time (ms)", s?.AvgFrameTimeMs, "0.00");
        r = Pair(ws, r, "Frame Time StdDev (ms)", s?.FrameTimeStdDevMs, "0.00");
        r = Pair(ws, r, "Stutters", s?.StutterCount, "0");
        r = Pair(ws, r, "Dropped Frames", dropped, "0");
        r = Pair(ws, r, "Frames", s?.FrameCount, "0");
        r = Pair(ws, r, "Duration (s)", s?.DurationSeconds, "0");

        r += 1;
        ws.Cell(r, 1).Value = "Sensors — whole-test average";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        r = Pair(ws, r, "Avg CPU Load (%)", sensors.AvgCpuLoad, "0");
        r = Pair(ws, r, "Avg GPU Load (%)", sensors.AvgGpuLoad, "0");
        r = Pair(ws, r, "Avg RAM Load (%)", sensors.AvgRamLoad, "0");
        r = Pair(ws, r, "Avg CPU Temp (C)", sensors.AvgCpuTemp, "0");
        r = Pair(ws, r, "Max CPU Temp (C)", sensors.MaxCpuTemp, "0");
        r = Pair(ws, r, "Avg GPU Temp (C)", sensors.AvgGpuTemp, "0");
        r = Pair(ws, r, "Max GPU Temp (C)", sensors.MaxGpuTemp, "0");

        AutoFit(ws);
    }

    private static void WriteFpsOverTime(XLWorkbook wb, SessionRecording session, ReportOptions options)
    {
        var ws = wb.Worksheets.Add("FPS over time");
        var buckets = SessionReportBuilder.BuildFpsBuckets(session.Frames, options);

        string[] headers = { "From", "To", "Length (s)", "Resolution", "Avg FPS", "Min FPS", "Dropped Frames", "Frames" };
        WriteHeader(ws, headers);

        int row = 2;
        foreach (var b in buckets)
        {
            ws.Cell(row, 1).Value = Clock(b.FromSec);
            ws.Cell(row, 2).Value = Clock(b.ToSec);
            ws.Cell(row, 3).Value = Math.Round(b.ToSec - b.FromSec, 0);
            ws.Cell(row, 4).Value = b.Fine ? "30s" : "5min";
            Num(ws.Cell(row, 5), b.AvgFps, "0.0");
            Num(ws.Cell(row, 6), b.MinFps, "0.0");
            ws.Cell(row, 7).Value = b.DroppedFrames;
            ws.Cell(row, 8).Value = b.FrameCount;
            if (b.DroppedFrames > 0)
                ws.Cell(row, 7).Style.Fill.BackgroundColor = XLColor.LightSalmon;
            row++;
        }

        ws.SheetView.FreezeRows(1);
        AutoFit(ws);
    }

    private static void WriteSensorsOverTime(XLWorkbook wb, SessionRecording session, ReportOptions options)
    {
        var ws = wb.Worksheets.Add("Sensors over time");
        var buckets = SessionReportBuilder.BuildSensorBuckets(session.Sensors, options);

        string[] headers =
        {
            "From", "To", "Length (s)", "Resolution",
            "Avg CPU %", "Avg GPU %", "Avg RAM %",
            "Avg CPU C", "Max CPU C", "Avg GPU C", "Max GPU C",
        };
        WriteHeader(ws, headers);

        int row = 2;
        foreach (var b in buckets)
        {
            ws.Cell(row, 1).Value = Clock(b.FromSec);
            ws.Cell(row, 2).Value = Clock(b.ToSec);
            ws.Cell(row, 3).Value = Math.Round(b.ToSec - b.FromSec, 0);
            ws.Cell(row, 4).Value = b.Fine ? "30s" : "5min";
            Num(ws.Cell(row, 5), b.AvgCpuLoad, "0");
            Num(ws.Cell(row, 6), b.AvgGpuLoad, "0");
            Num(ws.Cell(row, 7), b.AvgRamLoad, "0");
            Num(ws.Cell(row, 8), b.AvgCpuTemp, "0");
            Num(ws.Cell(row, 9), b.MaxCpuTemp, "0");
            Num(ws.Cell(row, 10), b.AvgGpuTemp, "0");
            Num(ws.Cell(row, 11), b.MaxGpuTemp, "0");
            if (b.Fine)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.LightSalmon;
            row++;
        }

        ws.SheetView.FreezeRows(1);
        AutoFit(ws);
    }

    // ---- helpers ----

    private static void WriteHeader(IXLWorksheet ws, string[] headers)
    {
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
        }
    }

    private static int Pair(IXLWorksheet ws, int row, string label, double? value, string format)
    {
        ws.Cell(row, 1).Value = label;
        Num(ws.Cell(row, 2), value, format);
        return row + 1;
    }

    private static int Pair(IXLWorksheet ws, int row, string label, int? value, string format)
        => Pair(ws, row, label, value.HasValue ? (double?)value.Value : null, format);

    private static void Num(IXLCell cell, double? value, string format)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = format;
        }
    }

    private static string Clock(double seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");

    private static void AutoFit(IXLWorksheet ws)
    {
        try { ws.Columns().AdjustToContents(); }
        catch { /* cosmetic only */ }
    }
}

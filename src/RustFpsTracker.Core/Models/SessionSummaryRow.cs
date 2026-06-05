using System.Globalization;

namespace RustFpsTracker.Core.Models;

/// <summary>
/// A flat, one-row-per-session summary of the key results. This is what the
/// in-app Results table shows and what gets written to the master spreadsheet
/// / CSV / Excel exports.
/// </summary>
public sealed class SessionSummaryRow
{
    public string Date { get; init; } = "";
    public string Label { get; init; } = "";

    public double DurationSec { get; init; }
    public int FrameCount { get; init; }

    public double AvgFps { get; init; }
    public double Low1Fps { get; init; }
    public double Low01Fps { get; init; }
    public double MinFps { get; init; }
    public double MaxFps { get; init; }
    public double MedianFps { get; init; }

    public double AvgFrameTimeMs { get; init; }
    public double FrameTimeStdDevMs { get; init; }
    public int Stutters { get; init; }

    public double? AvgCpuLoad { get; init; }
    public double? MaxCpuTempC { get; init; }
    public double? AvgGpuLoad { get; init; }
    public double? MaxGpuTempC { get; init; }
    public double? AvgGpuMemMb { get; init; }
    public double? AvgRamGb { get; init; }
    public double? MaxRamLoad { get; init; }

    public string Notes { get; init; } = "";

    public static SessionSummaryRow From(SessionRecording session)
    {
        var s = session.Stats;
        var localDate = session.StartedUtc.ToLocalTime();

        return new SessionSummaryRow
        {
            Date = localDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Label = session.Label,
            DurationSec = Round(s?.DurationSeconds ?? 0, 0),
            FrameCount = s?.FrameCount ?? 0,
            AvgFps = Round(s?.AvgFps ?? 0, 1),
            Low1Fps = Round(s?.Percentile1LowFps ?? 0, 1),
            Low01Fps = Round(s?.Percentile01LowFps ?? 0, 1),
            MinFps = Round(s?.MinFps ?? 0, 1),
            MaxFps = Round(s?.MaxFps ?? 0, 1),
            MedianFps = Round(s?.MedianFps ?? 0, 1),
            AvgFrameTimeMs = Round(s?.AvgFrameTimeMs ?? 0, 2),
            FrameTimeStdDevMs = Round(s?.FrameTimeStdDevMs ?? 0, 2),
            Stutters = s?.StutterCount ?? 0,
            AvgCpuLoad = Round(s?.AvgCpuLoadPercent, 0),
            MaxCpuTempC = Round(s?.MaxCpuTempC, 0),
            AvgGpuLoad = Round(s?.AvgGpuLoadPercent, 0),
            MaxGpuTempC = Round(s?.MaxGpuTempC, 0),
            AvgGpuMemMb = Round(s?.AvgGpuMemUsedMb, 0),
            AvgRamGb = Round(s?.AvgRamUsedGb, 1),
            MaxRamLoad = Round(s?.MaxRamLoadPercent, 0),
            Notes = session.Notes,
        };
    }

    private static double Round(double value, int digits) => Math.Round(value, digits);
    private static double? Round(double? value, int digits)
        => value.HasValue ? Math.Round(value.Value, digits) : null;
}

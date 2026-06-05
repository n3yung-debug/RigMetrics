using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RustFpsTracker.App;

/// <summary>
/// User-editable settings, loaded from appsettings.json next to the exe.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Path to PresentMon.exe. Relative paths resolve next to this app.</summary>
    public string PresentMonPath { get; set; } = "PresentMon.exe";

    /// <summary>The game's process name. Rust's client process is "RustClient.exe".</summary>
    public string GameProcessName { get; set; } = "RustClient.exe";

    /// <summary>Extra command-line args appended to the PresentMon invocation.</summary>
    public string[] PresentMonExtraArgs { get; set; } = Array.Empty<string>();

    /// <summary>How often hardware sensors are polled, in milliseconds.</summary>
    public int SensorPollMs { get; set; } = 1000;

    /// <summary>A frame is a "stutter" when its frame time exceeds this multiple of the median.</summary>
    public double StutterMultiplier { get; set; } = 2.0;

    /// <summary>Rolling window (seconds) used for the live on-screen statistics.</summary>
    public int LiveWindowSeconds { get; set; } = 60;

    // ----- Per-session report (combined FPS + sensors export) -----

    /// <summary>Normal report bucket size, in minutes.</summary>
    public double ReportCoarseMinutes { get; set; } = 5;

    /// <summary>Fine report bucket size, in seconds (used during drops / dangerous temps).</summary>
    public double ReportFineSeconds { get; set; } = 30;

    /// <summary>After a dropped frame, stay at fine resolution until this many seconds with no drops.</summary>
    public double ReportDropHoldSeconds { get; set; } = 60;

    /// <summary>CPU temperature (C) considered dangerous (switches sensors to fine resolution).</summary>
    public double ReportCpuDangerC { get; set; } = 90;

    /// <summary>GPU temperature (C) considered dangerous.</summary>
    public double ReportGpuDangerC { get; set; } = 85;

    /// <summary>How many degrees below the danger threshold counts as "stabilized".</summary>
    public double ReportStabilizeMarginC { get; set; } = 5;

    public RustFpsTracker.Core.Export.ReportOptions BuildReportOptions() => new()
    {
        CoarseSeconds = ReportCoarseMinutes * 60,
        FineSeconds = ReportFineSeconds,
        DropHoldSeconds = ReportDropHoldSeconds,
        CpuDangerC = ReportCpuDangerC,
        GpuDangerC = ReportGpuDangerC,
        CpuStableC = ReportCpuDangerC - ReportStabilizeMarginC,
        GpuStableC = ReportGpuDangerC - ReportStabilizeMarginC,
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppConfig Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options);
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // Fall back to defaults on any parse error.
        }
        return new AppConfig();
    }

    /// <summary>Resolves PresentMonPath to an absolute path next to the app if it is relative.</summary>
    public string ResolvePresentMonPath(string baseDirectory)
        => Path.IsPathRooted(PresentMonPath)
            ? PresentMonPath
            : Path.Combine(baseDirectory, PresentMonPath);
}

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

namespace RigMetrics.Core.Models;

/// <summary>
/// Aggregated statistics for a recording session. The "low" metrics
/// (1% / 0.1%) are what reveal stutter and consistency &mdash; they matter
/// more than average FPS when comparing settings.
/// </summary>
public sealed record SessionStats
{
    public int FrameCount { get; init; }
    public double DurationSeconds { get; init; }

    // FPS
    public double AvgFps { get; init; }
    public double MinFps { get; init; }
    public double MaxFps { get; init; }
    public double MedianFps { get; init; }

    /// <summary>1% low FPS (1st percentile) &mdash; 99% of frames are faster than this.</summary>
    public double Percentile1LowFps { get; init; }

    /// <summary>0.1% low FPS (0.1th percentile) &mdash; the worst-case smoothness.</summary>
    public double Percentile01LowFps { get; init; }

    // Frame time / smoothness
    public double AvgFrameTimeMs { get; init; }
    public double FrameTimeStdDevMs { get; init; }

    /// <summary>Number of frames whose frame time exceeded the stutter threshold.</summary>
    public int StutterCount { get; init; }

    // Hardware aggregates (nullable when a sensor wasn't available)
    public double? AvgCpuLoadPercent { get; init; }
    public double? MaxCpuTempC { get; init; }
    public double? AvgGpuLoadPercent { get; init; }
    public double? MaxGpuTempC { get; init; }
    public double? AvgGpuMemUsedMb { get; init; }
    public double? AvgRamUsedGb { get; init; }
    public double? MaxRamLoadPercent { get; init; }
}

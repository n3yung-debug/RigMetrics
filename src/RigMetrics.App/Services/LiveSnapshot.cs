using RigMetrics.Core.Models;

namespace RigMetrics.App.Services;

/// <summary>
/// A lightweight view of the current state for the live dashboard, computed
/// over a rolling time window. Series arrays are downsampled-friendly copies
/// safe to read on the UI thread.
/// </summary>
public sealed class LiveSnapshot
{
    public bool IsTracking { get; init; }
    public TimeSpan Elapsed { get; init; }

    public double CurrentFps { get; init; }
    public double AvgFps { get; init; }
    public double OnePercentLowFps { get; init; }
    public double PointOnePercentLowFps { get; init; }
    public double FrameTimeMs { get; init; }
    public int FrameCount { get; init; }

    public SensorSample? Sensors { get; init; }

    /// <summary>Recent per-frame FPS values (oldest first) for the FPS sparkline.</summary>
    public double[] FpsSeries { get; init; } = Array.Empty<double>();

    /// <summary>Recent per-frame frame times in ms (oldest first).</summary>
    public double[] FrameTimeSeries { get; init; } = Array.Empty<double>();

    /// <summary>Recent CPU/GPU temperatures (oldest first) for the temp sparklines.</summary>
    public double[] CpuTempSeries { get; init; } = Array.Empty<double>();
    public double[] GpuTempSeries { get; init; } = Array.Empty<double>();
}

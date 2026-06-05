namespace RustFpsTracker.Core.Models;

/// <summary>
/// A single frame "present" captured from PresentMon.
/// FrameTimeMs is the time between this present and the previous one
/// (PresentMon's msBetweenPresents), which is the basis for FPS.
/// </summary>
public sealed record FrameSample
{
    /// <summary>Timestamp of the present, in seconds since capture start (PresentMon TimeInSeconds).</summary>
    public double TimeInSeconds { get; init; }

    /// <summary>Time since the previous present, in milliseconds (frame time).</summary>
    public double FrameTimeMs { get; init; }

    /// <summary>True if this present was dropped (not shown on screen).</summary>
    public bool Dropped { get; init; }

    /// <summary>Instantaneous FPS for this frame.</summary>
    public double Fps => FrameTimeMs > 0 ? 1000.0 / FrameTimeMs : 0;
}

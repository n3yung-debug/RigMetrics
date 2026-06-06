namespace RigMetrics.Core.Models;

/// <summary>
/// A complete capture session: the raw per-frame and per-second samples,
/// plus a user label/notes describing the settings under test, and the
/// computed statistics.
/// </summary>
public sealed class SessionRecording
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Short label, e.g. "Shadows Low, AA Off". Used to compare runs.</summary>
    public string Label { get; set; } = "";

    /// <summary>Free-form notes about the settings/hardware for this run.</summary>
    public string Notes { get; set; } = "";

    /// <summary>Process that was tracked for this session (e.g. "cs2.exe").</summary>
    public string GameProcessName { get; set; } = "";

    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }

    public List<FrameSample> Frames { get; init; } = new();
    public List<SensorSample> Sensors { get; init; } = new();

    public SessionStats? Stats { get; set; }
}

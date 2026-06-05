namespace RigMetrics.Core.Models;

/// <summary>
/// A point-in-time snapshot of hardware sensors. All values are nullable
/// because not every machine exposes every sensor (and some require admin).
/// </summary>
public sealed record SensorSample
{
    public DateTime TimestampUtc { get; init; }

    public double? CpuLoadPercent { get; init; }
    public double? CpuTempC { get; init; }

    public double? GpuLoadPercent { get; init; }
    public double? GpuTempC { get; init; }
    public double? GpuMemUsedMb { get; init; }

    public double? RamUsedGb { get; init; }
    public double? RamLoadPercent { get; init; }
}

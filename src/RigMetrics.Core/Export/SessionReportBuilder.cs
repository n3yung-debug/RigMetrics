using RigMetrics.Core.Models;

namespace RigMetrics.Core.Export;

/// <summary>Tuning for the per-session report's adaptive time bucketing.</summary>
public sealed record ReportOptions
{
    /// <summary>Normal bucket size, in seconds (default 5 minutes).</summary>
    public double CoarseSeconds { get; init; } = 300;

    /// <summary>Fine bucket size used during drops / dangerous temps (default 30s).</summary>
    public double FineSeconds { get; init; } = 30;

    /// <summary>After a dropped frame, stay fine until this many seconds pass with no drops.</summary>
    public double DropHoldSeconds { get; init; } = 60;

    /// <summary>CPU temperature (C) at or above which sampling switches to fine.</summary>
    public double CpuDangerC { get; init; } = 90;

    /// <summary>GPU temperature (C) at or above which sampling switches to fine.</summary>
    public double GpuDangerC { get; init; } = 85;

    /// <summary>CPU is considered stabilized once it cools to or below this (C).</summary>
    public double CpuStableC { get; init; } = 85;

    /// <summary>GPU is considered stabilized once it cools to or below this (C).</summary>
    public double GpuStableC { get; init; } = 80;
}

/// <summary>One time bucket of FPS data. <see cref="Fine"/> marks 30s (drop) buckets.</summary>
public sealed record FpsBucket(
    double FromSec, double ToSec, bool Fine,
    double AvgFps, double MinFps, int DroppedFrames, int FrameCount);

/// <summary>One time bucket of sensor data. <see cref="Fine"/> marks 30s (dangerous-temp) buckets.</summary>
public sealed record SensorBucket(
    double FromSec, double ToSec, bool Fine,
    double? AvgCpuLoad, double? AvgGpuLoad, double? AvgRamLoad,
    double? AvgCpuTemp, double? MaxCpuTemp, double? AvgGpuTemp, double? MaxGpuTemp);

/// <summary>Whole-test sensor averages (loads and temps only; no memory amounts).</summary>
public sealed record SensorAverages(
    double? AvgCpuLoad, double? AvgGpuLoad, double? AvgRamLoad,
    double? AvgCpuTemp, double? MaxCpuTemp, double? AvgGpuTemp, double? MaxGpuTemp);

/// <summary>
/// Builds the adaptive 5-minute / 30-second time buckets for the per-session
/// report. Pure and deterministic so the bucketing rules are unit-tested.
/// </summary>
public static class SessionReportBuilder
{
    // ---- FPS ----

    public static IReadOnlyList<FpsBucket> BuildFpsBuckets(
        IReadOnlyList<FrameSample> frames, ReportOptions options)
    {
        if (frames.Count == 0) return Array.Empty<FpsBucket>();

        double t0 = frames[0].TimeInSeconds;
        double total = Math.Max(0, frames[^1].TimeInSeconds - t0);
        if (total <= 0) total = options.FineSeconds; // single instant of data

        var fine = DropFineIntervals(frames, t0, total, options.DropHoldSeconds);
        var spans = MakeBuckets(total, fine, options.CoarseSeconds, options.FineSeconds);

        var result = new List<FpsBucket>(spans.Count);
        foreach (var (start, end, isFine) in spans)
        {
            double sumFt = 0, maxFt = 0;
            int count = 0, dropped = 0;
            foreach (var f in frames)
            {
                double r = f.TimeInSeconds - t0;
                if (r < start || r >= end) continue;
                if (f.Dropped) { dropped++; continue; }
                if (f.FrameTimeMs <= 0) continue;
                sumFt += f.FrameTimeMs;
                if (f.FrameTimeMs > maxFt) maxFt = f.FrameTimeMs;
                count++;
            }
            double avgFps = count > 0 ? 1000.0 / (sumFt / count) : 0;
            double minFps = maxFt > 0 ? 1000.0 / maxFt : 0;
            result.Add(new FpsBucket(start, end, isFine, avgFps, minFps, dropped, count));
        }
        return result;
    }

    private static List<(double s, double e)> DropFineIntervals(
        IReadOnlyList<FrameSample> frames, double t0, double total, double hold)
    {
        var raw = new List<(double, double)>();
        foreach (var f in frames)
        {
            if (!f.Dropped) continue;
            double r = f.TimeInSeconds - t0;
            raw.Add((r, r + hold));
        }
        return Merge(raw, total);
    }

    // ---- Sensors ----

    public static IReadOnlyList<SensorBucket> BuildSensorBuckets(
        IReadOnlyList<SensorSample> sensors, ReportOptions options)
    {
        if (sensors.Count == 0) return Array.Empty<SensorBucket>();

        DateTime first = sensors[0].TimestampUtc;
        double total = Math.Max(0, (sensors[^1].TimestampUtc - first).TotalSeconds);
        if (total <= 0) total = options.FineSeconds;

        var fine = DangerFineIntervals(sensors, first, total, options);
        var spans = MakeBuckets(total, fine, options.CoarseSeconds, options.FineSeconds);

        var result = new List<SensorBucket>(spans.Count);
        foreach (var (start, end, isFine) in spans)
        {
            var inBucket = new List<SensorSample>();
            foreach (var s in sensors)
            {
                double r = (s.TimestampUtc - first).TotalSeconds;
                if (r >= start && r < end) inBucket.Add(s);
            }
            result.Add(new SensorBucket(
                start, end, isFine,
                Avg(inBucket, x => x.CpuLoadPercent),
                Avg(inBucket, x => x.GpuLoadPercent),
                Avg(inBucket, x => x.RamLoadPercent),
                Avg(inBucket, x => x.CpuTempC),
                Max(inBucket, x => x.CpuTempC),
                Avg(inBucket, x => x.GpuTempC),
                Max(inBucket, x => x.GpuTempC)));
        }
        return result;
    }

    private static List<(double s, double e)> DangerFineIntervals(
        IReadOnlyList<SensorSample> sensors, DateTime first, double total, ReportOptions o)
    {
        var intervals = new List<(double, double)>();
        bool inDanger = false;
        double startT = 0;

        foreach (var s in sensors)
        {
            double r = (s.TimestampUtc - first).TotalSeconds;
            bool dangerous = (s.CpuTempC is { } ct && ct >= o.CpuDangerC)
                          || (s.GpuTempC is { } gt && gt >= o.GpuDangerC);
            bool stabilized = (s.CpuTempC is null || s.CpuTempC <= o.CpuStableC)
                           && (s.GpuTempC is null || s.GpuTempC <= o.GpuStableC);

            if (!inDanger && dangerous) { inDanger = true; startT = r; }
            else if (inDanger && stabilized) { inDanger = false; intervals.Add((startT, r)); }
        }
        if (inDanger) intervals.Add((startT, total));

        return Merge(intervals, total);
    }

    public static SensorAverages BuildSensorAverages(IReadOnlyList<SensorSample> sensors)
        => new(
            Avg(sensors, x => x.CpuLoadPercent),
            Avg(sensors, x => x.GpuLoadPercent),
            Avg(sensors, x => x.RamLoadPercent),
            Avg(sensors, x => x.CpuTempC),
            Max(sensors, x => x.CpuTempC),
            Avg(sensors, x => x.GpuTempC),
            Max(sensors, x => x.GpuTempC));

    public static int CountDroppedFrames(IReadOnlyList<FrameSample> frames)
    {
        int n = 0;
        foreach (var f in frames) if (f.Dropped) n++;
        return n;
    }

    // ---- Shared bucketing helpers ----

    /// <summary>
    /// Splits [0, total] into buckets: coarse-sized normally, but fine-sized
    /// (and clipped to the fine boundaries) wherever a fine interval applies.
    /// </summary>
    private static List<(double start, double end, bool fine)> MakeBuckets(
        double total, List<(double s, double e)> fineIntervals, double coarse, double fine)
    {
        var buckets = new List<(double, double, bool)>();
        double p = 0;
        int idx = 0;
        int guard = 0;

        while (p < total - 1e-9 && guard++ < 1_000_000)
        {
            while (idx < fineIntervals.Count && fineIntervals[idx].e <= p) idx++;

            bool inFine = idx < fineIntervals.Count
                          && fineIntervals[idx].s <= p && p < fineIntervals[idx].e;

            double end;
            bool fineFlag;
            if (inFine)
            {
                end = Math.Min(Math.Min(p + fine, fineIntervals[idx].e), total);
                fineFlag = true;
            }
            else
            {
                double nextFineStart = idx < fineIntervals.Count ? fineIntervals[idx].s : total;
                if (nextFineStart < p) nextFineStart = total;
                end = Math.Min(Math.Min(p + coarse, nextFineStart), total);
                fineFlag = false;
            }

            if (end <= p) break;
            buckets.Add((p, end, fineFlag));
            p = end;
        }
        return buckets;
    }

    /// <summary>Sorts, clamps to [0, total] and merges overlapping/adjacent intervals.</summary>
    private static List<(double s, double e)> Merge(List<(double s, double e)> intervals, double total)
    {
        var clamped = new List<(double s, double e)>();
        foreach (var (s, e) in intervals)
        {
            double cs = Math.Max(0, s);
            double ce = Math.Min(total, e);
            if (ce > cs) clamped.Add((cs, ce));
        }
        if (clamped.Count == 0) return clamped;

        clamped.Sort((a, b) => a.s.CompareTo(b.s));
        var merged = new List<(double s, double e)> { clamped[0] };
        for (int i = 1; i < clamped.Count; i++)
        {
            var last = merged[^1];
            if (clamped[i].s <= last.e)
                merged[^1] = (last.s, Math.Max(last.e, clamped[i].e));
            else
                merged.Add(clamped[i]);
        }
        return merged;
    }

    private static double? Avg(IReadOnlyList<SensorSample> samples, Func<SensorSample, double?> sel)
    {
        double sum = 0;
        int n = 0;
        foreach (var s in samples)
            if (sel(s) is { } v) { sum += v; n++; }
        return n == 0 ? null : sum / n;
    }

    private static double? Max(IReadOnlyList<SensorSample> samples, Func<SensorSample, double?> sel)
    {
        double max = double.NegativeInfinity;
        bool any = false;
        foreach (var s in samples)
            if (sel(s) is { } v) { if (v > max) max = v; any = true; }
        return any ? max : null;
    }
}

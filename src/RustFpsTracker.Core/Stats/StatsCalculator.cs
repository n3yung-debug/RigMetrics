using RustFpsTracker.Core.Models;

namespace RustFpsTracker.Core.Stats;

/// <summary>
/// Computes FPS and smoothness statistics from captured frames and sensors.
/// Pure and deterministic so it can be unit-tested without any Windows APIs.
/// </summary>
public static class StatsCalculator
{
    /// <summary>
    /// A frame is counted as a "stutter" when its frame time exceeds
    /// this multiple of the median frame time. 2.0 means "took at least
    /// twice as long as a typical frame".
    /// </summary>
    public const double DefaultStutterMultiplier = 2.0;

    public static SessionStats Compute(
        IReadOnlyList<FrameSample> frames,
        IReadOnlyList<SensorSample>? sensors = null,
        double stutterMultiplier = DefaultStutterMultiplier)
    {
        // Only frames that were actually presented with a positive frame time
        // contribute to FPS math.
        var frameTimes = new List<double>(frames.Count);
        foreach (var f in frames)
        {
            if (!f.Dropped && f.FrameTimeMs > 0)
                frameTimes.Add(f.FrameTimeMs);
        }

        if (frameTimes.Count == 0)
        {
            var (c, ct, g, gt, gm, r, rl) = AggregateSensors(sensors);
            return new SessionStats
            {
                FrameCount = 0,
                AvgCpuLoadPercent = c,
                MaxCpuTempC = ct,
                AvgGpuLoadPercent = g,
                MaxGpuTempC = gt,
                AvgGpuMemUsedMb = gm,
                AvgRamUsedGb = r,
                MaxRamLoadPercent = rl,
            };
        }

        // Frame times sorted ascending (fast -> slow).
        var ftSorted = new List<double>(frameTimes);
        ftSorted.Sort();

        double avgFrameTime = Mean(frameTimes);
        double stdDev = StdDev(frameTimes, avgFrameTime);
        double medianFt = Percentile(ftSorted, 50.0);

        // FPS values sorted ascending (slow -> fast). The slowest frames
        // (largest frame times) become the lowest FPS values.
        var fpsSorted = new List<double>(ftSorted.Count);
        for (int i = ftSorted.Count - 1; i >= 0; i--)
            fpsSorted.Add(1000.0 / ftSorted[i]);

        double stutterThreshold = medianFt * stutterMultiplier;
        int stutters = 0;
        foreach (var ft in frameTimes)
            if (ft > stutterThreshold) stutters++;

        double duration = ComputeDuration(frames, frameTimes, avgFrameTime);

        var (avgCpu, maxCpuTemp, avgGpu, maxGpuTemp, avgGpuMem, avgRam, maxRamLoad) =
            AggregateSensors(sensors);

        return new SessionStats
        {
            FrameCount = frameTimes.Count,
            DurationSeconds = duration,

            // True average FPS = total frames / total time = 1000 / mean frame time.
            AvgFps = 1000.0 / avgFrameTime,
            MinFps = fpsSorted[0],
            MaxFps = fpsSorted[^1],
            MedianFps = 1000.0 / medianFt,
            Percentile1LowFps = Percentile(fpsSorted, 1.0),
            Percentile01LowFps = Percentile(fpsSorted, 0.1),

            AvgFrameTimeMs = avgFrameTime,
            FrameTimeStdDevMs = stdDev,
            StutterCount = stutters,

            AvgCpuLoadPercent = avgCpu,
            MaxCpuTempC = maxCpuTemp,
            AvgGpuLoadPercent = avgGpu,
            MaxGpuTempC = maxGpuTemp,
            AvgGpuMemUsedMb = avgGpuMem,
            AvgRamUsedGb = avgRam,
            MaxRamLoadPercent = maxRamLoad,
        };
    }

    private static double ComputeDuration(
        IReadOnlyList<FrameSample> frames, List<double> frameTimes, double avgFrameTime)
    {
        // Prefer real timestamps when present; otherwise sum of frame times.
        double first = double.NaN, last = double.NaN;
        foreach (var f in frames)
        {
            if (double.IsNaN(first)) first = f.TimeInSeconds;
            last = f.TimeInSeconds;
        }

        if (!double.IsNaN(first) && last > first)
            return last - first;

        return frameTimes.Count * avgFrameTime / 1000.0;
    }

    /// <summary>
    /// Nearest-rank percentile over an ascending-sorted list.
    /// p is in [0, 100]. p=1 returns a value near the low end.
    /// </summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        int n = sortedAscending.Count;
        if (n == 0) return 0;
        if (n == 1) return sortedAscending[0];

        double rank = Math.Ceiling(p / 100.0 * n);
        int index = (int)rank - 1;
        if (index < 0) index = 0;
        if (index >= n) index = n - 1;
        return sortedAscending[index];
    }

    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return 0;
        double sum = 0;
        foreach (var v in values) sum += v;
        return sum / values.Count;
    }

    /// <summary>Population standard deviation.</summary>
    public static double StdDev(IReadOnlyList<double> values, double mean)
    {
        if (values.Count == 0) return 0;
        double sumSq = 0;
        foreach (var v in values)
        {
            double d = v - mean;
            sumSq += d * d;
        }
        return Math.Sqrt(sumSq / values.Count);
    }

    private static (double? avgCpu, double? maxCpuTemp, double? avgGpu, double? maxGpuTemp,
        double? avgGpuMem, double? avgRam, double? maxRamLoad) AggregateSensors(
        IReadOnlyList<SensorSample>? sensors)
    {
        if (sensors is null || sensors.Count == 0)
            return (null, null, null, null, null, null, null);

        return (
            Avg(sensors, s => s.CpuLoadPercent),
            Max(sensors, s => s.CpuTempC),
            Avg(sensors, s => s.GpuLoadPercent),
            Max(sensors, s => s.GpuTempC),
            Avg(sensors, s => s.GpuMemUsedMb),
            Avg(sensors, s => s.RamUsedGb),
            Max(sensors, s => s.RamLoadPercent));
    }

    private static double? Avg(IReadOnlyList<SensorSample> s, Func<SensorSample, double?> sel)
    {
        double sum = 0;
        int count = 0;
        foreach (var x in s)
        {
            var v = sel(x);
            if (v.HasValue) { sum += v.Value; count++; }
        }
        return count == 0 ? null : sum / count;
    }

    private static double? Max(IReadOnlyList<SensorSample> s, Func<SensorSample, double?> sel)
    {
        double max = double.NegativeInfinity;
        bool any = false;
        foreach (var x in s)
        {
            var v = sel(x);
            if (v.HasValue) { if (v.Value > max) max = v.Value; any = true; }
        }
        return any ? max : null;
    }
}

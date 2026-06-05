using RustFpsTracker.Core.Export;
using RustFpsTracker.Core.Models;
using Xunit;

namespace RustFpsTracker.Core.Tests;

public class SessionReportBuilderTests
{
    private static readonly ReportOptions Options = new(); // 5min / 30s / 60s hold / 90C / 85C

    private static List<FrameSample> BuildFrames(double seconds, double spacing, double frameTimeMs)
    {
        var frames = new List<FrameSample>();
        for (double t = 0; t <= seconds + 1e-9; t += spacing)
            frames.Add(new FrameSample { TimeInSeconds = t, FrameTimeMs = frameTimeMs });
        return frames;
    }

    [Fact]
    public void Fps_NoDrops_UsesFiveMinuteBuckets()
    {
        var frames = BuildFrames(700, 0.5, 10.0); // 0..700s, 100 FPS, no drops

        var buckets = SessionReportBuilder.BuildFpsBuckets(frames, Options);

        Assert.All(buckets, b => Assert.False(b.Fine));
        Assert.Equal(3, buckets.Count); // [0,300) [300,600) [600,700)
        Assert.Equal(0, buckets[0].FromSec);
        Assert.Equal(300, buckets[0].ToSec);
        Assert.Equal(100.0, buckets[0].AvgFps, 1);
    }

    [Fact]
    public void Fps_DropTriggers30sBucketsForOneMinute()
    {
        var frames = BuildFrames(700, 0.5, 10.0);
        // A dropped frame at t=100 should force fine sampling over [100, 160).
        int dropIndex = frames.FindIndex(f => Math.Abs(f.TimeInSeconds - 100.0) < 1e-9);
        frames[dropIndex] = frames[dropIndex] with { Dropped = true };

        var buckets = SessionReportBuilder.BuildFpsBuckets(frames, Options);

        // Coarse up to the drop, then two 30s fine buckets, then back to coarse.
        Assert.Contains(buckets, b => b.FromSec == 0 && b.ToSec == 100 && !b.Fine);
        Assert.Contains(buckets, b => b.FromSec == 100 && b.ToSec == 130 && b.Fine);
        Assert.Contains(buckets, b => b.FromSec == 130 && b.ToSec == 160 && b.Fine);
        Assert.Contains(buckets, b => b.FromSec == 160 && !b.Fine);
        // The drop is counted in its bucket.
        Assert.Equal(1, buckets.First(b => b.FromSec == 100).DroppedFrames);
    }

    private static List<SensorSample> BuildSensors(double seconds, double spacing, Func<double, double> cpuTemp)
    {
        var baseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sensors = new List<SensorSample>();
        for (double t = 0; t <= seconds + 1e-9; t += spacing)
            sensors.Add(new SensorSample
            {
                TimestampUtc = baseUtc.AddSeconds(t),
                CpuLoadPercent = 50,
                GpuLoadPercent = 60,
                RamLoadPercent = 40,
                CpuTempC = cpuTemp(t),
                GpuTempC = 70,
            });
        return sensors;
    }

    [Fact]
    public void Sensors_NoDanger_UsesFiveMinuteBuckets()
    {
        var sensors = BuildSensors(700, 10, _ => 70); // always cool

        var buckets = SessionReportBuilder.BuildSensorBuckets(sensors, Options);

        Assert.All(buckets, b => Assert.False(b.Fine));
        Assert.Equal(3, buckets.Count);
    }

    [Fact]
    public void Sensors_DangerousTemp_Triggers30sUntilStabilized()
    {
        // CPU spikes to 95C between t=200 and t=260, then cools to 70C.
        var sensors = BuildSensors(700, 10, t => (t >= 200 && t <= 260) ? 95 : 70);

        var buckets = SessionReportBuilder.BuildSensorBuckets(sensors, Options);

        // Fine sampling starts when it goes dangerous (t=200) and continues
        // until the next stabilized sample (t=270).
        Assert.Contains(buckets, b => b.FromSec == 0 && b.ToSec == 200 && !b.Fine);
        Assert.Contains(buckets, b => b.FromSec == 200 && b.Fine);
        Assert.Contains(buckets, b => b.FromSec >= 200 && b.ToSec <= 270 && b.Fine);
        // After stabilization it returns to coarse buckets.
        Assert.Contains(buckets, b => b.FromSec == 270 && !b.Fine);
    }

    [Fact]
    public void SensorAverages_ComputesLoadsAndTemps()
    {
        var sensors = BuildSensors(100, 10, _ => 80);

        var avg = SessionReportBuilder.BuildSensorAverages(sensors);

        Assert.Equal(50, avg.AvgCpuLoad!.Value, 3);
        Assert.Equal(60, avg.AvgGpuLoad!.Value, 3);
        Assert.Equal(40, avg.AvgRamLoad!.Value, 3);
        Assert.Equal(80, avg.AvgCpuTemp!.Value, 3);
        Assert.Equal(80, avg.MaxCpuTemp!.Value, 3);
    }

    [Fact]
    public void CountDroppedFrames_Counts()
    {
        var frames = new List<FrameSample>
        {
            new() { FrameTimeMs = 10 },
            new() { FrameTimeMs = 10, Dropped = true },
            new() { FrameTimeMs = 10, Dropped = true },
        };
        Assert.Equal(2, SessionReportBuilder.CountDroppedFrames(frames));
    }
}

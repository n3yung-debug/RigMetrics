using RigMetrics.Core.Models;
using RigMetrics.Core.Stats;
using Xunit;

namespace RigMetrics.Core.Tests;

public class StatsCalculatorTests
{
    private static FrameSample Frame(double frameTimeMs, double time = 0, bool dropped = false)
        => new() { FrameTimeMs = frameTimeMs, TimeInSeconds = time, Dropped = dropped };

    [Fact]
    public void ConstantFrameTime_GivesExactFps()
    {
        // 100 frames at exactly 10ms => 100 FPS everywhere.
        var frames = new List<FrameSample>();
        for (int i = 0; i < 100; i++) frames.Add(Frame(10.0, i * 0.01));

        var stats = StatsCalculator.Compute(frames);

        Assert.Equal(100, stats.FrameCount);
        Assert.Equal(100.0, stats.AvgFps, 3);
        Assert.Equal(100.0, stats.MinFps, 3);
        Assert.Equal(100.0, stats.MaxFps, 3);
        Assert.Equal(100.0, stats.Percentile1LowFps, 3);
        Assert.Equal(0.0, stats.FrameTimeStdDevMs, 6);
        Assert.Equal(0, stats.StutterCount);
    }

    [Fact]
    public void DroppedAndZeroFrames_AreExcluded()
    {
        var frames = new List<FrameSample>
        {
            Frame(10.0),
            Frame(10.0, dropped: true),
            Frame(0.0),
            Frame(10.0),
        };

        var stats = StatsCalculator.Compute(frames);

        Assert.Equal(2, stats.FrameCount);
        Assert.Equal(100.0, stats.AvgFps, 3);
    }

    [Fact]
    public void OnePercentLow_ReflectsWorstFrames()
    {
        // 99 fast frames (10ms => 100fps) and 1 slow frame (100ms => 10fps).
        var frames = new List<FrameSample>();
        for (int i = 0; i < 99; i++) frames.Add(Frame(10.0));
        frames.Add(Frame(100.0));

        var stats = StatsCalculator.Compute(frames);

        // The single worst frame should drag the 1% low down to ~10 FPS.
        Assert.Equal(10.0, stats.Percentile1LowFps, 1);

        // The (time-weighted) average FPS stays high because 99% of frames are
        // fast: 100 frames over (99*10ms + 100ms) = 1.09s => ~91.7 FPS. This is
        // exactly why average FPS hides stutter and the 1% low does not.
        Assert.True(stats.AvgFps > 85 && stats.AvgFps < 95,
            $"AvgFps was {stats.AvgFps}");
    }

    [Fact]
    public void StutterCount_CountsLongFrames()
    {
        var frames = new List<FrameSample>();
        for (int i = 0; i < 50; i++) frames.Add(Frame(10.0));   // median ~10ms
        frames.Add(Frame(25.0));                                 // > 2x median => stutter
        frames.Add(Frame(30.0));                                 // stutter

        var stats = StatsCalculator.Compute(frames);

        Assert.Equal(2, stats.StutterCount);
    }

    [Fact]
    public void Duration_UsesTimestampsWhenAvailable()
    {
        var frames = new List<FrameSample>
        {
            Frame(10.0, time: 5.0),
            Frame(10.0, time: 5.5),
            Frame(10.0, time: 6.0),
        };

        var stats = StatsCalculator.Compute(frames);

        Assert.Equal(1.0, stats.DurationSeconds, 6);
    }

    [Fact]
    public void Sensors_AreAggregated()
    {
        var frames = new List<FrameSample> { Frame(10.0) };
        var sensors = new List<SensorSample>
        {
            new() { CpuLoadPercent = 40, CpuTempC = 60, GpuTempC = 70, RamUsedGb = 8 },
            new() { CpuLoadPercent = 60, CpuTempC = 65, GpuTempC = 80, RamUsedGb = 10 },
        };

        var stats = StatsCalculator.Compute(frames, sensors);

        Assert.Equal(50.0, stats.AvgCpuLoadPercent!.Value, 3);
        Assert.Equal(65.0, stats.MaxCpuTempC!.Value, 3);
        Assert.Equal(80.0, stats.MaxGpuTempC!.Value, 3);
        Assert.Equal(9.0, stats.AvgRamUsedGb!.Value, 3);
    }

    [Fact]
    public void EmptyFrames_DoesNotThrow_AndStillAggregatesSensors()
    {
        var sensors = new List<SensorSample> { new() { CpuTempC = 55 } };

        var stats = StatsCalculator.Compute(new List<FrameSample>(), sensors);

        Assert.Equal(0, stats.FrameCount);
        Assert.Equal(55.0, stats.MaxCpuTempC!.Value, 3);
    }

    [Theory]
    [InlineData(50.0, 5.0)]   // median of 1..10 at p50 (nearest-rank) => index ceil(5)=5 -> value 5
    [InlineData(100.0, 10.0)]
    public void Percentile_NearestRank(double p, double expected)
    {
        var sorted = new List<double> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.Equal(expected, StatsCalculator.Percentile(sorted, p), 6);
    }
}

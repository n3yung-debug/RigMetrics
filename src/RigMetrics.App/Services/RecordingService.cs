using RigMetrics.Core.Export;
using RigMetrics.Core.Models;
using RigMetrics.Core.Stats;
using RigMetrics.Core.Storage;

namespace RigMetrics.App.Services;

/// <summary>
/// Ties PresentMon and the hardware monitor together. "Start" begins capturing
/// frames + sensors into a new session and feeds the live dashboard; "Stop"
/// computes the final statistics and saves the session to disk.
/// </summary>
public sealed class RecordingService : IDisposable
{
    private readonly AppConfig _config;
    private readonly PresentMonService _presentMon;
    private readonly HardwareMonitorService _hardware;
    private readonly SessionStore _store;

    private readonly object _gate = new();

    // Full capture for the saved session.
    private SessionRecording? _session;

    // Bounded rolling buffers for the live dashboard.
    private readonly Queue<FrameSample> _liveFrames = new();
    private readonly Queue<SensorSample> _liveSensors = new();
    private double _latestFrameTime; // PresentMon TimeInSeconds of the newest frame

    public RecordingService(AppConfig config, PresentMonService presentMon,
        HardwareMonitorService hardware, SessionStore store)
    {
        _config = config;
        _presentMon = presentMon;
        _hardware = hardware;
        _store = store;

        _presentMon.FrameCaptured += OnFrame;
        _hardware.SensorUpdated += OnSensor;
    }

    public bool IsTracking { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Starts a new tracking session. Returns false (and sets LastError) on failure.</summary>
    public bool Start(string label, string notes)
    {
        lock (_gate)
        {
            if (IsTracking) return true;

            _liveFrames.Clear();
            _liveSensors.Clear();
            _latestFrameTime = 0;

            _session = new SessionRecording
            {
                Label = label,
                Notes = notes,
                GameProcessName = _config.GameProcessName,
                StartedUtc = DateTime.UtcNow,
            };
        }

        try
        {
            _hardware.Start();
            _presentMon.Start();
            LastError = null;
            IsTracking = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            // Roll back so we don't leave half-started capture running.
            _presentMon.Stop();
            _hardware.Stop();
            lock (_gate) _session = null;
            IsTracking = false;
            return false;
        }
    }

    /// <summary>Stops tracking, computes statistics, saves the session, and returns it.</summary>
    public SessionRecording? Stop()
    {
        if (!IsTracking) return null;
        IsTracking = false;

        _presentMon.Stop();
        _hardware.Stop();

        SessionRecording? session;
        lock (_gate)
        {
            session = _session;
            _session = null;
        }
        if (session is null) return null;

        session.EndedUtc = DateTime.UtcNow;
        session.Stats = StatsCalculator.Compute(
            session.Frames, session.Sensors, _config.StutterMultiplier);

        try
        {
            _store.Save(session);
            // Keep the running master results spreadsheet up to date.
            ResultsExporter.AppendToMaster(SessionSummaryRow.From(session), _store.MasterCsvPath);
        }
        catch (Exception ex)
        {
            LastError = $"Failed to save session: {ex.Message}";
        }
        return session;
    }

    private void OnFrame(FrameSample frame)
    {
        lock (_gate)
        {
            _session?.Frames.Add(frame);

            _liveFrames.Enqueue(frame);
            if (frame.TimeInSeconds > _latestFrameTime)
                _latestFrameTime = frame.TimeInSeconds;

            TrimLiveFrames();
        }
    }

    private void OnSensor(SensorSample sensor)
    {
        lock (_gate)
        {
            _session?.Sensors.Add(sensor);

            _liveSensors.Enqueue(sensor);
            // Keep enough sensor history to fill the temperature sparklines.
            int max = Math.Max(60, _config.LiveWindowSeconds * 1000 / Math.Max(1, _config.SensorPollMs));
            while (_liveSensors.Count > max) _liveSensors.Dequeue();
        }
    }

    private void TrimLiveFrames()
    {
        double cutoff = _latestFrameTime - _config.LiveWindowSeconds;
        while (_liveFrames.Count > 0 && _liveFrames.Peek().TimeInSeconds < cutoff)
            _liveFrames.Dequeue();

        // Hard cap so an extreme frame rate can't grow the buffer without bound.
        const int hardCap = 60_000;
        while (_liveFrames.Count > hardCap) _liveFrames.Dequeue();
    }

    /// <summary>Builds a snapshot for the dashboard. Safe to call from the UI thread.</summary>
    public LiveSnapshot GetSnapshot()
    {
        FrameSample[] frames;
        SensorSample[] sensors;
        double latestTime;
        DateTime? startedUtc;

        lock (_gate)
        {
            frames = _liveFrames.ToArray();
            sensors = _liveSensors.ToArray();
            latestTime = _latestFrameTime;
            startedUtc = _session?.StartedUtc;
        }

        var elapsed = startedUtc.HasValue ? DateTime.UtcNow - startedUtc.Value : TimeSpan.Zero;

        if (frames.Length == 0)
        {
            return new LiveSnapshot
            {
                IsTracking = IsTracking,
                Elapsed = elapsed,
                Sensors = _hardware.Latest,
                CpuTempSeries = ExtractSeries(sensors, s => s.CpuTempC),
                GpuTempSeries = ExtractSeries(sensors, s => s.GpuTempC),
            };
        }

        var stats = StatsCalculator.Compute(frames, sensors, _config.StutterMultiplier);

        // "Current" FPS: average over the most recent half-second for stability.
        double recentCutoff = latestTime - 0.5;
        double sumFt = 0;
        int recentCount = 0;
        for (int i = frames.Length - 1; i >= 0; i--)
        {
            if (frames[i].TimeInSeconds < recentCutoff) break;
            sumFt += frames[i].FrameTimeMs;
            recentCount++;
        }
        double currentFps = recentCount > 0 ? 1000.0 / (sumFt / recentCount) : stats.AvgFps;

        return new LiveSnapshot
        {
            IsTracking = IsTracking,
            Elapsed = elapsed,
            CurrentFps = currentFps,
            AvgFps = stats.AvgFps,
            OnePercentLowFps = stats.Percentile1LowFps,
            PointOnePercentLowFps = stats.Percentile01LowFps,
            FrameTimeMs = stats.AvgFrameTimeMs,
            FrameCount = stats.FrameCount,
            Sensors = _hardware.Latest,
            FpsSeries = Downsample(frames, f => f.Fps, 600),
            FrameTimeSeries = Downsample(frames, f => f.FrameTimeMs, 600),
            CpuTempSeries = ExtractSeries(sensors, s => s.CpuTempC),
            GpuTempSeries = ExtractSeries(sensors, s => s.GpuTempC),
        };
    }

    private static double[] Downsample(FrameSample[] frames, Func<FrameSample, double> sel, int maxPoints)
    {
        if (frames.Length <= maxPoints)
        {
            var all = new double[frames.Length];
            for (int i = 0; i < frames.Length; i++) all[i] = sel(frames[i]);
            return all;
        }

        // Bucket the series down to maxPoints, taking the worst (max frame time /
        // min fps) per bucket so stutter spikes stay visible.
        var result = new double[maxPoints];
        double step = (double)frames.Length / maxPoints;
        for (int i = 0; i < maxPoints; i++)
        {
            int start = (int)(i * step);
            int end = (int)((i + 1) * step);
            if (end <= start) end = start + 1;
            if (end > frames.Length) end = frames.Length;

            double acc = 0;
            int n = 0;
            for (int j = start; j < end; j++) { acc += sel(frames[j]); n++; }
            result[i] = n > 0 ? acc / n : 0;
        }
        return result;
    }

    private static double[] ExtractSeries(SensorSample[] sensors, Func<SensorSample, double?> sel)
    {
        var list = new List<double>(sensors.Length);
        foreach (var s in sensors)
            if (sel(s) is { } v) list.Add(v);
        return list.ToArray();
    }

    public void Dispose()
    {
        _presentMon.FrameCaptured -= OnFrame;
        _hardware.SensorUpdated -= OnSensor;
    }
}

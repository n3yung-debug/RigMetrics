using LibreHardwareMonitor.Hardware;
using RustFpsTracker.Core.Models;

namespace RustFpsTracker.App.Services;

/// <summary>
/// Polls CPU/GPU/RAM load and temperatures via LibreHardwareMonitor.
/// Temperatures require the app to run as Administrator (see app.manifest).
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly System.Threading.Timer _timer;
    private readonly int _pollMs;
    private readonly object _gate = new();
    private bool _opened;

    public HardwareMonitorService(int pollMs)
    {
        _pollMs = pollMs;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true, // some CPU temps surface via the motherboard
        };
        _timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Raised (on a background thread) every poll with the latest sensor snapshot.</summary>
    public event Action<SensorSample>? SensorUpdated;

    /// <summary>The most recently read snapshot, or null if none yet.</summary>
    public SensorSample? Latest { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (!_opened)
            {
                try
                {
                    _computer.Open();
                    _opened = true;
                }
                catch
                {
                    // Sensors unavailable (e.g. not elevated). The app still runs;
                    // hardware fields just stay empty.
                    return;
                }
            }
            _timer.Change(0, _pollMs);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void Poll()
    {
        // Guard against overlapping polls if a read runs long.
        if (System.Threading.Interlocked.Exchange(ref _pollingFlag, 1) == 1) return;
        try
        {
            _computer.Accept(_visitor);
            var sample = Read();
            Latest = sample;
            SensorUpdated?.Invoke(sample);
        }
        catch
        {
            // Ignore transient read errors.
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _pollingFlag, 0);
        }
    }

    private int _pollingFlag;

    private SensorSample Read()
    {
        double? cpuLoad = null, cpuTemp = null;
        double? gpuLoad = null, gpuTemp = null, gpuMem = null;
        double? ramUsed = null, ramLoad = null;

        foreach (var hw in _computer.Hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    cpuLoad ??= FindLoad(hw, "CPU Total") ?? FirstLoad(hw);
                    cpuTemp ??= FindTemp(hw, "Core (Tctl/Tdie)", "CPU Package", "Core Max")
                                ?? MaxTemp(hw);
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    gpuLoad ??= FindLoad(hw, "GPU Core") ?? FirstLoad(hw);
                    gpuTemp ??= FindTemp(hw, "GPU Core", "GPU Hot Spot") ?? MaxTemp(hw);
                    gpuMem ??= FindSensor(hw, s =>
                        (s.SensorType == SensorType.SmallData || s.SensorType == SensorType.Data)
                        && s.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase));
                    break;

                case HardwareType.Memory:
                    ramUsed ??= FindSensor(hw, s =>
                        s.SensorType == SensorType.Data
                        && s.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase));
                    ramLoad ??= FindLoad(hw, "Memory");
                    break;

                case HardwareType.Motherboard:
                    // Fallback CPU temperature if the CPU package sensor was hidden.
                    cpuTemp ??= FindTempRecursive(hw, "CPU");
                    break;
            }
        }

        return new SensorSample
        {
            TimestampUtc = DateTime.UtcNow,
            CpuLoadPercent = cpuLoad,
            CpuTempC = cpuTemp,
            GpuLoadPercent = gpuLoad,
            GpuTempC = gpuTemp,
            GpuMemUsedMb = gpuMem,
            RamUsedGb = ramUsed,
            RamLoadPercent = ramLoad,
        };
    }

    private static double? FindLoad(IHardware hw, string name)
        => FindSensor(hw, s => s.SensorType == SensorType.Load
            && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static double? FirstLoad(IHardware hw)
        => FindSensor(hw, s => s.SensorType == SensorType.Load);

    private static double? FindTemp(IHardware hw, params string[] names)
    {
        foreach (var name in names)
        {
            var v = FindSensor(hw, s => s.SensorType == SensorType.Temperature
                && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (v.HasValue) return v;
        }
        return null;
    }

    private static double? MaxTemp(IHardware hw)
    {
        double max = double.NegativeInfinity;
        bool any = false;
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Temperature && s.Value is { } v)
            {
                if (v > max) max = v;
                any = true;
            }
        }
        return any ? max : null;
    }

    private static double? FindTempRecursive(IHardware hw, string contains)
    {
        var v = FindSensor(hw, s => s.SensorType == SensorType.Temperature
            && s.Name.Contains(contains, StringComparison.OrdinalIgnoreCase));
        if (v.HasValue) return v;
        foreach (var sub in hw.SubHardware)
        {
            var sv = FindTempRecursive(sub, contains);
            if (sv.HasValue) return sv;
        }
        return null;
    }

    private static double? FindSensor(IHardware hw, Func<ISensor, bool> predicate)
    {
        foreach (var s in hw.Sensors)
            if (predicate(s) && s.Value is { } v)
                return v;
        return null;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
        try { if (_opened) _computer.Close(); } catch { /* ignore */ }
    }

    /// <summary>Walks the hardware tree so each sensor refreshes its value.</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}

using System.Diagnostics;
using System.IO;
using RustFpsTracker.Core.Models;
using RustFpsTracker.Core.PresentMon;

namespace RustFpsTracker.App.Services;

/// <summary>
/// Runs Intel PresentMon as a child process and turns its CSV stdout into a
/// stream of <see cref="FrameSample"/> events.
///
/// PresentMon reads frame-presentation timings through ETW at the OS level and
/// never injects into the game, which is why this approach is safe to use with
/// Easy Anti-Cheat (Rust). See the README for details.
/// </summary>
public sealed class PresentMonService : IDisposable
{
    private readonly string _exePath;
    private readonly string[] _extraArgs;
    private readonly PresentMonCsvParser _parser = new();
    private readonly object _gate = new();

    private Process? _process;

    /// <summary>
    /// The executable name of the game/app to track (e.g. "RustClient.exe").
    /// Settable while idle so the user can switch targets between sessions.
    /// </summary>
    public string ProcessName { get; set; }

    public PresentMonService(string exePath, string processName, string[] extraArgs)
    {
        _exePath = exePath;
        ProcessName = processName;
        _extraArgs = extraArgs;
    }

    /// <summary>Raised (on a background thread) for every captured frame of the target game.</summary>
    public event Action<FrameSample>? FrameCaptured;

    /// <summary>Raised (on a background thread) with diagnostic text from PresentMon.</summary>
    public event Action<string>? Message;

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;

            if (!File.Exists(_exePath))
            {
                throw new FileNotFoundException(
                    $"PresentMon was not found at \"{_exePath}\". Download it from " +
                    "https://github.com/GameTechDev/PresentMon/releases, rename it to " +
                    "PresentMon.exe, and place it next to this app (or set presentMonPath in appsettings.json).",
                    _exePath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = BuildArguments(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += OnOutputLine;
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data)) Message?.Invoke(e.Data!);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }
    }

    public void Stop()
    {
        Process? toStop;
        lock (_gate)
        {
            toStop = _process;
            _process = null;
        }

        if (toStop is null) return;
        try
        {
            if (!toStop.HasExited)
            {
                // Kill the whole tree; PresentMon may spawn helper processes.
                toStop.Kill(entireProcessTree: true);
                toStop.WaitForExit(3000);
            }
        }
        catch
        {
            // Process may have already exited.
        }
        finally
        {
            toStop.Dispose();
        }
    }

    private string BuildArguments()
    {
        // --output_stdout      : stream CSV to stdout instead of writing a file
        // --stop_existing_session : take over a leftover ETW session if one exists
        // --terminate_on_proc_exit: exit when the game closes
        var args = new List<string>
        {
            "--process_name", ProcessName,
            "--output_stdout",
            "--stop_existing_session",
            "--terminate_on_proc_exit",
        };
        args.AddRange(_extraArgs);

        return string.Join(' ', args.Select(QuoteIfNeeded));
    }

    private static string QuoteIfNeeded(string arg)
        => arg.Contains(' ') ? $"\"{arg}\"" : arg;

    private void OnOutputLine(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;

        try
        {
            if (!_parser.TryParseLine(e.Data, out var sample, out var app))
                return;

            // Only forward frames belonging to the target game.
            if (sample is null) return;
            if (app is not null &&
                !string.Equals(app, ProcessName, StringComparison.OrdinalIgnoreCase))
                return;

            FrameCaptured?.Invoke(sample);
        }
        catch
        {
            // Never let a single bad line tear down the capture loop.
        }
    }

    public void Dispose() => Stop();
}

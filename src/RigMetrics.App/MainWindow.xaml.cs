using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using RigMetrics.App.Services;
using RigMetrics.Core.Models;
using RigMetrics.Core.Storage;

namespace RigMetrics.App;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly SessionStore _store;
    private readonly PresentMonService _presentMon;
    private readonly HardwareMonitorService _hardware;
    private readonly RecordingService _recording;
    private readonly DispatcherTimer _uiTimer;
    private readonly string _presentMonPath;
    private readonly UserPreferences _prefs;

    public MainWindow()
    {
        InitializeComponent();

        var baseDir = AppContext.BaseDirectory;
        _config = AppConfig.Load(Path.Combine(baseDir, "appsettings.json"));
        _store = SessionStore.CreateDefault();
        _prefs = UserPreferences.Load();

        _presentMonPath = _config.ResolvePresentMonPath(baseDir);
        _presentMon = new PresentMonService(
            _presentMonPath,
            _config.GameProcessName,
            _config.PresentMonExtraArgs);
        _hardware = new HardwareMonitorService(_config.SensorPollMs);
        _recording = new RecordingService(_config, _presentMon, _hardware, _store);

        // Sensible fixed axes so spikes are shown in context.
        FpsSpark.FixedMin = 0;
        FrameTimeSpark.FixedMin = 0;

        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _uiTimer.Tick += (_, _) => RefreshLive();
        _uiTimer.Start();

        // Populate the game picker, defaulting to the last tracked game (or config).
        var initialTarget = string.IsNullOrWhiteSpace(_prefs.LastGameProcessName)
            ? _config.GameProcessName
            : _prefs.LastGameProcessName;
        PopulateTargets(initialTarget);

        LoadSessions();
        Closed += OnClosed;
    }

    // ===== Game target picker =====

    private void TargetCombo_DropDownOpened(object sender, EventArgs e)
        => PopulateTargets(TargetCombo.Text);

    private void PopulateTargets(string? keepSelection)
    {
        var apps = ProcessLister.ListWindowedApps().ToList();
        var current = string.IsNullOrWhiteSpace(keepSelection) ? TargetCombo.Text : keepSelection;

        // Make sure the current/last choice is always selectable even if not running.
        if (!string.IsNullOrWhiteSpace(current)
            && !apps.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            apps.Insert(0, current);
        }

        TargetCombo.ItemsSource = apps;
        if (!string.IsNullOrWhiteSpace(current))
            TargetCombo.Text = current;
    }

    // ===== Recording control =====

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recording.IsTracking)
        {
            StopTracking();
        }
        else
        {
            await StartTrackingAsync();
        }
    }

    private async Task StartTrackingAsync()
    {
        // Resolve which game/app to track from the picker (dropdown or typed name).
        var target = (TargetCombo.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show(this,
                "Choose a game to track (pick a running app from the list, or type its .exe name, e.g. cs2.exe).",
                "No game selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _presentMon.ProcessName = target;
        _config.GameProcessName = target;
        _prefs.LastGameProcessName = target;
        _prefs.Save();

        // PresentMon is bundled in the release ZIP. If it is missing (e.g. a
        // build from source), offer to fetch it automatically.
        if (!PresentMonProvider.Exists(_presentMonPath))
        {
            var choice = MessageBox.Show(this,
                "PresentMon is required to measure FPS and was not found.\n\n" +
                "Download the official PresentMon now? (~a few MB, one time)",
                "Download PresentMon", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
                return;

            try
            {
                StartStopButton.IsEnabled = false;
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                var progress = new Progress<string>(msg => StatusText.Text = msg);
                await PresentMonProvider.EnsureAsync(_presentMonPath, progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Could not download PresentMon automatically:\n\n" + ex.Message +
                    "\n\nDownload it from https://github.com/GameTechDev/PresentMon/releases " +
                    "and place PresentMon.exe next to this app.",
                    "Download failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                StartStopButton.IsEnabled = true;
            }
        }

        var label = string.IsNullOrWhiteSpace(LabelBox.Text)
            ? $"Session {DateTime.Now:yyyy-MM-dd HH:mm}"
            : LabelBox.Text.Trim();

        if (!_recording.Start(label, notes: ""))
        {
            MessageBox.Show(this,
                _recording.LastError ?? "Failed to start tracking.",
                "Could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StartStopButton.Content = "Stop";
        StatusText.Text = $"Tracking \"{label}\" on {target} - play now. Stop when done.";
    }

    private void StopTracking()
    {
        var session = _recording.Stop();
        StartStopButton.Content = "Start";

        if (session?.Stats is { } s && s.FrameCount > 0)
        {
            StatusText.Text =
                $"Saved \"{session.Label}\": avg {s.AvgFps:0.0} | 1% low {s.Percentile1LowFps:0.0} | " +
                $"0.1% low {s.Percentile01LowFps:0.0} | {s.StutterCount} stutters";
        }
        else
        {
            StatusText.Text =
                $"Stopped. No frames captured - is {_config.GameProcessName} running, and was the app started as Administrator?";
        }
        LoadSessions();
    }

    // ===== Live dashboard refresh =====

    private void RefreshLive()
    {
        var snap = _recording.GetSnapshot();

        CurrentFpsText.Text = snap.FrameCount > 0 ? snap.CurrentFps.ToString("0") : "--";
        AvgFpsText.Text = snap.FrameCount > 0 ? snap.AvgFps.ToString("0.0") : "--";
        Low1Text.Text = snap.FrameCount > 0 ? snap.OnePercentLowFps.ToString("0.0") : "--";
        Low01Text.Text = snap.FrameCount > 0 ? snap.PointOnePercentLowFps.ToString("0.0") : "--";
        FrameTimeText.Text = snap.FrameCount > 0 ? $"{snap.FrameTimeMs:0.0} ms" : "--";
        FrameCountText.Text = $"{snap.FrameCount} frames";
        ElapsedText.Text = snap.Elapsed.ToString(@"mm\:ss");

        FpsSpark.SetValues(snap.FpsSeries);
        FrameTimeSpark.SetValues(snap.FrameTimeSeries);
        CpuTempSpark.SetValues(snap.CpuTempSeries);
        GpuTempSpark.SetValues(snap.GpuTempSeries);

        var hw = snap.Sensors;
        CpuLoadText.Text = Fmt(hw?.CpuLoadPercent, "0", " %");
        CpuTempText.Text = Fmt(hw?.CpuTempC, "0", " C");
        GpuLoadText.Text = Fmt(hw?.GpuLoadPercent, "0", " %");
        GpuTempText.Text = Fmt(hw?.GpuTempC, "0", " C");
        GpuMemText.Text = "VRAM: " + Fmt(hw?.GpuMemUsedMb, "0", " MB");
        RamUsedText.Text = Fmt(hw?.RamUsedGb, "0.0", " GB");
        RamLoadText.Text = Fmt(hw?.RamLoadPercent, "0", " %");

        // Prominent temperature tiles, coloured by how hot they are.
        UpdateTempTile(CpuTempTileText, hw?.CpuTempC, _config.ReportCpuDangerC);
        UpdateTempTile(GpuTempTileText, hw?.GpuTempC, _config.ReportGpuDangerC);
    }

    private void UpdateTempTile(System.Windows.Controls.TextBlock tile, double? tempC, double dangerC)
    {
        tile.Text = tempC.HasValue ? $"{tempC.Value:0} °C" : "--";

        var brushKey = "GoodBrush";
        if (tempC is { } t)
        {
            if (t >= dangerC) brushKey = "BadBrush";
            else if (t >= dangerC - 10) brushKey = "WarnBrush";
        }
        tile.Foreground = (System.Windows.Media.Brush)FindResource(brushKey);
    }

    private static string Fmt(double? value, string format, string suffix)
        => value.HasValue ? value.Value.ToString(format) + suffix : "--";

    // ===== Sessions list =====

    private void LoadSessions()
    {
        SessionsList.ItemsSource = _store.LoadAll();
    }

    private List<SessionRecording> SelectedSessions()
        => SessionsList.SelectedItems.Cast<SessionRecording>().ToList();

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedSessions();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select a session to export.", "Export report");
            return;
        }

        var session = selected[0];
        var dialog = new SaveFileDialog
        {
            Title = "Export session report",
            FileName = SanitizeFileName(session.Label) + "_report.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog(this) != true) return;

        // One workbook: Summary + FPS over time + Sensors over time, with
        // adaptive 5-min / 30-s bucketing on drops and dangerous temps.
        SessionReportXlsxExporter.Export(session, _config.BuildReportOptions(), dialog.FileName);

        StatusText.Text = $"Exported report to {dialog.FileName}";
    }

    private void Results_Click(object sender, RoutedEventArgs e)
    {
        var window = new ResultsWindow(_store) { Owner = this };
        window.ShowDialog();
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedSessions();
        if (selected.Count != 2)
        {
            MessageBox.Show(this, "Select exactly two sessions to compare.", "Compare");
            return;
        }
        var window = new ComparisonWindow(selected[0], selected[1]) { Owner = this };
        window.ShowDialog();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = SelectedSessions();
        if (selected.Count == 0) return;

        var result = MessageBox.Show(this,
            $"Delete {selected.Count} session(s)? This cannot be undone.",
            "Delete sessions", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        foreach (var s in selected) _store.Delete(s.Id);
        LoadSessions();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_store.Directory}\""));
        }
        catch
        {
            MessageBox.Show(this, _store.Directory, "Sessions folder");
        }
    }

    private void TopmostCheck_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheck.IsChecked == true;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "session" : name;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _uiTimer.Stop();
        _recording.Dispose();
        _presentMon.Dispose();
        _hardware.Dispose();
    }
}

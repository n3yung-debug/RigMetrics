using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RustFpsTracker.Core.Models;

namespace RustFpsTracker.App;

/// <summary>
/// Side-by-side comparison of two sessions so you can see exactly which
/// settings change helped. Built in code (no XAML) to keep it self-contained.
/// </summary>
public sealed class ComparisonWindow : Window
{
    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x24));
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x33));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA));
    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(0x5B, 0xD7, 0x5B));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xE0, 0x60, 0x60));

    /// <summary>Whether a higher value is better for a given metric.</summary>
    private enum Better { Higher, Lower }

    public ComparisonWindow(SessionRecording a, SessionRecording b)
    {
        Title = "Compare sessions";
        Width = 720;
        Height = 560;
        Background = Bg;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent(a, b);
    }

    private FrameworkElement BuildContent(SessionRecording a, SessionRecording b)
    {
        var sa = a.Stats;
        var sb = b.Stats;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header with the two labels.
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddCell(header, 0, "METRIC", Muted, bold: true);
        AddCell(header, 1, $"A: {a.Label}", Text, bold: true);
        AddCell(header, 2, $"B: {b.Label}", Text, bold: true);
        AddCell(header, 3, "DELTA (B vs A)", Muted, bold: true);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var rows = new StackPanel();
        AddRow(rows, "Avg FPS", sa?.AvgFps, sb?.AvgFps, Better.Higher, "0.0");
        AddRow(rows, "1% low FPS", sa?.Percentile1LowFps, sb?.Percentile1LowFps, Better.Higher, "0.0");
        AddRow(rows, "0.1% low FPS", sa?.Percentile01LowFps, sb?.Percentile01LowFps, Better.Higher, "0.0");
        AddRow(rows, "Min FPS", sa?.MinFps, sb?.MinFps, Better.Higher, "0.0");
        AddRow(rows, "Max FPS", sa?.MaxFps, sb?.MaxFps, Better.Higher, "0.0");
        AddRow(rows, "Avg frame time (ms)", sa?.AvgFrameTimeMs, sb?.AvgFrameTimeMs, Better.Lower, "0.00");
        AddRow(rows, "Frame time stddev (ms)", sa?.FrameTimeStdDevMs, sb?.FrameTimeStdDevMs, Better.Lower, "0.00");
        AddRow(rows, "Stutters", sa?.StutterCount, sb?.StutterCount, Better.Lower, "0");
        AddRow(rows, "Avg CPU load (%)", sa?.AvgCpuLoadPercent, sb?.AvgCpuLoadPercent, Better.Lower, "0");
        AddRow(rows, "Max CPU temp (C)", sa?.MaxCpuTempC, sb?.MaxCpuTempC, Better.Lower, "0");
        AddRow(rows, "Avg GPU load (%)", sa?.AvgGpuLoadPercent, sb?.AvgGpuLoadPercent, Better.Lower, "0");
        AddRow(rows, "Max GPU temp (C)", sa?.MaxGpuTempC, sb?.MaxGpuTempC, Better.Lower, "0");
        AddRow(rows, "Avg RAM used (GB)", sa?.AvgRamUsedGb, sb?.AvgRamUsedGb, Better.Lower, "0.0");
        AddRow(rows, "Duration (s)", sa?.DurationSeconds, sb?.DurationSeconds, Better.Higher, "0");

        var scroller = new ScrollViewer
        {
            Content = rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Grid.SetRow(scroller, 1);
        grid.Children.Add(scroller);

        return grid;
    }

    private void AddRow(StackPanel host, string metric, double? a, double? b, Better better, string format)
    {
        var border = new Border
        {
            Background = Panel,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 0, 4),
            Padding = new Thickness(10, 8, 10, 8),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddCell(grid, 0, metric, Text, bold: false);
        AddCell(grid, 1, Format(a, format), Text, bold: false);
        AddCell(grid, 2, Format(b, format), Text, bold: false);

        string deltaText = "--";
        Brush deltaBrush = Muted;
        if (a.HasValue && b.HasValue)
        {
            double delta = b.Value - a.Value;
            string sign = delta > 0 ? "+" : "";
            deltaText = sign + delta.ToString(format, CultureInfo.InvariantCulture);

            if (Math.Abs(delta) < 1e-9) deltaBrush = Muted;
            else
            {
                bool improved = better == Better.Higher ? delta > 0 : delta < 0;
                deltaBrush = improved ? Good : Bad;
            }
        }
        AddCell(grid, 3, deltaText, deltaBrush, bold: true);

        border.Child = grid;
        host.Children.Add(border);
    }

    private static void AddCell(Grid grid, int column, string text, Brush brush, bool bold)
    {
        var tb = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            FontFamily = new FontFamily("Segoe UI"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private static string Format(double? value, string format)
        => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : "--";
}

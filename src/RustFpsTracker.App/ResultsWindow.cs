using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using RustFpsTracker.App.Services;
using RustFpsTracker.Core.Export;
using RustFpsTracker.Core.Models;
using RustFpsTracker.Core.Storage;

namespace RustFpsTracker.App;

/// <summary>
/// A sortable table of every recorded session's results, with one-click export
/// to a CSV (Google Sheets-ready) or an Excel .xlsx workbook. Built in code so
/// it stays self-contained.
/// </summary>
public sealed class ResultsWindow : Window
{
    private static readonly Brush Bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1F, 0x24));
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x2A, 0x2C, 0x33));
    private static readonly Brush Text = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xED));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xAA));

    private readonly SessionStore _store;
    private readonly List<SessionSummaryRow> _rows;
    private readonly TextBlock _info;

    public ResultsWindow(SessionStore store)
    {
        _store = store;
        _rows = store.LoadAll().Select(SessionSummaryRow.From).ToList();

        Title = "Results history";
        Width = 1000;
        Height = 600;
        Background = Bg;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        // Toolbar
        var toolbar = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(toolbar, Dock.Top);
        toolbar.Margin = new Thickness(0, 0, 0, 10);

        _info = new TextBlock
        {
            Text = $"{_rows.Count} session(s).  Master file auto-updates after each session.",
            Foreground = Muted,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(_info, Dock.Left);
        toolbar.Children.Add(_info);

        toolbar.Children.Add(MakeButton("Open master file", Dock.Right, OpenMaster));
        toolbar.Children.Add(MakeButton("Export Excel (.xlsx)", Dock.Right, ExportExcel));
        toolbar.Children.Add(MakeButton("Export CSV", Dock.Right, ExportCsv));

        root.Children.Add(toolbar);
        root.Children.Add(BuildGrid());

        Content = root;
    }

    private DataGrid BuildGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserSortColumns = true,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Panel,
            Foreground = Text,
            RowBackground = Panel,
            BorderBrush = Muted,
            FontFamily = new FontFamily("Segoe UI"),
            ItemsSource = _rows,
        };

        foreach (var column in ResultsExporter.Columns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new Binding(column.Property) { StringFormat = column.Format },
                SortMemberPath = column.Property,
            });
        }

        return grid;
    }

    private Button MakeButton(string text, Dock dock, Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(12, 6, 12, 6),
        };
        button.Click += (_, _) => onClick();
        DockPanel.SetDock(button, dock);
        return button;
    }

    private void ExportCsv()
    {
        if (!EnsureHasRows()) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export all results to CSV",
            FileName = "rust-fps-results.csv",
            Filter = "CSV files (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) != true) return;

        ResultsExporter.ExportCsv(_rows, dialog.FileName);
        _info.Text = $"Exported {_rows.Count} rows to {dialog.FileName}";
    }

    private void ExportExcel()
    {
        if (!EnsureHasRows()) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export all results to Excel",
            FileName = "rust-fps-results.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
        };
        if (dialog.ShowDialog(this) != true) return;

        ResultsXlsxExporter.Export(_rows, dialog.FileName);
        _info.Text = $"Exported {_rows.Count} rows to {dialog.FileName}";
    }

    private void OpenMaster()
    {
        var path = _store.MasterCsvPath;
        // If no session has been recorded yet, generate it from whatever exists.
        if (!File.Exists(path))
        {
            if (!EnsureHasRows()) return;
            ResultsExporter.ExportCsv(_rows, path);
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(this, path, "Master results file");
        }
    }

    private bool EnsureHasRows()
    {
        if (_rows.Count > 0) return true;
        MessageBox.Show(this, "No sessions recorded yet.", "Results");
        return false;
    }
}

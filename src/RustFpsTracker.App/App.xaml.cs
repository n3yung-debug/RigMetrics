using System.Windows;
using System.Windows.Threading;

namespace RustFpsTracker.App;

public partial class App : Application
{
    public App()
    {
        // Surface unexpected errors instead of silently crashing.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Rust FPS Tracker - Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

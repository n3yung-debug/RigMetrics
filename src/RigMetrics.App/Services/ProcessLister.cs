using System.Diagnostics;

namespace RigMetrics.App.Services;

/// <summary>
/// Lists running applications that are plausible capture targets &mdash; i.e.
/// processes that own a visible window. Returns executable names like
/// "cs2.exe" suitable for PresentMon's --process_name.
/// </summary>
public static class ProcessLister
{
    public static IReadOnlyList<string> ListWindowedApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        string self = Process.GetCurrentProcess().ProcessName;

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                if (string.Equals(process.ProcessName, self, StringComparison.OrdinalIgnoreCase)) continue;

                names.Add(process.ProcessName + ".exe");
            }
            catch
            {
                // Some processes deny access; skip them.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names.ToList();
    }
}

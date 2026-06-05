using System.Globalization;
using System.Text;
using System.Text.Json;
using RustFpsTracker.Core.Models;

namespace RustFpsTracker.Core.Storage;

/// <summary>
/// Persists sessions as JSON files in a folder, and exports them to CSV
/// for analysis in Excel or other tools.
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public string Directory { get; }

    public SessionStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>Default per-user location: %AppData%/RustFpsTracker/sessions.</summary>
    public static SessionStore CreateDefault()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new SessionStore(Path.Combine(baseDir, "RustFpsTracker", "sessions"));
    }

    public string Save(SessionRecording session)
    {
        var path = Path.Combine(Directory, $"{session.Id}.json");
        var json = JsonSerializer.Serialize(session, JsonOptions);
        File.WriteAllText(path, json);
        return path;
    }

    public SessionRecording? Load(string id)
    {
        var path = Path.Combine(Directory, $"{id}.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(path), JsonOptions);
    }

    /// <summary>Loads every saved session, newest first. Corrupt files are skipped.</summary>
    public IReadOnlyList<SessionRecording> LoadAll()
    {
        var sessions = new List<SessionRecording>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var s = JsonSerializer.Deserialize<SessionRecording>(File.ReadAllText(path), JsonOptions);
                if (s is not null) sessions.Add(s);
            }
            catch
            {
                // Ignore unreadable/corrupt files rather than crashing the UI.
            }
        }
        sessions.Sort((a, b) => b.StartedUtc.CompareTo(a.StartedUtc));
        return sessions;
    }

    public bool Delete(string id)
    {
        var path = Path.Combine(Directory, $"{id}.json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Writes one row per frame (frame time, FPS) to a CSV file.</summary>
    public static void ExportFramesCsv(SessionRecording session, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FrameIndex,TimeInSeconds,FrameTimeMs,Fps,Dropped");
        for (int i = 0; i < session.Frames.Count; i++)
        {
            var f = session.Frames[i];
            sb.Append(i).Append(',')
              .Append(F(f.TimeInSeconds)).Append(',')
              .Append(F(f.FrameTimeMs)).Append(',')
              .Append(F(f.Fps)).Append(',')
              .Append(f.Dropped ? '1' : '0')
              .Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Writes one row per second of hardware sensor samples to a CSV file.</summary>
    public static void ExportSensorsCsv(SessionRecording session, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TimestampUtc,CpuLoad%,CpuTempC,GpuLoad%,GpuTempC,GpuMemUsedMb,RamUsedGb,RamLoad%");
        foreach (var s in session.Sensors)
        {
            sb.Append(s.TimestampUtc.ToString("o", CultureInfo.InvariantCulture)).Append(',')
              .Append(F(s.CpuLoadPercent)).Append(',')
              .Append(F(s.CpuTempC)).Append(',')
              .Append(F(s.GpuLoadPercent)).Append(',')
              .Append(F(s.GpuTempC)).Append(',')
              .Append(F(s.GpuMemUsedMb)).Append(',')
              .Append(F(s.RamUsedGb)).Append(',')
              .Append(F(s.RamLoadPercent))
              .Append('\n');
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F(double? v) => v.HasValue ? F(v.Value) : "";
}

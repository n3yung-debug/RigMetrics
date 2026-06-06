using System.IO;
using System.Text.Json;

namespace RigMetrics.App;

/// <summary>
/// Small per-user preferences (separate from appsettings.json) so choices like
/// the last tracked game persist across runs without editing config files.
/// Stored at %AppData%/RigMetrics/preferences.json.
/// </summary>
public sealed class UserPreferences
{
    public string? LastGameProcessName { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static string FilePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RigMetrics");
            return Path.Combine(dir, "preferences.json");
        }
    }

    public static UserPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // Ignore unreadable preferences; fall back to defaults.
        }
        return new UserPreferences();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch
        {
            // Non-fatal if we can't persist.
        }
    }
}

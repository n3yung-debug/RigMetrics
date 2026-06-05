using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace RigMetrics.App.Services;

/// <summary>
/// Ensures PresentMon.exe is available. The packaged release ZIP already ships
/// it next to the app; this is the fallback for people who build from source —
/// it fetches the latest PresentMon x64 build from its official GitHub releases.
/// </summary>
public static class PresentMonProvider
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";

    public static bool Exists(string path) => File.Exists(path);

    /// <summary>
    /// Downloads PresentMon to <paramref name="targetPath"/> if it isn't there yet.
    /// Reports progress text via <paramref name="log"/>. Throws on failure.
    /// </summary>
    public static async Task EnsureAsync(string targetPath, IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (File.Exists(targetPath))
            return;

        log?.Report("Looking up the latest PresentMon release...");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RigMetrics");
        http.Timeout = TimeSpan.FromMinutes(2);

        var json = await http.GetStringAsync(LatestReleaseApi, ct);
        var downloadUrl = FindX64ExeAsset(json)
            ?? throw new InvalidOperationException(
                "Could not find an x64 PresentMon executable in the latest release. " +
                "Download PresentMon.exe manually from " +
                "https://github.com/GameTechDev/PresentMon/releases and place it next to this app.");

        log?.Report("Downloading PresentMon...");
        var bytes = await http.GetByteArrayAsync(downloadUrl, ct);

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllBytesAsync(targetPath, bytes, ct);
        log?.Report("PresentMon is ready.");
    }

    private static string? FindX64ExeAsset(string releaseJson)
    {
        using var doc = JsonDocument.Parse(releaseJson);
        if (!doc.RootElement.TryGetProperty("assets", out var assets))
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.StartsWith("PresentMon", StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && name.Contains("x64", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("arm", StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }
        return null;
    }
}

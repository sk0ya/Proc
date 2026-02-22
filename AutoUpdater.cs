using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Windows;

namespace Proc;

public static class AutoUpdater
{
    private const string RepoApiUrl = "https://api.github.com/repos/sk0ya/Proc/releases/latest";
    private static Timer? _timer;
    private static readonly bool _isEnabled = DetectEnabled();

    private static bool DetectEnabled()
    {
#if DEBUG
        return false;
#else
        var path = Environment.ProcessPath;
        if (string.IsNullOrEmpty(path))
            return false;

        // Safety guard: never self-update binaries running from a Debug output path.
        return path.IndexOf(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) < 0;
#endif
    }

    public static void StartPeriodicCheck(TimeSpan interval)
    {
        if (!_isEnabled) return;
        _timer = new Timer(_ => _ = CheckAndPrompt(), null, interval, interval);
    }

    public static async Task<bool> CheckAndUpdate()
    {
        if (!_isEnabled) return false;
        var result = await DownloadUpdate();
        if (result == null) return false;
        ApplyUpdate(result.Value.tempExe);
        return true;
    }

    private static async Task CheckAndPrompt()
    {
        if (!_isEnabled) return;
        var result = await DownloadUpdate();
        if (result == null) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            ApplyUpdate(result.Value.tempExe);
            Application.Current.Shutdown();
        });
    }

    private static void ApplyUpdate(string tempExe)
    {
        if (!_isEnabled) return;
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return;

        var tempDir = Path.GetDirectoryName(tempExe)!;
        var batPath = Path.Combine(tempDir, "update.bat");
        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /y "{tempExe}" "{currentExe}" >nul
            start "" "{currentExe}"
            del "{tempExe}"
            del "%~f0"
            """;
        File.WriteAllText(batPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = batPath,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static async Task<(string tempExe, string version)?> DownloadUpdate()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Proc-AutoUpdater");
            http.Timeout = TimeSpan.FromSeconds(10);

            var release = await http.GetFromJsonAsync<GitHubRelease>(RepoApiUrl);
            if (release?.TagName == null || release.Assets == null || release.Assets.Length == 0)
                return null;

            var remoteVersion = ParseVersion(release.TagName);
            var localVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (remoteVersion == null || localVersion == null || remoteVersion <= localVersion)
                return null;

            var asset = Array.Find(release.Assets, a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
            if (asset?.BrowserDownloadUrl == null)
                return null;

            var tempDir = Path.Combine(Path.GetTempPath(), "Proc_update");
            Directory.CreateDirectory(tempDir);
            var tempExe = Path.Combine(tempDir, "Proc.exe");

            using (var stream = await http.GetStreamAsync(asset.BrowserDownloadUrl))
            using (var file = File.Create(tempExe))
            {
                await stream.CopyToAsync(file);
            }

            return (tempExe, release.TagName);
        }
        catch
        {
            return null;
        }
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public GitHubAsset[]? Assets { get; set; }
    }

    private class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Windows;

namespace Proc;

public static class AutoUpdater
{
    private const string RepoApiUrl = "https://api.github.com/repos/sk0ya/Proc/releases/latest";
    private static Timer? _timer;
    private static readonly bool _isEnabled = DetectEnabled();

    // Version the user declined in this session; not asked again until a newer one appears
    private static Version? _declinedVersion;
    private static int _checking;

    private record UpdateInfo(Version Version, string ExeUrl, string HashUrl);

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

    /// <summary>Startup check: installs a newer release without asking (nothing is in progress yet).</summary>
    public static async Task<bool> CheckAndUpdate()
    {
        if (!_isEnabled) return false;
        var info = await GetAvailableUpdate();
        if (info == null) return false;
        var tempExe = await DownloadAndVerify(info);
        if (tempExe == null) return false;
        return ApplyUpdate(tempExe);
    }

    /// <summary>Periodic check while running: asks before restarting.</summary>
    private static async Task CheckAndPrompt()
    {
        if (!_isEnabled) return;
        if (Interlocked.Exchange(ref _checking, 1) == 1) return;
        try
        {
            var info = await GetAvailableUpdate();
            if (info == null || info.Version == _declinedVersion) return;

            var accepted = Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                $"Proc {info.Version} is available. Update and restart now?",
                "Proc Update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes);
            if (!accepted)
            {
                _declinedVersion = info.Version;
                return;
            }

            var tempExe = await DownloadAndVerify(info);
            if (tempExe == null)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(
                    "The update could not be downloaded or failed verification.",
                    "Proc Update", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ApplyUpdate(tempExe))
                    Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Update.Periodic", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private static bool ApplyUpdate(string tempExe)
    {
        if (!_isEnabled) return false;
        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe)) return false;

        try
        {
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
            })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Update.Apply", ex);
            return false;
        }
    }

    private static HttpClient CreateClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Proc-AutoUpdater");
        return http;
    }

    private static async Task<UpdateInfo?> GetAvailableUpdate()
    {
        try
        {
            using var http = CreateClient(TimeSpan.FromSeconds(10));
            var release = await http.GetFromJsonAsync<GitHubRelease>(RepoApiUrl);
            if (release?.TagName == null || release.Assets == null || release.Assets.Length == 0)
                return null;

            var remoteVersion = ParseVersion(release.TagName);
            var localVersion = Assembly.GetEntryAssembly()?.GetName().Version;
            if (remoteVersion == null || localVersion == null || remoteVersion <= localVersion)
                return null;

            var exe = Array.Find(release.Assets, a => a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true);
            if (exe?.Name == null || exe.BrowserDownloadUrl == null)
                return null;

            // Releases without a checksum are not installed
            var hash = Array.Find(release.Assets, a =>
                string.Equals(a.Name, exe.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (hash?.BrowserDownloadUrl == null)
                return null;

            return new UpdateInfo(remoteVersion, exe.BrowserDownloadUrl, hash.BrowserDownloadUrl);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Update.Check", ex);
            return null;
        }
    }

    /// <summary>Downloads the exe and returns its path if its SHA256 matches the published checksum.</summary>
    private static async Task<string?> DownloadAndVerify(UpdateInfo info)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Proc_update");
        var tempExe = Path.Combine(tempDir, "Proc.exe");
        try
        {
            using var http = CreateClient(TimeSpan.FromMinutes(5));
            var expected = (await http.GetStringAsync(info.HashUrl)).Trim().Split(' ', '\t', '\r', '\n')[0];

            Directory.CreateDirectory(tempDir);
            using (var stream = await http.GetStreamAsync(info.ExeUrl))
            using (var file = File.Create(tempExe))
            {
                await stream.CopyToAsync(file);
            }

            string actual;
            using (var file = File.OpenRead(tempExe))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file));

            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                ErrorLog.Write("Update.Verify", new InvalidDataException($"SHA256 mismatch: expected {expected}, got {actual}"));
                File.Delete(tempExe);
                return null;
            }
            return tempExe;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Update.Download", ex);
            try { File.Delete(tempExe); } catch { }
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

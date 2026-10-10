using System.IO;
using System.Text.Json;

namespace Proc;

public class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Proc", "settings.json");

    public bool ShowTitle { get; set; } = true;
    public bool ShowWhenIdle { get; set; } = false;
    public bool RunAsAdmin { get; set; } = false;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public string BgColor { get; set; } = "#1E1E1E";
    public string AccentColor { get; set; } = "#FF8C00";
    public List<string> HiddenApps { get; set; } = [];

    /// <summary>Apps whose window titles are never written to the log.</summary>
    public List<string> TitleExcludedApps { get; set; } = [];

    /// <summary>Daily logs older than this many days are deleted. 0 keeps everything.</summary>
    public int LogRetentionDays { get; set; } = 0;

    /// <summary>Category rule lines, e.g. "Work = devenv, code, title:Jira" (see <see cref="CategoryRules"/>).</summary>
    public List<string> CategoryRules { get; set; } = [];

    /// <summary>
    /// Loads the latest settings from disk, applies the change and saves, so that
    /// windows holding older instances don't overwrite each other's changes.
    /// </summary>
    public static void Update(Action<AppSettings> change)
    {
        var settings = Load();
        change(settings);
        settings.Save();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            // Write to a temp file and swap it in, so a crash mid-write can't corrupt the settings
            var tempPath = SettingsPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this));
            File.Move(tempPath, SettingsPath, true);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Settings.Save", ex);
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Settings.Load", ex);
        }
        return new();
    }
}

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
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }
}

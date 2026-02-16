using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Proc;

public partial class SettingsWindow : Window
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Proc";

    private bool _initialized;
    private bool _suppressHexEvent;

    public event Action<bool>? ShowTitleChanged;

    // Background swatch colors
    private static readonly string[] BgColors =
    [
        // Dark
        "#1A1A2E", "#16213E", "#1B1B2F", "#2C2C34",
        "#1E1E1E", "#0F0E17", "#1A1423", "#202B30",
        // Light
        "#FAF9F6", "#F5F0EB", "#EDE8E2", "#E8ECF1",
        "#F0EDE5", "#E9E4DC", "#F2EFF9", "#E6EBE0",
    ];

    // Accent swatch colors
    private static readonly string[] AccentColors =
    [
        // Orange / Gold
        "#E8913A", "#D4A04A",
        // Red / Rose
        "#D95B5B", "#C46B82",
        // Pink / Magenta
        "#D87CAC", "#B854A6",
        // Purple / Violet
        "#9B6ED8", "#7B5EC4",
        // Blue
        "#5B8FD8", "#4AA4C9",
        // Teal / Cyan
        "#3DBAB0", "#48B8A0",
        // Green
        "#5EAE6B", "#8CB454",
        // Yellow / Lime
        "#C9B840", "#D9A830",
    ];

    public SettingsWindow(bool showTitle)
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = IsStartupEnabled();
        ShowTitleCheckBox.IsChecked = showTitle;
        RunAsAdminCheckBox.IsChecked = AppSettings.Load().RunAsAdmin;

        BuildSwatches(BgSwatches, BgColors, BgSwatch_Click);
        BuildSwatches(AccentSwatches, AccentColors, AccentSwatch_Click);

        var settings = AppSettings.Load();
        _suppressHexEvent = true;
        BgHexBox.Text = settings.BgColor;
        AccentHexBox.Text = settings.AccentColor;
        UpdatePreview(BgPreview, settings.BgColor);
        UpdatePreview(AccentPreview, settings.AccentColor);
        _suppressHexEvent = false;

        _initialized = true;
    }

    private void BuildSwatches(WrapPanel panel, string[] colors, RoutedEventHandler clickHandler)
    {
        foreach (var hex in colors)
        {
            var color = ColorTheme.ParseHex(hex);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonDown += (s, e) =>
            {
                clickHandler(s, e);
                e.Handled = true;
            };
            panel.Children.Add(swatch);
        }
    }

    private void BgSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border { Tag: string hex })
            BgHexBox.Text = hex;
    }

    private void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border { Tag: string hex })
            AccentHexBox.Text = hex;
    }

    private void BgHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexEvent || !_initialized) return;
        var text = BgHexBox.Text;
        if (!ColorTheme.IsValidHex(text)) return;
        UpdatePreview(BgPreview, text);
        ApplyAndSave();
    }

    private void AccentHexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressHexEvent || !_initialized) return;
        var text = AccentHexBox.Text;
        if (!ColorTheme.IsValidHex(text)) return;
        UpdatePreview(AccentPreview, text);
        ApplyAndSave();
    }

    private void ApplyAndSave()
    {
        var bgText = BgHexBox.Text;
        var accentText = AccentHexBox.Text;
        if (!ColorTheme.IsValidHex(bgText) || !ColorTheme.IsValidHex(accentText)) return;

        var bg = ColorTheme.ParseHex(bgText);
        var accent = ColorTheme.ParseHex(accentText);
        var theme = ColorTheme.FromColors(bg, accent);
        theme.Apply();

        var settings = AppSettings.Load();
        settings.BgColor = bgText.StartsWith('#') ? bgText : "#" + bgText;
        settings.AccentColor = accentText.StartsWith('#') ? accentText : "#" + accentText;
        settings.Save();
    }

    private static void UpdatePreview(Border preview, string hex)
    {
        if (ColorTheme.IsValidHex(hex))
            preview.Background = new SolidColorBrush(ColorTheme.ParseHex(hex));
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false);
        return key?.GetValue(AppName) is string;
    }

    private static void SetStartupEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exePath))
                key.SetValue(AppName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    private void StartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        SetStartupEnabled(StartupCheckBox.IsChecked == true);
    }

    private void ShowTitleCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        ShowTitleChanged?.Invoke(ShowTitleCheckBox.IsChecked == true);
    }

    private void RunAsAdminCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        var settings = AppSettings.Load();
        settings.RunAsAdmin = RunAsAdminCheckBox.IsChecked == true;
        settings.Save();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        Close();
    }
}

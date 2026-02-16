using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Proc;

public partial class SettingsWindow : Window
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Proc";

    private bool _initialized;

    public event Action<bool>? ShowTitleChanged;

    public SettingsWindow(bool showTitle)
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = IsStartupEnabled();
        ShowTitleCheckBox.IsChecked = showTitle;
        RunAsAdminCheckBox.IsChecked = AppSettings.Load().RunAsAdmin;

        // Populate theme combo
        var currentTheme = AppSettings.Load().ThemeName;
        foreach (var theme in ColorTheme.All)
        {
            var item = new ComboBoxItem
            {
                Content = theme.DisplayName,
                Tag = new SolidColorBrush(theme.Accent),
                DataContext = theme,
            };
            ThemeComboBox.Items.Add(item);
            if (theme.Name == currentTheme)
                ThemeComboBox.SelectedItem = item;
        }

        _initialized = true;
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

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        if (ThemeComboBox.SelectedItem is ComboBoxItem { DataContext: ColorTheme theme })
        {
            theme.Apply();
            var settings = AppSettings.Load();
            settings.ThemeName = theme.Name;
            settings.Save();
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}

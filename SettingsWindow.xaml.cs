using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Proc;

public partial class SettingsWindow : Window
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Proc";

    public event Action<bool>? ShowTitleChanged;

    public SettingsWindow(bool showTitle)
    {
        InitializeComponent();
        StartupCheckBox.IsChecked = IsStartupEnabled();
        ShowTitleCheckBox.IsChecked = showTitle;
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

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }
}

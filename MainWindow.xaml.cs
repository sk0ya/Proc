using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Proc;

public partial class MainWindow : Window
{
    private readonly ActivityLogger _logger;
    private readonly AppSettings _settings;
    private bool _showTitle;
    private AnalysisWindow? _analysisWindow;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _showTitle = _settings.ShowTitle;
        ToggleTitleMenu.IsChecked = _showTitle;

        _logger = new ActivityLogger();
        _logger.OnRecorded += () => Dispatcher.Invoke(() =>
        {
            RefreshList();
            if (_analysisWindow is { IsLoaded: true })
                _analysisWindow.Refresh();
        });
        _logger.OnActiveChanged += () => Dispatcher.Invoke(RefreshList);

        Loaded += (_, _) =>
        {
            _logger.StartForegroundHook();

            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        };

        RefreshList();
    }

    private void ToggleVisibility()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    private void RefreshList()
    {
        var records = _logger.GetTodayRecords();
        var titleVis = _showTitle ? Visibility.Visible : Visibility.Collapsed;
        var activeProc = _logger.CurrentProcessName;
        var activeTitle = _logger.CurrentWindowTitle;

        List<ActivityRow> grouped;
        if (_showTitle)
        {
            grouped = records
                .GroupBy(r => (r.ProcessName, r.WindowTitle))
                .Select(g => new ActivityRow(g.Count(), g.Key.ProcessName, g.Key.WindowTitle, titleVis,
                    g.Key.ProcessName == activeProc && g.Key.WindowTitle == activeTitle,
                    IconHelper.GetIcon(_logger.GetExePath(g.Key.ProcessName))))
                .OrderByDescending(x => x.Minutes)
                .ToList();
        }
        else
        {
            grouped = records
                .GroupBy(r => r.ProcessName)
                .Select(g => new ActivityRow(g.Count(), g.Key, "", titleVis,
                    g.Key == activeProc,
                    IconHelper.GetIcon(_logger.GetExePath(g.Key))))
                .OrderByDescending(x => x.Minutes)
                .ToList();
        }
        ActivityList.ItemsSource = grouped;
    }

    private void SetShowTitle(bool value)
    {
        _showTitle = value;
        ToggleTitleMenu.IsChecked = value;
        _settings.ShowTitle = value;
        _settings.Save();
        RefreshList();
    }

    private void ToggleTitle_Click(object sender, RoutedEventArgs e)
    {
        SetShowTitle(ToggleTitleMenu.IsChecked);
    }

    private void TrayIcon_LeftClick(object sender, RoutedEventArgs e)
    {
        ToggleVisibility();
    }

    private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
    {
        ToggleVisibility();
    }

    private void Analysis_Click(object sender, RoutedEventArgs e)
    {
        if (_analysisWindow == null || !_analysisWindow.IsLoaded)
        {
            _analysisWindow = new AnalysisWindow(
                _logger.LogDirectory,
                processName => IconHelper.GetIcon(_logger.GetExePath(processName)));
            _analysisWindow.Closed += (_, _) => _analysisWindow = null;
        }
        _analysisWindow.Show();
        _analysisWindow.Activate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_showTitle);
            _settingsWindow.ShowTitleChanged += SetShowTitle;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SetShowTitle(!_showTitle);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

public record ActivityRow(int Minutes, string Process, string Title, Visibility TitleVisibility, bool IsActive, ImageSource? Icon)
{
    public string TimeDisplay => Minutes >= 60 ? $"{Minutes / 60}h {Minutes % 60:D2}m" : $"{Minutes}m";
}

using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Proc;

public partial class MainWindow : Window
{
    private static readonly TimeSpan PostInputVisibilityDuration = TimeSpan.FromSeconds(5);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private readonly ActivityLogger _logger;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _autoHideTimer;
    private bool _showTitle;
    private bool _showWhenIdle;
    private bool _autoShownForIdle;
    private bool _autoShownWindowInteracted;
    private DateTime? _autoHideAfter;
    private AnalysisWindow? _analysisWindow;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();

        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoHideTimer.Tick += (_, _) => HideAutoShownIfReady();

        _settings = AppSettings.Load();
        _showTitle = _settings.ShowTitle;
        _showWhenIdle = _settings.ShowWhenIdle;
        ToggleTitleMenu.IsChecked = _showTitle;

        _logger = new ActivityLogger();
        _logger.OnRecorded += () => Dispatcher.Invoke(() =>
        {
            RefreshList();
            if (_analysisWindow is { IsLoaded: true })
                _analysisWindow.Refresh();
        });
        _logger.OnActiveChanged += () => Dispatcher.Invoke(RefreshList);
        _logger.OnIdleChanged += isIdle => Dispatcher.Invoke(() => ApplyIdleVisibility(isIdle));

        Loaded += (_, _) =>
        {
            _logger.StartForegroundHook();
            ApplyIdleVisibility(_logger.IsIdle);

            if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue)
            {
                Left = _settings.WindowLeft.Value;
                Top = _settings.WindowTop.Value;
            }
            else
            {
                var area = SystemParameters.WorkArea;
                Left = area.Right - ActualWidth;
                Top = area.Bottom - ActualHeight;
            }

            LocationChanged += (_, _) =>
            {
                _settings.WindowLeft = Left;
                _settings.WindowTop = Top;
                _settings.Save();
            };
        };

        RefreshList();
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            _autoShownForIdle = false;
            _autoShownWindowInteracted = false;
            _autoHideAfter = null;
            _autoHideTimer.Stop();
            Hide();
            return;
        }

        if (_showWhenIdle)
        {
            _autoShownForIdle = true;
            _autoShownWindowInteracted = true;
            _autoHideAfter = DateTime.Now + PostInputVisibilityDuration;
            _autoHideTimer.Start();
        }

        Show();
        Activate();
    }

    private void RefreshList()
    {
        var records = _logger.GetTodayRecords().Where(r => !r.IsIdle).ToList();
        TotalTimeValueText.Text = LogAnalyzer.FormatTime(records.Count);
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
                    IconHelper.GetIconByProcessName(g.Key.ProcessName)))
                .OrderByDescending(x => x.Minutes)
                .ToList();
        }
        else
        {
            grouped = records
                .GroupBy(r => r.ProcessName)
                .Select(g => new ActivityRow(g.Count(), g.Key, "", titleVis,
                    g.Key == activeProc,
                    IconHelper.GetIconByProcessName(g.Key)))
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

    private void SetShowWhenIdle(bool value)
    {
        _showWhenIdle = value;
        _settings.ShowWhenIdle = value;
        _settings.Save();

        if (!value)
        {
            _autoHideAfter = null;
            _autoHideTimer.Stop();
        }

        if (!value && _autoShownForIdle)
        {
            _autoShownForIdle = false;
            _autoShownWindowInteracted = false;
            Hide();
            return;
        }

        ApplyIdleVisibility(_logger.IsIdle);
    }

    private void ApplyIdleVisibility(bool isIdle)
    {
        if (!_showWhenIdle) return;

        if (isIdle)
        {
            _autoHideAfter = null;
            if (!IsVisible)
            {
                _autoShownForIdle = true;
                _autoShownWindowInteracted = false;
                Show();
            }
            if (_autoShownForIdle)
                _autoHideTimer.Start();
            return;
        }

        if (_autoShownForIdle)
        {
            _autoHideAfter ??= DateTime.Now + PostInputVisibilityDuration;
            _autoHideTimer.Start();
            HideAutoShownIfReady();
            return;
        }

        if (IsVisible)
            Hide();
    }

    private void HideAutoShownIfReady()
    {
        if (!_autoShownForIdle)
        {
            _autoHideAfter = null;
            _autoHideTimer.Stop();
            return;
        }

        if (_logger.IsIdle)
            return;

        if (ShouldKeepVisibleForProcActivity())
            return;

        _autoHideAfter ??= DateTime.Now + PostInputVisibilityDuration;
        if (DateTime.Now < _autoHideAfter)
            return;

        _autoShownForIdle = false;
        _autoShownWindowInteracted = false;
        _autoHideAfter = null;
        _autoHideTimer.Stop();
        Hide();
    }

    private bool ShouldKeepVisibleForProcActivity()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero) return false;

        var mainWindow = new WindowInteropHelper(this).Handle;
        if (foregroundWindow == mainWindow)
            return _autoShownWindowInteracted;

        GetWindowThreadProcessId(foregroundWindow, out var processId);
        return processId == Environment.ProcessId;
    }

    private void ToggleTitle_Click(object sender, RoutedEventArgs e)
    {
        SetShowTitle(ToggleTitleMenu.IsChecked);
    }

    private void Window_UserInteracted(object sender, RoutedEventArgs e)
    {
        if (_autoShownForIdle)
        {
            _autoShownWindowInteracted = true;
            _autoHideAfter ??= DateTime.Now + PostInputVisibilityDuration;
            _autoHideTimer.Start();
        }
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
            _analysisWindow = new AnalysisWindow(_logger.LogDirectory);
            _analysisWindow.Closed += (_, _) => _analysisWindow = null;
        }
        _analysisWindow.Show();
        _analysisWindow.Activate();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow(_showTitle, _showWhenIdle);
            _settingsWindow.ShowTitleChanged += SetShowTitle;
            _settingsWindow.ShowWhenIdleChanged += SetShowWhenIdle;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "Import Activity Log"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                int count = ImportHelper.Import(dlg.FileName, _logger.LogDirectory);
                MessageBox.Show(this, $"{count} records imported.", "Import", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshList();
                if (_analysisWindow is { IsLoaded: true })
                    _analysisWindow.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Import failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
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
    public string TimeDisplay => LogAnalyzer.FormatTime(Minutes);
}

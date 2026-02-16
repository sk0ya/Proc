using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Proc;

public partial class AnalysisWindow : Window
{
    private readonly AnalysisViewModel _vm;

    public AnalysisWindow(string logDirectory)
    {
        InitializeComponent();
        _vm = new AnalysisViewModel(logDirectory);
        DataContext = _vm;
        _vm.PropertyChanged += Vm_PropertyChanged;
    }

    public void Refresh() => _vm.Refresh();

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnalysisViewModel.TimelineBlocks)
            or nameof(AnalysisViewModel.ProcessColors)
            or nameof(AnalysisViewModel.IsDayView))
        {
            RenderTimeline();
        }
    }

    private void Period_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        if (DayRadio.IsChecked == true) _vm.SetPeriod(AnalysisPeriod.Day);
        else if (WeekRadio.IsChecked == true) _vm.SetPeriod(AnalysisPeriod.Week);
        else if (MonthRadio.IsChecked == true) _vm.SetPeriod(AnalysisPeriod.Month);
    }

    private void PrevPeriod_Click(object sender, RoutedEventArgs e) => _vm.NavigatePrevious();
    private void NextPeriod_Click(object sender, RoutedEventArgs e) => _vm.NavigateNext();

    private void PeriodLabel_Click(object sender, RoutedEventArgs e)
    {
        DateCalendar.SelectedDate = _vm.ReferenceDate;
        DateCalendar.DisplayDate = _vm.ReferenceDate;
        CalendarPopup.IsOpen = true;
    }

    private void DateCalendar_SelectedDatesChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (DateCalendar.SelectedDate is DateTime selected)
        {
            CalendarPopup.IsOpen = false;
            _vm.SetDate(selected);
        }
    }

    private void CalendarToday_Click(object sender, RoutedEventArgs e)
    {
        CalendarPopup.IsOpen = false;
        _vm.NavigateToday();
    }

    private void AllButton_Click(object sender, MouseButtonEventArgs e)
    {
        _vm.SelectAll();
    }

    private void AppBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppBarItem item)
            _vm.SelectApp(item.ProcessName);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left:
                _vm.NavigatePrevious();
                break;
            case Key.Right:
                _vm.NavigateNext();
                break;
        }
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RenderTimeline();
    }

    private void RenderTimeline()
    {
        TimelineCanvas.Children.Clear();
        TimelineHourLabels.Children.Clear();

        if (!_vm.IsDayView || _vm.TimelineBlocks.Count == 0)
            return;

        double canvasWidth = TimelineCanvas.ActualWidth;
        double canvasHeight = TimelineCanvas.ActualHeight;
        if (canvasWidth <= 0) return;

        // Determine the time range from data
        var blocks = _vm.TimelineBlocks;
        var minTime = blocks.Min(b => b.StartTime);
        var maxTime = blocks.Max(b => b.EndTime);

        // Snap to hour boundaries with padding
        int startHour = Math.Max(0, minTime.Hours);
        int endHour = Math.Min(24, maxTime.Hours + 1);
        if (endHour <= startHour) endHour = startHour + 1;

        var rangeStart = TimeSpan.FromHours(startHour);
        var rangeEnd = TimeSpan.FromHours(endHour);
        double totalMinutes = (rangeEnd - rangeStart).TotalMinutes;

        var colors = _vm.ProcessColors;
        var defaultBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
        defaultBrush.Freeze();

        // Draw blocks
        foreach (var block in blocks)
        {
            double x1 = (block.StartTime - rangeStart).TotalMinutes / totalMinutes * canvasWidth;
            double x2 = (block.EndTime - rangeStart).TotalMinutes / totalMinutes * canvasWidth;
            double width = Math.Max(2, x2 - x1); // minimum 2px so tiny blocks are visible

            var brush = colors.TryGetValue(block.ProcessName, out var b) ? b : defaultBrush;

            var rect = new Rectangle
            {
                Width = width,
                Height = canvasHeight,
                Fill = brush,
                RadiusX = 1,
                RadiusY = 1,
                ToolTip = BuildTooltip(block),
                Cursor = Cursors.Hand
            };
            rect.MouseLeftButtonDown += (_, _) => _vm.SelectApp(block.ProcessName);

            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, 0);
            TimelineCanvas.Children.Add(rect);
        }

        // Draw hour labels
        var labelBrush = (SolidColorBrush)FindResource("ThemeText");
        var tickBrush = (SolidColorBrush)FindResource("ThemeBorder");

        for (int h = startHour; h <= endHour; h++)
        {
            double x = (h - startHour) * 60.0 / totalMinutes * canvasWidth;

            // Tick line on timeline canvas
            var tick = new Line
            {
                X1 = x, X2 = x,
                Y1 = 0, Y2 = canvasHeight,
                Stroke = tickBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            TimelineCanvas.Children.Add(tick);

            // Hour label
            var label = new TextBlock
            {
                Text = $"{h}:00",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = labelBrush
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, 0);
            TimelineHourLabels.Children.Add(label);
        }
    }

    private static object BuildTooltip(TimelineBlock block)
    {
        var start = $"{block.StartTime.Hours:D2}:{block.StartTime.Minutes:D2}";
        var end = $"{block.EndTime.Hours:D2}:{block.EndTime.Minutes:D2}";
        var title = block.WindowTitle.Length > 60
            ? block.WindowTitle[..57] + "..."
            : block.WindowTitle;
        if (string.IsNullOrWhiteSpace(title)) title = "(no title)";

        var sp = new StackPanel { MaxWidth = 350 };
        var res = Application.Current.Resources;
        sp.Children.Add(new TextBlock
        {
            Text = block.ProcessName,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)res["ThemeAccent"]
        });
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)res["ThemeText"]
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"{start} - {end} ({LogAnalyzer.FormatTime(block.Minutes)})",
            FontFamily = new FontFamily("Consolas"),
            Foreground = (SolidColorBrush)res["ThemeSubText"],
            Margin = new Thickness(0, 2, 0, 0)
        });
        return new ToolTip
        {
            Content = sp,
            Background = (SolidColorBrush)res["ThemeSurfaceAlt"],
            BorderBrush = (SolidColorBrush)res["ThemeCheckBorder"],
            Padding = new Thickness(8, 6, 8, 6)
        };
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

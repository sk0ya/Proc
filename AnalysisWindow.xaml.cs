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

    // Timeline geometry from the last render (used to map x <-> time of day)
    private TimeSpan _timelineStart;
    private double _timelineMinutes;

    // Drag-to-select state
    private const double DragThreshold = 3;
    private Point? _dragOrigin;
    private TimelineBlock? _dragOriginBlock;
    private bool _isDragging;
    private const double EdgeGrabWidth = 8;
    private const double HandleWidth = 6;
    private static readonly object SelectionTag = new();

    // Hover cursor line + time readout
    private Line? _hoverLine;
    private TextBlock? _hoverLabel;

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
            or nameof(AnalysisViewModel.IsDayView)
            or nameof(AnalysisViewModel.HasTimeRange)
            or nameof(AnalysisViewModel.SelectedAppName))
        {
            RenderTimeline();
        }

        if (e.PropertyName is nameof(AnalysisViewModel.DailyStacks)
            or nameof(AnalysisViewModel.ProcessColors)
            or nameof(AnalysisViewModel.SelectedAppName))
        {
            RenderDailyChart();
        }
    }

    private void GroupMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        if (GroupAppRadio.IsChecked == true) _vm.SetGroupMode(GroupMode.App);
        else if (GroupCategoryRadio.IsChecked == true) _vm.SetGroupMode(GroupMode.Category);
        else if (GroupSiteRadio.IsChecked == true) _vm.SetGroupMode(GroupMode.Site);
    }

    private void ExportButton_Click(object sender, MouseButtonEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export",
            FileName = _vm.ExportFileName,
            Filter = "CSV (*.csv)|*.csv|Markdown (*.md)|*.md",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _vm.Export(dlg.FileName);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Export", ex);
            MessageBox.Show(this, $"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void FilterButton_Click(object sender, MouseButtonEventArgs e)
    {
        // Opened on mouse-up: a StaysOpen=False popup opened on mouse-down is closed again immediately
        _vm.AppFilterQuery = "";
        PositionFilterPopup();
        FilterPopup.IsOpen = true;
        FilterSearchBox.Focus();
        e.Handled = true;
    }

    // The button label ("Filter (2 hidden)") changes width; keep the popup pinned to the right edge
    private void FilterBar_SizeChanged(object sender, SizeChangedEventArgs e) => PositionFilterPopup();

    /// <summary>Aligns the popup's right edge with the right edge of the Filter / All buttons.</summary>
    private void PositionFilterPopup()
    {
        double popupWidth = ((FrameworkElement)FilterPopup.Child).Width;
        FilterPopup.HorizontalOffset = FilterBar.ActualWidth - popupWidth;
        FilterPopup.VerticalOffset = FilterBar.ActualHeight + 2;
    }

    private void AppFilterCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: AppFilterItem item } cb)
            _vm.SetAppVisible(item.ProcessName, cb.IsChecked == true);
    }

    private void ShowAllApps_Click(object sender, RoutedEventArgs e) => _vm.ShowAllApps();

    private void HideApp_Click(object sender, RoutedEventArgs e)
    {
        // Hiding works on app names; in Category / Site mode the rows aren't apps
        if (_vm.IsAppMode && sender is FrameworkElement { DataContext: AppBarItem item } && item.ProcessName != "Other")
            _vm.SetAppVisible(item.ProcessName, false);
    }

    private void ShowOnlyApp_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsAppMode && sender is FrameworkElement { DataContext: AppBarItem item } && item.ProcessName != "Other")
            _vm.ShowOnlyApp(item.ProcessName);
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
        // Let text input (filter search box) handle its own keys
        if (e.OriginalSource is TextBox) return;

        switch (e.Key)
        {
            case Key.Escape:
                if (_vm.HasTimeRange) _vm.ClearTimeRange();
                else Close();
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

    private void ClearRange_Click(object sender, MouseButtonEventArgs e) => _vm.ClearTimeRange();

    private void TimelineArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_timelineMinutes <= 0) return;

        var pos = e.GetPosition(TimelineArea);
        var block = (e.OriginalSource as FrameworkElement)?.Tag as TimelineBlock;

        // Double-click a block to select its time span
        if (e.ClickCount == 2)
        {
            if (block != null) _vm.SetTimeRange(block.StartTime, block.EndTime);
            e.Handled = true;
            return;
        }

        // Grabbing an edge of the current selection resizes it from the opposite edge
        var edge = HitRangeEdge(pos);
        if (edge is double anchorX)
        {
            _dragOrigin = new Point(anchorX, pos.Y);
            _dragOriginBlock = null;
            _isDragging = true;
        }
        else
        {
            _dragOrigin = pos;
            _dragOriginBlock = block;
            _isDragging = false;
        }

        HideHover();
        TimelineArea.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_timelineMinutes <= 0) return;
        var pos = e.GetPosition(TimelineArea);

        if (_dragOrigin is not Point origin || !TimelineArea.IsMouseCaptured)
        {
            UpdateHover(pos.X);
            TimelineArea.Cursor = HitRangeEdge(pos) != null ? Cursors.SizeWE : Cursors.IBeam;
            return;
        }

        if (!_isDragging && Math.Abs(pos.X - origin.X) < DragThreshold) return;
        _isDragging = true;

        var (start, end) = GetDragRange(origin.X, pos.X);
        DrawSelection(start, end);
        SetRangeLabel(start, end);
    }

    private void TimelineArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragOrigin is not Point origin) return;

        var pos = e.GetPosition(TimelineArea);
        bool wasDragging = _isDragging;
        var clickedBlock = _dragOriginBlock;
        EndDrag();
        e.Handled = true;

        if (wasDragging)
        {
            var (start, end) = GetDragRange(origin.X, pos.X);
            _vm.SetTimeRange(start, end);
        }
        else if (pos.Y >= TimelineCanvas.ActualHeight)
        {
            // Click on the hour-label row selects that whole hour
            var (start, _) = GetDragRange(pos.X, pos.X);
            var hourStart = TimeSpan.FromHours(Math.Floor(start.TotalHours));
            var hourEnd = hourStart + TimeSpan.FromHours(1);
            if (hourEnd > _timelineStart + TimeSpan.FromMinutes(_timelineMinutes))
                hourEnd = _timelineStart + TimeSpan.FromMinutes(_timelineMinutes);
            _vm.SetTimeRange(hourStart, hourEnd);
        }
        else if (clickedBlock != null)
        {
            _vm.SelectApp(clickedBlock.ProcessName);
        }
        else if (_vm.HasTimeRange)
        {
            _vm.ClearTimeRange();
        }
        else
        {
            _vm.SelectAll();
        }
    }

    private void TimelineArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!TimelineArea.IsMouseCaptured) HideHover();
    }

    private void TimelineArea_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // Capture lost mid-drag (e.g. window deactivated): cancel the drag
        if (_dragOrigin == null) return;
        EndDrag();
        RenderTimeline();
    }

    private void EndDrag()
    {
        _dragOrigin = null;
        _dragOriginBlock = null;
        _isDragging = false;
        if (TimelineArea.IsMouseCaptured)
            TimelineArea.ReleaseMouseCapture();
    }

    /// <summary>
    /// Returns the x of the opposite edge when pos is near an edge of the selected range.
    /// Only the block row has handles; the hour-label row below is for hour clicks.
    /// </summary>
    private double? HitRangeEdge(Point pos)
    {
        if (_vm.RangeStart is not TimeSpan start || _vm.RangeEnd is not TimeSpan end) return null;
        if (pos.Y >= TimelineCanvas.ActualHeight) return null;
        double x = pos.X;
        double sx1 = TimeToX(start);
        double sx2 = TimeToX(end);
        if (Math.Abs(x - sx1) <= EdgeGrabWidth) return sx2;
        if (Math.Abs(x - sx2) <= EdgeGrabWidth) return sx1;
        return null;
    }

    private void UpdateHover(double x)
    {
        double width = TimelineCanvas.ActualWidth;
        if (x < 0 || x > width) { HideHover(); return; }

        var time = _timelineStart + TimeSpan.FromMinutes(Math.Floor(x / width * _timelineMinutes));

        if (_hoverLine == null || _hoverLabel == null)
        {
            var accent = (SolidColorBrush)FindResource("ThemeAccent");
            _hoverLine = new Line { Stroke = accent, StrokeThickness = 1 };
            _hoverLabel = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = (Brush)FindResource("ThemeBg"),
                Background = accent,
                Padding = new Thickness(3, 0, 3, 0)
            };
            TimelineHover.Children.Add(_hoverLine);
            TimelineHover.Children.Add(_hoverLabel);
        }

        _hoverLine.X1 = _hoverLine.X2 = x;
        _hoverLine.Y1 = 0;
        _hoverLine.Y2 = TimelineCanvas.ActualHeight;

        _hoverLabel.Text = LogAnalyzer.FormatClock(time);
        _hoverLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double labelWidth = _hoverLabel.DesiredSize.Width;
        Canvas.SetLeft(_hoverLabel, Math.Clamp(x - labelWidth / 2, 0, Math.Max(0, width - labelWidth)));
        Canvas.SetTop(_hoverLabel, TimelineCanvas.ActualHeight + 1);

        TimelineHover.Visibility = Visibility.Visible;
    }

    private void HideHover() => TimelineHover.Visibility = Visibility.Collapsed;

    /// <summary>Converts two x positions into a minute-snapped time range.</summary>
    private (TimeSpan Start, TimeSpan End) GetDragRange(double xa, double xb)
    {
        double width = TimelineCanvas.ActualWidth;
        double lo = Math.Clamp(Math.Min(xa, xb), 0, width);
        double hi = Math.Clamp(Math.Max(xa, xb), 0, width);

        int startMin = (int)Math.Floor(lo / width * _timelineMinutes);
        int endMin = (int)Math.Ceiling(hi / width * _timelineMinutes);
        if (endMin <= startMin) endMin = startMin + 1;

        return (_timelineStart + TimeSpan.FromMinutes(startMin),
                _timelineStart + TimeSpan.FromMinutes(endMin));
    }

    private double TimeToX(TimeSpan time) =>
        (time - _timelineStart).TotalMinutes / _timelineMinutes * TimelineCanvas.ActualWidth;

    private void UpdateRangeLabel()
    {
        if (_vm.RangeStart is TimeSpan start && _vm.RangeEnd is TimeSpan end)
        {
            SetRangeLabel(start, end);
            ClearRangeButton.Visibility = Visibility.Visible;
        }
        else
        {
            RangeLabel.Text = "Drag: select range / Click hour: select hour";
            RangeLabel.Foreground = (Brush)FindResource("ThemeDimText");
            ClearRangeButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SetRangeLabel(TimeSpan start, TimeSpan end)
    {
        RangeLabel.Text = $"{LogAnalyzer.FormatClock(start)} - {LogAnalyzer.FormatClock(end)}" +
                          $" ({LogAnalyzer.FormatTime((int)(end - start).TotalMinutes)})";
        RangeLabel.Foreground = (Brush)FindResource("ThemeAccent");
    }

    private void RenderTimeline()
    {
        TimelineCanvas.Children.Clear();
        TimelineHourLabels.Children.Clear();
        _timelineMinutes = 0;
        UpdateRangeLabel();

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
        _timelineStart = rangeStart;
        _timelineMinutes = totalMinutes;

        var colors = _vm.ProcessColors;
        var selectedApp = _vm.SelectedAppName;
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
                Tag = block,
                // Dim blocks of other apps while one app is selected
                Opacity = selectedApp == "" || selectedApp == block.ProcessName ? 1.0 : 0.3
            };

            Canvas.SetLeft(rect, x1);
            Canvas.SetTop(rect, 0);
            TimelineCanvas.Children.Add(rect);
        }

        if (_vm.RangeStart is TimeSpan selStart && _vm.RangeEnd is TimeSpan selEnd)
            DrawSelection(selStart, selEnd);

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
            // Keep the last label inside the canvas instead of clipping it
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Min(x, canvasWidth - label.DesiredSize.Width));
            Canvas.SetTop(label, 0);
            TimelineHourLabels.Children.Add(label);
        }
    }

    /// <summary>
    /// Draws the selection for [start, end): shades the outside, outlines the inside and
    /// puts grab handles on both edges. Replaces any previously drawn selection.
    /// </summary>
    private void DrawSelection(TimeSpan start, TimeSpan end)
    {
        var old = TimelineCanvas.Children.OfType<FrameworkElement>()
            .Where(el => el.Tag == SelectionTag).ToList();
        foreach (var el in old) TimelineCanvas.Children.Remove(el);

        double width = TimelineCanvas.ActualWidth;
        double height = TimelineCanvas.ActualHeight;
        double sx1 = Math.Clamp(TimeToX(start), 0, width);
        double sx2 = Math.Clamp(TimeToX(end), 0, width);

        var accent = (SolidColorBrush)FindResource("ThemeAccent");
        var bg = (SolidColorBrush)FindResource("ThemeBg");
        var shade = new SolidColorBrush(bg.Color) { Opacity = 0.7 };
        shade.Freeze();

        AddSelectionRect(0, sx1, height, 0, shade, null);
        AddSelectionRect(sx2, width - sx2, height, 0, shade, null);
        AddSelectionRect(sx1, sx2 - sx1, height, 0, null, accent);

        // Edge handles (centered on the edge, with a grip line in the middle)
        foreach (var x in new[] { sx1, sx2 })
        {
            double hx = Math.Clamp(x - HandleWidth / 2, 0, width - HandleWidth);
            AddSelectionRect(hx, HandleWidth, height, 2, accent, null);
            var grip = new Line
            {
                X1 = hx + HandleWidth / 2, X2 = hx + HandleWidth / 2,
                Y1 = height * 0.3, Y2 = height * 0.7,
                Stroke = bg,
                StrokeThickness = 1,
                IsHitTestVisible = false,
                Tag = SelectionTag
            };
            TimelineCanvas.Children.Add(grip);
        }
    }

    private void AddSelectionRect(double x, double width, double height, double radius, Brush? fill, Brush? stroke)
    {
        if (width <= 0) return;
        var rect = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = stroke != null ? 1.5 : 0,
            RadiusX = radius,
            RadiusY = radius,
            IsHitTestVisible = false,
            Tag = SelectionTag
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, 0);
        TimelineCanvas.Children.Add(rect);
    }

    private void DailyCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderDailyChart();

    /// <summary>Draws one stacked bar per day (Week / Month views); clicking a bar opens that day.</summary>
    private void RenderDailyChart()
    {
        DailyCanvas.Children.Clear();
        DailyLabels.Children.Clear();

        var stacks = _vm.DailyStacks;
        double width = DailyCanvas.ActualWidth;
        double height = DailyCanvas.ActualHeight;
        if (_vm.IsDayView || stacks.Count == 0 || width <= 0 || height <= 0) return;

        int maxMinutes = Math.Max(1, stacks.Max(s => s.TotalMinutes));
        double slot = width / stacks.Count;
        double barWidth = Math.Max(2, Math.Min(40, slot * 0.7));
        var colors = _vm.ProcessColors;
        var selectedApp = _vm.SelectedAppName;
        var defaultBrush = new SolidColorBrush(BarPalette.GetColor(15));
        defaultBrush.Freeze();
        var labelBrush = (SolidColorBrush)FindResource("ThemeText");
        var dimBrush = (SolidColorBrush)FindResource("ThemeDimText");
        // Month views get crowded: label every few days
        int labelEvery = slot < 24 ? (int)Math.Ceiling(24 / slot) : 1;

        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            double x = i * slot + (slot - barWidth) / 2;
            double y = height;

            foreach (var seg in stack.Segments)
            {
                double h = seg.Minutes / (double)maxMinutes * height;
                y -= h;
                var rect = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(0.5, h),
                    Fill = colors.TryGetValue(seg.ProcessName, out var b) ? b : defaultBrush,
                    Opacity = selectedApp == "" || selectedApp == seg.ProcessName ? 1.0 : 0.3,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                DailyCanvas.Children.Add(rect);
            }

            // Full-height transparent hit area with the day's breakdown as tooltip
            var hit = new Rectangle
            {
                Width = slot,
                Height = height,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = stack.Date,
                ToolTip = BuildDayTooltip(stack)
            };
            hit.MouseLeftButtonUp += DailyBar_Click;
            Canvas.SetLeft(hit, i * slot);
            DailyCanvas.Children.Add(hit);

            if (i % labelEvery != 0) continue;
            var label = new TextBlock
            {
                Text = stacks.Count <= 7 ? stack.Date.ToString("MM/dd ddd") : stack.Date.Day.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = stack.Date == DateTime.Today ? (Brush)FindResource("ThemeAccent")
                    : stack.TotalMinutes > 0 ? labelBrush : dimBrush
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(i * slot + slot / 2 - label.DesiredSize.Width / 2, 0,
                Math.Max(0, width - label.DesiredSize.Width)));
            DailyLabels.Children.Add(label);
        }
    }

    private void DailyBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DateTime date }) return;
        _vm.SetDate(date);
        DayRadio.IsChecked = true;
    }

    private static object BuildDayTooltip(DailyStack stack)
    {
        var res = Application.Current.Resources;
        var sp = new StackPanel { MaxWidth = 350 };
        sp.Children.Add(new TextBlock
        {
            Text = $"{stack.Date:yyyy/MM/dd (ddd)}  {LogAnalyzer.FormatTime(stack.TotalMinutes)}",
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold,
            Foreground = (SolidColorBrush)res["ThemeAccent"]
        });
        foreach (var seg in stack.Segments.OrderByDescending(s => s.Minutes).Take(8))
        {
            sp.Children.Add(new TextBlock
            {
                Text = $"{LogAnalyzer.FormatTime(seg.Minutes),7}  {seg.ProcessName}",
                FontFamily = new FontFamily("Consolas"),
                Foreground = (SolidColorBrush)res["ThemeText"]
            });
        }
        return new ToolTip
        {
            Content = sp,
            Background = (SolidColorBrush)res["ThemeSurfaceAlt"],
            BorderBrush = (SolidColorBrush)res["ThemeCheckBorder"],
            Padding = new Thickness(8, 6, 8, 6)
        };
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

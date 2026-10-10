using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Proc;

public class AnalysisViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string _logDirectory;
    private readonly Dispatcher _dispatcher;

    private DateTime _referenceDate = DateTime.Today;
    private AnalysisPeriod _period = AnalysisPeriod.Day;
    private List<AppBarItem> _appBars = [];
    private List<DetailItem> _detailItems = [];
    private List<TimelineBlock> _timelineBlocks = [];
    private Dictionary<string, SolidColorBrush> _processColors = new();
    private bool _isDayView = true;
    private string _periodLabel = "";
    private string _totalTimeText = "";
    private string _topAppText = "";
    private string _selectedAppName = "";
    private string _detailHeader = "";
    private bool _hasData;
    private bool _hasDetail;
    private bool _showIdle = false;

    private List<(DateTime Date, ActivityRecord Record)> _periodRecords = [];
    private List<(DateTime Date, ActivityRecord Record)>? _currentRecords;
    private TimeSpan? _rangeStart;
    private TimeSpan? _rangeEnd;
    private int _iconLoadVersion;

    // Apps excluded from the analysis (persisted in settings)
    private readonly HashSet<string> _hiddenApps;
    private List<AppFilterItem> _appFilterItems = [];
    private string _appFilterQuery = "";

    public AnalysisViewModel(string logDirectory)
    {
        _logDirectory = logDirectory;
        _dispatcher = Application.Current.Dispatcher;
        _hiddenApps = new HashSet<string>(AppSettings.Load().HiddenApps, StringComparer.OrdinalIgnoreCase);
        Refresh();
    }

    /// <summary>All apps of the period (plus hidden ones) for the filter popup, narrowed by the query.</summary>
    public List<AppFilterItem> AppFilterItems => string.IsNullOrWhiteSpace(_appFilterQuery)
        ? _appFilterItems
        : _appFilterItems.Where(a => a.ProcessName.Contains(_appFilterQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

    public string AppFilterQuery
    {
        get => _appFilterQuery;
        set { _appFilterQuery = value; OnPropertyChanged(nameof(AppFilterQuery)); OnPropertyChanged(nameof(AppFilterItems)); }
    }

    public string FilterButtonText => _hiddenApps.Count > 0 ? $"Filter ({_hiddenApps.Count} hidden)" : "Filter";

    public bool HasHiddenApps => _hiddenApps.Count > 0;

    public void SetAppVisible(string processName, bool visible)
    {
        bool changed = visible ? _hiddenApps.Remove(processName) : _hiddenApps.Add(processName);
        if (changed) SaveHiddenAppsAndRefresh();
    }

    public void ShowAllApps()
    {
        if (_hiddenApps.Count == 0) return;
        _hiddenApps.Clear();
        SaveHiddenAppsAndRefresh();
    }

    /// <summary>Hides every app of the current period except the given one.</summary>
    public void ShowOnlyApp(string processName)
    {
        foreach (var item in _appFilterItems)
        {
            if (string.Equals(item.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                _hiddenApps.Remove(item.ProcessName);
            else
                _hiddenApps.Add(item.ProcessName);
        }
        SaveHiddenAppsAndRefresh();
    }

    private void SaveHiddenAppsAndRefresh()
    {
        var hidden = _hiddenApps.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        AppSettings.Update(s => s.HiddenApps = hidden);
        OnPropertyChanged(nameof(FilterButtonText));
        OnPropertyChanged(nameof(HasHiddenApps));
        Refresh();
    }

    public DateTime ReferenceDate
    {
        get => _referenceDate;
        private set { if (_referenceDate != value) { _referenceDate = value; OnPropertyChanged(nameof(ReferenceDate)); } }
    }

    public AnalysisPeriod Period
    {
        get => _period;
        private set { if (_period != value) { _period = value; OnPropertyChanged(nameof(Period)); } }
    }

    public List<AppBarItem> AppBars
    {
        get => _appBars;
        private set { _appBars = value; OnPropertyChanged(nameof(AppBars)); }
    }

    public List<DetailItem> DetailItems
    {
        get => _detailItems;
        private set { _detailItems = value; OnPropertyChanged(nameof(DetailItems)); }
    }

    public string PeriodLabel
    {
        get => _periodLabel;
        private set { _periodLabel = value; OnPropertyChanged(nameof(PeriodLabel)); }
    }

    public string TotalTimeText
    {
        get => _totalTimeText;
        private set { _totalTimeText = value; OnPropertyChanged(nameof(TotalTimeText)); }
    }

    public string TopAppText
    {
        get => _topAppText;
        private set { _topAppText = value; OnPropertyChanged(nameof(TopAppText)); }
    }

    public string SelectedAppName
    {
        get => _selectedAppName;
        private set { _selectedAppName = value; OnPropertyChanged(nameof(SelectedAppName)); }
    }

    public string DetailHeader
    {
        get => _detailHeader;
        private set { _detailHeader = value; OnPropertyChanged(nameof(DetailHeader)); }
    }

    public bool HasData
    {
        get => _hasData;
        private set { _hasData = value; OnPropertyChanged(nameof(HasData)); }
    }

    public bool HasDetail
    {
        get => _hasDetail;
        private set { _hasDetail = value; OnPropertyChanged(nameof(HasDetail)); }
    }

    public bool ShowIdle
    {
        get => _showIdle;
        set { if (_showIdle != value) { _showIdle = value; OnPropertyChanged(nameof(ShowIdle)); Refresh(); } }
    }

    public List<TimelineBlock> TimelineBlocks
    {
        get => _timelineBlocks;
        private set { _timelineBlocks = value; OnPropertyChanged(nameof(TimelineBlocks)); }
    }

    public Dictionary<string, SolidColorBrush> ProcessColors
    {
        get => _processColors;
        private set { _processColors = value; OnPropertyChanged(nameof(ProcessColors)); }
    }

    public bool IsDayView
    {
        get => _isDayView;
        private set { _isDayView = value; OnPropertyChanged(nameof(IsDayView)); }
    }

    /// <summary>Start of the time-of-day range selected on the Day timeline (inclusive).</summary>
    public TimeSpan? RangeStart => _rangeStart;

    /// <summary>End of the time-of-day range selected on the Day timeline (exclusive).</summary>
    public TimeSpan? RangeEnd => _rangeEnd;

    public bool HasTimeRange => _rangeStart.HasValue && _rangeEnd.HasValue;

    /// <summary>Selected range as text for headers, e.g. "10:15 - 11:40"; empty when none.</summary>
    public string RangeText => HasTimeRange
        ? $"{LogAnalyzer.FormatClock(_rangeStart!.Value)} - {LogAnalyzer.FormatClock(_rangeEnd!.Value)}"
        : "";

    private void NotifyRangeChanged()
    {
        OnPropertyChanged(nameof(HasTimeRange));
        OnPropertyChanged(nameof(RangeText));
    }

    public void SetPeriod(AnalysisPeriod period)
    {
        Period = period;
        ResetTimeRange();
        Refresh();
    }

    public void NavigatePrevious()
    {
        ReferenceDate = LogAnalyzer.Navigate(_referenceDate, _period, -1);
        ResetTimeRange();
        Refresh();
    }

    public void NavigateNext()
    {
        ReferenceDate = LogAnalyzer.Navigate(_referenceDate, _period, 1);
        ResetTimeRange();
        Refresh();
    }

    public void NavigateToday()
    {
        ReferenceDate = DateTime.Today;
        ResetTimeRange();
        Refresh();
    }

    public void SetTimeRange(TimeSpan start, TimeSpan end)
    {
        if (_period != AnalysisPeriod.Day || end <= start) return;
        _rangeStart = start;
        _rangeEnd = end;
        NotifyRangeChanged();
        ApplyRecords();
    }

    public void ClearTimeRange()
    {
        if (!HasTimeRange) return;
        ResetTimeRange();
        NotifyRangeChanged();
        ApplyRecords();
    }

    private void ResetTimeRange()
    {
        _rangeStart = null;
        _rangeEnd = null;
    }

    public void SelectApp(string processName)
    {
        SelectedAppName = processName;

        // Update IsSelected on bar items
        AppBars = _appBars.Select(b => b with { IsSelected = b.ProcessName == processName }).ToList();

        RefreshDetail(processName);
    }

    public void SelectAll()
    {
        SelectedAppName = "";

        // Deselect all bar items
        AppBars = _appBars.Select(b => b with { IsSelected = false }).ToList();

        RefreshDetailAll();
    }

    public void SetDate(DateTime date)
    {
        ReferenceDate = date;
        ResetTimeRange();
        Refresh();
    }

    public void Refresh()
    {
        var (start, end) = LogAnalyzer.GetDateRange(_referenceDate, _period);
        var allRecords = LogAnalyzer.ReadRecords(_logDirectory, start, end);
        _periodRecords = _hiddenApps.Count == 0
            ? allRecords
            : allRecords.Where(r => !_hiddenApps.Contains(r.Record.ProcessName)).ToList();

        // Filter list: every app in the period (by time), then hidden apps not seen in this period
        var usage = LogAnalyzer.Aggregate(allRecords, int.MaxValue, _showIdle);
        _appFilterItems = usage
            .Select(u => new AppFilterItem(u.ProcessName, LogAnalyzer.FormatTime(u.TotalMinutes),
                !_hiddenApps.Contains(u.ProcessName)))
            .Concat(_hiddenApps
                .Where(h => !usage.Any(u => string.Equals(u.ProcessName, h, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .Select(h => new AppFilterItem(h, "-", false)))
            .ToList();
        OnPropertyChanged(nameof(AppFilterItems));

        PeriodLabel = LogAnalyzer.GetPeriodLabel(_referenceDate, _period);
        IsDayView = _period == AnalysisPeriod.Day;

        // Build process -> color map from the whole period so colors stay stable
        // while a time range is selected (consistent across bar chart and timeline)
        var periodSummaries = LogAnalyzer.Aggregate(_periodRecords, includeIdle: _showIdle);
        var colorMap = new Dictionary<string, SolidColorBrush>();
        for (int i = 0; i < periodSummaries.Count; i++)
        {
            var brush = new SolidColorBrush(BarPalette.GetColor(i));
            brush.Freeze();
            colorMap[periodSummaries[i].ProcessName] = brush;
        }
        ProcessColors = colorMap;

        // Build timeline for day view (always the whole day, regardless of range)
        if (_period == AnalysisPeriod.Day && _periodRecords.Count > 0)
            TimelineBlocks = LogAnalyzer.BuildTimeline(_periodRecords, _showIdle);
        else
            TimelineBlocks = [];

        NotifyRangeChanged();
        ApplyRecords();
    }

    /// <summary>
    /// Recomputes bars, totals and details from the period records, filtered by the
    /// selected time range if any. Keeps the current app selection when still present.
    /// </summary>
    private void ApplyRecords()
    {
        _currentRecords = HasTimeRange
            ? _periodRecords.Where(r =>
            {
                var t = r.Record.Timestamp.TimeOfDay;
                return t >= _rangeStart!.Value && t < _rangeEnd!.Value;
            }).ToList()
            : _periodRecords;

        var summaries = LogAnalyzer.Aggregate(_currentRecords, includeIdle: _showIdle);

        int totalMinutes = summaries.Sum(s => s.TotalMinutes);
        TotalTimeText = $"Total: {LogAnalyzer.FormatTime(totalMinutes)}";
        TopAppText = summaries.Count > 0 ? $"Top: {summaries[0].ProcessName}" : "No data";
        HasData = summaries.Count > 0;

        var fallbackBrush = new SolidColorBrush(BarPalette.GetColor(15));
        fallbackBrush.Freeze();

        int maxMinutes = summaries.Count > 0 ? summaries.Max(s => s.TotalMinutes) : 1;
        var selected = summaries.Any(s => s.ProcessName == _selectedAppName) ? _selectedAppName : "";

        AppBars = summaries.Select(s => new AppBarItem(
            s.ProcessName,
            LogAnalyzer.FormatTime(s.TotalMinutes),
            $"{s.Percentage}%",
            s.TotalMinutes / (double)maxMinutes,
            _processColors.TryGetValue(s.ProcessName, out var brush) ? brush : fallbackBrush,
            null,
            s.ProcessName == selected
        )).ToList();

        // Load icons asynchronously.
        int iconLoadVersion = System.Threading.Interlocked.Increment(ref _iconLoadVersion);
        _ = LoadIconsAsync(summaries.Select(s => s.ProcessName).ToList(), iconLoadVersion);

        if (summaries.Count > 0)
        {
            SelectedAppName = selected;
            if (selected == "") RefreshDetailAll();
            else RefreshDetail(selected);
        }
        else
        {
            DetailItems = [];
            DetailHeader = "";
            HasDetail = false;
        }
    }

    private async Task LoadIconsAsync(List<string> processNames, int iconLoadVersion)
    {
        try
        {
            var icons = await Task.Run(() =>
            {
                var result = new Dictionary<string, ImageSource?>();
                foreach (var name in processNames)
                {
                    if (name == "Other") continue;
                    try
                    {
                        result[name] = IconHelper.GetIconByProcessName(name);
                    }
                    catch
                    {
                        result[name] = null;
                    }
                }
                return result;
            }).ConfigureAwait(false);

            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return;

            // Ignore stale icon results from older refresh calls.
            _dispatcher.Invoke(() =>
            {
                if (iconLoadVersion != _iconLoadVersion) return;

                AppBars = _appBars.Select(b =>
                {
                    if (icons.TryGetValue(b.ProcessName, out var icon) && icon != null)
                        return b with { Icon = icon };
                    return b;
                }).ToList();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadIconsAsync failed: {ex}");
        }
    }

    private void RefreshDetail(string processName)
    {
        if (_currentRecords == null || _currentRecords.Count == 0)
        {
            HasDetail = false;
            return;
        }

        var items = new List<DetailItem>();
        var summary = LogAnalyzer.Aggregate(_currentRecords, includeIdle: _showIdle).FirstOrDefault(s => s.ProcessName == processName);

        if (summary != null)
        {
            DetailHeader = $"{processName} - {LogAnalyzer.FormatTime(summary.TotalMinutes)}{RangeSuffix()}";
            items.Add(new DetailItem("Window Titles", "", true));
            foreach (var title in summary.TitleBreakdown.Take(20))
            {
                var label = title.WindowTitle.Length > 80
                    ? title.WindowTitle[..77] + "..."
                    : title.WindowTitle;
                if (string.IsNullOrWhiteSpace(label)) label = "(no title)";
                items.Add(new DetailItem(label, LogAnalyzer.FormatTime(title.Minutes), false));
            }
        }

        if (_period != AnalysisPeriod.Day)
        {
            var daily = LogAnalyzer.GetDailyBreakdown(_currentRecords, processName);
            if (daily.Count > 0)
            {
                items.Add(new DetailItem("Daily Breakdown", "", true));
                foreach (var d in daily)
                    items.Add(new DetailItem(d.Date.ToString("MM/dd (ddd)"), LogAnalyzer.FormatTime(d.Minutes), false));
            }
        }

        DetailItems = items;
        HasDetail = items.Count > 0;
    }

    private void RefreshDetailAll()
    {
        if (_currentRecords == null || _currentRecords.Count == 0)
        {
            HasDetail = false;
            return;
        }

        var items = new List<DetailItem>();
        var summaries = LogAnalyzer.Aggregate(_currentRecords, int.MaxValue, _showIdle);
        int allTotal = summaries.Sum(s => s.TotalMinutes);
        DetailHeader = $"All Apps - {LogAnalyzer.FormatTime(allTotal)}{RangeSuffix()}";

        // All window titles across all apps, grouped by process then title
        foreach (var summary in summaries)
        {
            items.Add(new DetailItem($"{summary.ProcessName} ({LogAnalyzer.FormatTime(summary.TotalMinutes)})", "", true));
            foreach (var title in summary.TitleBreakdown.Take(10))
            {
                var label = title.WindowTitle.Length > 80
                    ? title.WindowTitle[..77] + "..."
                    : title.WindowTitle;
                if (string.IsNullOrWhiteSpace(label)) label = "(no title)";
                items.Add(new DetailItem(label, LogAnalyzer.FormatTime(title.Minutes), false));
            }
        }

        DetailItems = items;
        HasDetail = items.Count > 0;
    }

    private string RangeSuffix() =>
        HasTimeRange
            ? $"  [{LogAnalyzer.FormatClock(_rangeStart!.Value)}-{LogAnalyzer.FormatClock(_rangeEnd!.Value)}]"
            : "";

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record AppBarItem(
    string ProcessName,
    string TimeText,
    string PercentText,
    double BarWidthRatio,
    SolidColorBrush BarBrush,
    ImageSource? Icon,
    bool IsSelected);

public record AppFilterItem(string ProcessName, string TimeText, bool IsVisible);

public record DetailItem(string Label, string Value, bool IsHeader);

public static class BarPalette
{
    private static readonly Color[] Colors =
    [
        Color.FromRgb(0xFF, 0x8C, 0x00), // Orange
        Color.FromRgb(0x4E, 0xA8, 0xDE), // Blue
        Color.FromRgb(0x7B, 0xC8, 0x5E), // Green
        Color.FromRgb(0xE0, 0x5A, 0x5A), // Red
        Color.FromRgb(0xC0, 0x7E, 0xD4), // Purple
        Color.FromRgb(0xE8, 0xC5, 0x47), // Yellow
        Color.FromRgb(0x5E, 0xC4, 0xB8), // Teal
        Color.FromRgb(0xD4, 0x7E, 0x9B), // Pink
        Color.FromRgb(0x8C, 0xA8, 0xC8), // Steel
        Color.FromRgb(0xB8, 0x96, 0x6E), // Tan
        Color.FromRgb(0xA0, 0xD0, 0x70), // Lime
        Color.FromRgb(0xD0, 0x90, 0x60), // Copper
        Color.FromRgb(0x70, 0x90, 0xD0), // Periwinkle
        Color.FromRgb(0xD0, 0x70, 0x70), // Salmon
        Color.FromRgb(0x90, 0xB0, 0x90), // Sage
        Color.FromRgb(0x80, 0x80, 0x80), // Gray (Other)
    ];

    public static Color GetColor(int index) => Colors[index % Colors.Length];
}

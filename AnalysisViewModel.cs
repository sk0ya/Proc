using System.ComponentModel;
using System.Windows.Media;

namespace Proc;

public class AnalysisViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly string _logDirectory;
    private readonly Func<string, ImageSource?> _iconResolver;

    private DateTime _referenceDate = DateTime.Today;
    private AnalysisPeriod _period = AnalysisPeriod.Day;
    private List<AppBarItem> _appBars = [];
    private List<DetailItem> _detailItems = [];
    private string _periodLabel = "";
    private string _totalTimeText = "";
    private string _topAppText = "";
    private string _selectedAppName = "";
    private string _detailHeader = "";
    private bool _hasData;
    private bool _hasDetail;

    private List<(DateTime Date, ActivityRecord Record)>? _currentRecords;

    public AnalysisViewModel(string logDirectory, Func<string, ImageSource?> iconResolver)
    {
        _logDirectory = logDirectory;
        _iconResolver = iconResolver;
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

    public void SetPeriod(AnalysisPeriod period)
    {
        Period = period;
        Refresh();
    }

    public void NavigatePrevious()
    {
        ReferenceDate = LogAnalyzer.Navigate(_referenceDate, _period, -1);
        Refresh();
    }

    public void NavigateNext()
    {
        ReferenceDate = LogAnalyzer.Navigate(_referenceDate, _period, 1);
        Refresh();
    }

    public void NavigateToday()
    {
        ReferenceDate = DateTime.Today;
        Refresh();
    }

    public void SelectApp(string processName)
    {
        SelectedAppName = processName;

        // Update IsSelected on bar items
        AppBars = _appBars.Select(b => b with { IsSelected = b.ProcessName == processName }).ToList();

        RefreshDetail(processName);
    }

    public void Refresh()
    {
        var (start, end) = LogAnalyzer.GetDateRange(_referenceDate, _period);
        _currentRecords = LogAnalyzer.ReadRecords(_logDirectory, start, end);
        var summaries = LogAnalyzer.Aggregate(_currentRecords);

        PeriodLabel = LogAnalyzer.GetPeriodLabel(_referenceDate, _period);
        int totalMinutes = _currentRecords.Count;
        TotalTimeText = $"Total: {LogAnalyzer.FormatTime(totalMinutes)}";
        TopAppText = summaries.Count > 0 ? $"Top: {summaries[0].ProcessName}" : "No data";
        HasData = summaries.Count > 0;

        int maxMinutes = summaries.Count > 0 ? summaries.Max(s => s.TotalMinutes) : 1;
        var firstApp = summaries.Count > 0 ? summaries[0].ProcessName : "";

        AppBars = summaries.Select((s, i) => new AppBarItem(
            s.ProcessName,
            LogAnalyzer.FormatTime(s.TotalMinutes),
            $"{s.Percentage}%",
            s.TotalMinutes / (double)maxMinutes,
            new SolidColorBrush(BarPalette.GetColor(i)),
            s.ProcessName == "Other" ? null : _iconResolver(s.ProcessName),
            s.ProcessName == firstApp
        )).ToList();

        if (summaries.Count > 0)
        {
            SelectedAppName = firstApp;
            RefreshDetail(firstApp);
        }
        else
        {
            DetailItems = [];
            DetailHeader = "";
            HasDetail = false;
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
        var summary = LogAnalyzer.Aggregate(_currentRecords).FirstOrDefault(s => s.ProcessName == processName);

        if (summary != null)
        {
            DetailHeader = $"{processName} - {LogAnalyzer.FormatTime(summary.TotalMinutes)}";
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

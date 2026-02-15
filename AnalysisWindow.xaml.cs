using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Proc;

public partial class AnalysisWindow : Window
{
    private readonly AnalysisViewModel _vm;

    public AnalysisWindow(string logDirectory, Func<string, System.Windows.Media.ImageSource?> iconResolver)
    {
        InitializeComponent();
        _vm = new AnalysisViewModel(logDirectory, iconResolver);
        DataContext = _vm;
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
    private void Today_Click(object sender, RoutedEventArgs e) => _vm.NavigateToday();

    private void AppBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppBarItem item)
            _vm.SelectApp(item.ProcessName);
    }

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

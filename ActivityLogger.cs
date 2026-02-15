using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Proc;

public class ActivityLogger : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly string _logDir;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public event Action? OnRecorded;
    public string? CurrentProcessName { get; private set; }
    public string? CurrentWindowTitle { get; private set; }

    public ActivityLogger()
    {
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proc", "logs");
        Directory.CreateDirectory(_logDir);
        _timer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    private void OnTick(object? state)
    {
        try
        {
            var record = CaptureActiveWindow();
            if (record == null) return;
            CurrentProcessName = record.ProcessName;
            CurrentWindowTitle = record.WindowTitle;
            var filePath = GetLogFilePath(DateTime.Today);
            File.AppendAllText(filePath, record.ToCsvLine() + Environment.NewLine, Encoding.UTF8);
            OnRecorded?.Invoke();
        }
        catch
        {
            // ignore logging errors
        }
    }

    private ActivityRecord? CaptureActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var sb = new StringBuilder(1024);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        GetWindowThreadProcessId(hwnd, out uint pid);
        string processName;
        try
        {
            processName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            processName = "(unknown)";
        }

        return new ActivityRecord(DateTime.Now, processName, title);
    }

    public List<ActivityRecord> GetTodayRecords()
    {
        var filePath = GetLogFilePath(DateTime.Today);
        if (!File.Exists(filePath)) return [];

        var records = new List<ActivityRecord>();
        foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                records.Add(ActivityRecord.FromCsvLine(line));
            }
            catch
            {
                // skip malformed lines
            }
        }
        return records;
    }

    private string GetLogFilePath(DateTime date) =>
        Path.Combine(_logDir, $"{date:yyyy-MM-dd}.csv");

    public void Dispose()
    {
        _timer.Dispose();
    }
}

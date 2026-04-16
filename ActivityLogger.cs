using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Proc;

public class ActivityLogger : IDisposable
{
    private readonly System.Threading.Timer _logTimer;
    private readonly System.Threading.Timer _idleTimer;
    private readonly string _logDir;
    public string LogDirectory => _logDir;
    private IntPtr _winEventHook;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private readonly WinEventDelegate _winEventProc;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private static readonly TimeSpan ShowIdleThreshold = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LoggingIdleThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleCheckInterval = TimeSpan.FromSeconds(1);

    public event Action? OnRecorded;
    public event Action? OnActiveChanged;
    public event Action<bool>? OnIdleChanged;
    public string? CurrentProcessName { get; private set; }
    public string? CurrentWindowTitle { get; private set; }
    public bool IsIdle { get; private set; }

    private volatile bool _isSessionLocked;
    private readonly Dictionary<string, string> _exePaths = new();

    public ActivityLogger()
    {
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proc", "logs");
        Directory.CreateDirectory(_logDir);

        // Keep a reference to prevent GC collection of the delegate
        _winEventProc = OnForegroundChanged;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _logTimer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        _idleTimer = new System.Threading.Timer(OnIdleCheck, null, TimeSpan.Zero, IdleCheckInterval);
    }

    public void StartForegroundHook()
    {
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Capture initial state
        UpdateCurrentWindow();
    }

    private void OnForegroundChanged(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        UpdateCurrentWindow();
        UpdateIdleState();
        OnActiveChanged?.Invoke();
    }

    private void UpdateCurrentWindow()
    {
        try
        {
            var record = CaptureActiveWindow();
            if (record == null) return;
            CurrentProcessName = record.ProcessName;
            CurrentWindowTitle = record.WindowTitle;
        }
        catch
        {
            // ignore
        }
    }

    private static TimeSpan GetIdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return TimeSpan.Zero;
        return TimeSpan.FromMilliseconds((uint)Environment.TickCount - info.dwTime);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _isSessionLocked = e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect;
        if (_isSessionLocked)
            SetIdleState(false);
    }

    private void UpdateIdleState()
    {
        UpdateIdleState(GetIdleTime());
    }

    private void UpdateIdleState(TimeSpan idleTime)
    {
        SetIdleState(!_isSessionLocked && idleTime >= ShowIdleThreshold);
    }

    private void SetIdleState(bool isIdle)
    {
        if (IsIdle == isIdle) return;
        IsIdle = isIdle;
        OnIdleChanged?.Invoke(isIdle);
    }

    private void OnIdleCheck(object? state)
    {
        try
        {
            UpdateIdleState();
        }
        catch
        {
            // ignore idle detection errors
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            if (_isSessionLocked)
            {
                SetIdleState(false);
                return;
            }

            var idleTime = GetIdleTime();
            UpdateIdleState(idleTime);
            if (idleTime >= LoggingIdleThreshold) return;

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
            var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;
            try
            {
                if (proc.MainModule?.FileName is string path)
                {
                    _exePaths[processName] = path;
                    IconHelper.RegisterExePath(processName, path);
                }
            }
            catch { }
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

    public string? GetExePath(string processName) =>
        _exePaths.TryGetValue(processName, out var path) ? path : null;

    private string GetLogFilePath(DateTime date) =>
        Path.Combine(_logDir, $"{date:yyyy-MM-dd}.csv");

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _logTimer.Dispose();
        _idleTimer.Dispose();
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }
}

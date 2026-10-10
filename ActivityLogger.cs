using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Proc;

public class ActivityLogger : IDisposable
{
    private readonly System.Threading.Timer _logTimer;
    private readonly System.Threading.Timer _sampleTimer;
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
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    public event Action? OnRecorded;
    public event Action? OnActiveChanged;
    public event Action<bool>? OnIdleChanged;

    private sealed record WindowInfo(string ProcessName, string WindowTitle);

    // Replaced atomically so readers on other threads always see a consistent pair
    private volatile WindowInfo? _current;
    public string? CurrentProcessName => _current?.ProcessName;
    public string? CurrentWindowTitle => _current?.WindowTitle;

    private volatile bool _isIdle;
    public bool IsIdle => _isIdle;

    private volatile bool _isSessionLocked;

    // Guards everything below (touched by the hook on the UI thread and by both timers)
    private readonly object _lock = new();

    // Seconds each window was in the foreground during the current minute; the minute's
    // record goes to the window that was used most, not whichever happened to be active at the tick
    private readonly Dictionary<WindowInfo, int> _minuteSamples = new();

    // pid -> process name; cleared every minute so reused pids are picked up
    private readonly Dictionary<uint, string> _processNames = new();

    private HashSet<string> _titleExcludedApps = new(StringComparer.OrdinalIgnoreCase);
    private int _retentionDays;

    // Today's records kept in memory so the overlay doesn't re-read the CSV on every window switch
    private List<ActivityRecord>? _todayRecords;
    private DateTime _todayDate;

    public ActivityLogger()
    {
        _logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proc", "logs");
        Directory.CreateDirectory(_logDir);

        ReloadSettings();
        LogAnalyzer.DeleteOldLogs(_logDir, _retentionDays, DateTime.Today);

        // Keep a reference to prevent GC collection of the delegate
        _winEventProc = OnForegroundChanged;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        _logTimer = new System.Threading.Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
        _sampleTimer = new System.Threading.Timer(OnSample, null, TimeSpan.Zero, SampleInterval);
    }

    /// <summary>Re-reads the privacy settings (title exclusion, log retention).</summary>
    public void ReloadSettings()
    {
        var settings = AppSettings.Load();
        lock (_lock)
        {
            _titleExcludedApps = new HashSet<string>(
                settings.TitleExcludedApps.Select(NormalizeProcessName), StringComparer.OrdinalIgnoreCase);
            _retentionDays = settings.LogRetentionDays;
        }
    }

    private static string NormalizeProcessName(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
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

    /// <summary>Captures the foreground window; returns true when it differs from the previous one.</summary>
    private bool UpdateCurrentWindow(WindowInfo? info = null)
    {
        try
        {
            info ??= CaptureActiveWindow();
            if (info == null || info == _current) return false;
            _current = info;
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("UpdateCurrentWindow", ex);
            return false;
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
        if (_isIdle == isIdle) return;
        _isIdle = isIdle;
        OnIdleChanged?.Invoke(isIdle);
    }

    private void OnSample(object? state)
    {
        try
        {
            var idleTime = GetIdleTime();
            UpdateIdleState(idleTime);
            if (_isSessionLocked || idleTime >= ShowIdleThreshold) return;

            var info = CaptureActiveWindow();
            if (info == null) return;

            lock (_lock)
                _minuteSamples[info] = _minuteSamples.GetValueOrDefault(info) + 1;

            // Title changes inside the same window (e.g. browser tabs) don't fire the foreground hook
            if (UpdateCurrentWindow(info))
                OnActiveChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Sample", ex);
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            if (_isSessionLocked)
            {
                ClearMinute();
                SetIdleState(false);
                return;
            }

            var idleTime = GetIdleTime();
            UpdateIdleState(idleTime);
            if (idleTime >= LoggingIdleThreshold)
            {
                ClearMinute();
                return;
            }

            var now = DateTime.Now;
            ActivityRecord? record;
            if (idleTime >= ShowIdleThreshold)
            {
                ClearMinute();
                record = new ActivityRecord(now, "Idle", "", true);
            }
            else
            {
                var info = TakeDominantWindow() ?? CaptureActiveWindow();
                if (info == null) return;
                record = new ActivityRecord(now, info.ProcessName, info.WindowTitle);
            }

            Append(record);
            OnRecorded?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Tick", ex);
        }
    }

    private void ClearMinute()
    {
        lock (_lock)
        {
            _minuteSamples.Clear();
            _processNames.Clear();
        }
    }

    /// <summary>
    /// Picks the process used longest during the minute, then its most-used title,
    /// and resets the samples for the next minute.
    /// </summary>
    private WindowInfo? TakeDominantWindow()
    {
        lock (_lock)
        {
            try
            {
                if (_minuteSamples.Count == 0) return null;
                var process = _minuteSamples
                    .GroupBy(kv => kv.Key.ProcessName)
                    .MaxBy(g => g.Sum(kv => kv.Value))!;
                return process.MaxBy(kv => kv.Value).Key;
            }
            finally
            {
                _minuteSamples.Clear();
                _processNames.Clear();
            }
        }
    }

    private void Append(ActivityRecord record)
    {
        var date = record.Timestamp.Date;
        lock (_lock)
        {
            // Load the cache before writing so the new line isn't picked up twice
            EnsureTodayLoaded(date);
            File.AppendAllText(LogAnalyzer.GetLogFilePath(_logDir, date),
                record.ToCsvLine() + Environment.NewLine, Encoding.UTF8);
            _todayRecords!.Add(record);
        }
    }

    private void EnsureTodayLoaded(DateTime date)
    {
        if (_todayRecords != null && _todayDate == date) return;

        bool dayChanged = _todayRecords != null && _todayDate != date;
        _todayRecords = LogAnalyzer.ReadLogFile(LogAnalyzer.GetLogFilePath(_logDir, date));
        _todayDate = date;
        if (dayChanged)
            LogAnalyzer.DeleteOldLogs(_logDir, _retentionDays, date);
    }

    private WindowInfo? CaptureActiveWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var sb = new StringBuilder(1024);
        GetWindowText(hwnd, sb, sb.Capacity);
        var title = sb.ToString();

        GetWindowThreadProcessId(hwnd, out uint pid);
        var processName = GetProcessName(pid);

        lock (_lock)
        {
            if (_titleExcludedApps.Contains(processName))
                title = "";
        }
        return new WindowInfo(processName, title);
    }

    private string GetProcessName(uint pid)
    {
        lock (_lock)
        {
            if (_processNames.TryGetValue(pid, out var cached))
                return cached;
        }

        string processName;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;
            try
            {
                if (proc.MainModule?.FileName is string path)
                    IconHelper.RegisterExePath(processName, path);
            }
            catch
            {
                // access denied for elevated processes when not running as admin
            }
        }
        catch
        {
            processName = "(unknown)";
        }

        lock (_lock)
            _processNames[pid] = processName;
        return processName;
    }

    public List<ActivityRecord> GetTodayRecords()
    {
        lock (_lock)
        {
            EnsureTodayLoaded(DateTime.Today);
            return [.. _todayRecords!];
        }
    }

    /// <summary>Drops the in-memory copy of today's log (call after the file was changed externally).</summary>
    public void InvalidateCache()
    {
        lock (_lock)
            _todayRecords = null;
    }

    public void Dispose()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _logTimer.Dispose();
        _sampleTimer.Dispose();
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }
}

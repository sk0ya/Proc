using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace Proc;

public partial class App : System.Windows.Application
{
    private const int ERROR_CANCELLED = 1223;

    private Mutex? _mutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => ErrorLog.Write("Dispatcher", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) ErrorLog.Write("AppDomain", ex);
        };

        var settings = AppSettings.Load();
        if (settings.RunAsAdmin && !IsRunningAsAdmin() && TryRelaunchAsAdmin())
        {
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "Proc_SingleInstance_Mutex", out _ownsMutex);
        if (!_ownsMutex)
        {
            Shutdown();
            return;
        }

        // Apply saved theme from custom colors
        ColorTheme.FromColors(
            ColorTheme.ParseHex(settings.BgColor),
            ColorTheme.ParseHex(settings.AccentColor)).Apply();

        // Keep the elevated logon task in sync when it can be managed (needs admin)
        StartupManager.SyncWithSettings();

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();

        // Check for updates asynchronously after UI is shown
        _ = CheckUpdateAsync();

        // Check for updates every 12 hours while running
        AutoUpdater.StartPeriodicCheck(TimeSpan.FromHours(12));
    }

    /// <summary>
    /// Starts an elevated copy of this exe. Returns false when the UAC prompt was declined
    /// or the launch failed, in which case this instance keeps running without admin rights.
    /// </summary>
    private static bool TryRelaunchAsAdmin()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            })?.Dispose();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return false;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("RelaunchAsAdmin", ex);
            return false;
        }
    }

    private static async Task CheckUpdateAsync()
    {
        try
        {
            if (await AutoUpdater.CheckAndUpdate())
            {
                Current.Dispatcher.Invoke(() => Current.Shutdown());
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("CheckUpdate", ex);
        }
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // ReleaseMutex throws when this instance never owned it (second instance shutting down)
        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

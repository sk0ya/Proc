using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace Proc;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppSettings.Load();
        if (settings.RunAsAdmin && !IsRunningAsAdmin())
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                }
            }
            catch { }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "Proc_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        KillOtherInstances();

        // Check for updates before showing UI
        try
        {
            if (AutoUpdater.CheckAndUpdate().GetAwaiter().GetResult())
            {
                Shutdown();
                return;
            }
        }
        catch { }

        // Apply saved theme from custom colors
        ColorTheme.FromColors(
            ColorTheme.ParseHex(settings.BgColor),
            ColorTheme.ParseHex(settings.AccentColor)).Apply();

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();

        // Check for updates every 1 hour while running
        AutoUpdater.StartPeriodicCheck(TimeSpan.FromHours(12));
    }

    public static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void KillOtherInstances()
    {
        var currentId = Environment.ProcessId;
        var currentName = Process.GetCurrentProcess().ProcessName;
        foreach (var proc in Process.GetProcessesByName(currentName))
        {
            if (proc.Id != currentId)
            {
                try { proc.Kill(); } catch { }
            }
            proc.Dispose();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}

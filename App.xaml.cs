using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace Proc;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "Proc_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        KillOtherInstances();

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
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

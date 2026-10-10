using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Proc;

/// <summary>
/// Windows startup registration. Normally a HKCU Run entry; when "Run as administrator" is on
/// and Proc is elevated, a logon task with highest privileges is used instead, so the app starts
/// elevated without a UAC prompt at every sign-in.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "Proc";
    private const string TaskName = "Proc";

    public static bool IsEnabled() => RunKeyExists() || TaskExists();

    public static void SetEnabled(bool enabled)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (!enabled)
            {
                RemoveRunKey();
                if (App.IsRunningAsAdmin() && TaskExists()) RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                return;
            }
            if (string.IsNullOrEmpty(exePath)) return;

            if (AppSettings.Load().RunAsAdmin && App.IsRunningAsAdmin() && TryCreateTask(exePath))
            {
                RemoveRunKey();
                return;
            }

            SetRunKey(exePath);
            // Deleting the elevated task needs admin; if it stays, the mutex blocks the duplicate at logon
            if (App.IsRunningAsAdmin() && TaskExists()) RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Startup.SetEnabled", ex);
        }
    }

    /// <summary>Re-applies the registration (picks up a moved exe or a changed admin setting).</summary>
    public static void SyncWithSettings()
    {
        if (IsEnabled()) SetEnabled(true);
    }

    private static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(AppName) is string;
    }

    private static void SetRunKey(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.SetValue(AppName, $"\"{exePath}\"");
    }

    private static void RemoveRunKey()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        key?.DeleteValue(AppName, false);
    }

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"") == 0;

    private static bool TryCreateTask(string exePath)
    {
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        // Created from XML because schtasks' defaults would stop the task after 3 days
        // and refuse to start it on battery power
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exePath)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;

        var xmlPath = Path.Combine(Path.GetTempPath(), "Proc_task.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F") == 0;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    private static int RunSchtasks(string arguments)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (proc == null) return -1;
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);
            return proc.HasExited ? proc.ExitCode : -1;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("schtasks", ex);
            return -1;
        }
    }
}

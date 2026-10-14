using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace Thermals.Core;

internal static class Platform
{
    public static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    /// <summary>
    /// LibreHardwareMonitor uses the PawnIO driver (the signed replacement for WinRing0) for
    /// CPU MSRs, Super-I/O chips and SMBus. Without it those sensors are simply absent.
    /// </summary>
    public static bool IsPawnIoInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            return key != null;
        }
        catch { return false; }
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Could not open " + url, ex); }
    }
}

/// <summary>
/// Start-with-Windows via a logon scheduled task with highest privileges. A Run registry key
/// cannot start an elevated app without a UAC prompt; a scheduled task can.
/// </summary>
internal static class Autostart
{
    private const string TaskName = @"\Noisewater Labs\Thermals";
    private const string LegacyTaskName = "Thermals";

    public static bool IsEnabled() =>
        RunSchtasks($"/Query /TN \"{TaskName}\"") == 0 || RunSchtasks($"/Query /TN \"{LegacyTaskName}\"") == 0;

    public static bool Enable()
    {
        string exe = SecurityElement.Escape(Environment.ProcessPath ?? Application.ExecutablePath)!;
        string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name)!;

        // Defined via XML because plain /SC ONLOGON tasks are killed after 72 hours and
        // refuse to start on battery power.
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Author>Noisewater Labs</Author><Description>Starts the Thermals hardware monitor by Noisewater Labs at logon.</Description></RegistrationInfo>
              <Triggers>
                <LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT10S</Delay></LogonTrigger>
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
                <AllowHardTerminate>true</AllowHardTerminate>
                <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec><Command>{exe}</Command><Arguments>--minimized</Arguments></Exec>
              </Actions>
            </Task>
            """;

        RemoveLegacy();
        string file = Path.Combine(Path.GetTempPath(), "thermals_task.xml");
        try
        {
            File.WriteAllText(file, xml, Encoding.Unicode);
            return RunSchtasks($"/Create /F /TN \"{TaskName}\" /XML \"{file}\"") == 0;
        }
        catch (Exception ex)
        {
            Log.Error("Could not write task definition", ex);
            return false;
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    public static bool Disable()
    {
        RemoveLegacy();
        return RunSchtasks($"/Delete /F /TN \"{TaskName}\"") == 0;
    }

    private static void RemoveLegacy()
    {
        if (RunSchtasks($"/Query /TN \"{LegacyTaskName}\"") == 0)
            RunSchtasks($"/Delete /F /TN \"{LegacyTaskName}\"");
    }

    private static int RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit(10_000);
            if (p.ExitCode != 0 && !args.StartsWith("/Query", StringComparison.Ordinal))
                Log.Error($"schtasks {args} failed: {err.Trim()}");
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            Log.Error("schtasks failed", ex);
            return -1;
        }
    }
}

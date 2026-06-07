using System;
using System.ServiceProcess;
using Microsoft.Win32;

namespace CtblPlusPlus.Core.AppSystem;

/// <summary>
/// Manages Windows service state using the ServiceController API directly,
/// bypassing any shell-level blocks (e.g. IFEO on sc.exe).
/// </summary>
public class WindowsServiceMonitor
{
    public void EnsureTimeServiceCheck()
    {
        EnsureServiceRunning("w32time");
        EnsureRegistryValue(@"SYSTEM\CurrentControlSet\Services\tzautoupdate", "Start", 3);
        EnsureRegistryValue(@"SYSTEM\CurrentControlSet\Services\W32Time\Parameters", "Type", "NTP");
    }

    private void EnsureServiceRunning(string serviceName)
    {
        try
        {
            using ServiceController sc = new ServiceController(serviceName);

            // Ensure the service start type is set to Automatic via registry,
            // since ServiceController does not expose a ChangeStartType method
            // prior to .NET 7 / Windows-only extension methods.
            EnsureServiceStartTypeAutomatic(serviceName);

            ServiceControllerStatus status = sc.Status;

            if (status == ServiceControllerStatus.Stopped ||
                status == ServiceControllerStatus.StopPending)
            {
                if (status == ServiceControllerStatus.StopPending)
                {
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }

                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            else if (status == ServiceControllerStatus.Paused ||
                     status == ServiceControllerStatus.PausePending)
            {
                if (status == ServiceControllerStatus.PausePending)
                {
                    sc.WaitForStatus(ServiceControllerStatus.Paused, TimeSpan.FromSeconds(10));
                }

                sc.Continue();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            // ServiceControllerStatus.Running or StartPending — nothing to do
        }
        catch (Exception ex)
        {
            Log($"Failed to ensure service '{serviceName}' is running: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the service start type to Automatic directly via the registry,
    /// equivalent to "sc config <name> start= auto".
    /// </summary>
    private void EnsureServiceStartTypeAutomatic(string serviceName)
    {
        const int SERVICE_AUTO_START = 2;
        EnsureRegistryValue(
            $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
            "Start",
            SERVICE_AUTO_START);
    }

    private void EnsureRegistryValue(string subKey, string valueName, object expectedValue)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(subKey, true);
            if (key != null)
            {
                object? current = key.GetValue(valueName);
                if (current == null || !current.Equals(expectedValue))
                {
                    key.SetValue(valueName, expectedValue);
                }
            }
        }
        catch { }
    }

    private void Log(string message)
    {
        try
        {
            string logPath = global::System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CtblPlusPlus", "process_log.txt");
            global::System.IO.File.AppendAllText(logPath,
                $"{DateTime.Now:O}: [WindowsServiceMonitor] {message}{Environment.NewLine}");
        }
        catch { }
    }
}



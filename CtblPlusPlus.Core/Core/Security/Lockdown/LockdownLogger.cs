using System;
using System.IO;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Centralized logger for all Lockdown services.
/// Writes exclusively to security_log.txt, preserving isolation from the
/// engine's process_log.txt pipeline.
/// Component tags follow the pattern [Lockdown.Purge], [Lockdown.Acl],
/// [Lockdown.Watchdog] to keep log output queryable.
/// </summary>
internal static class LockdownLogger
{
    public static void Log(string component, string message)
    {
        try
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CtblPlusPlus",
                "security_log.txt");

            string dir = Path.GetDirectoryName(logPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            File.AppendAllText(logPath,
                $"{DateTime.Now:O}: [{component}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}



namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Shared constants for the Lockdown subsystem.
/// Centralizes the target binary list so that ScorchedEarthPurgeService and
/// FileSystemWatchdogService reference a single source of truth.
/// </summary>
internal static class LockdownConstants
{
    public static readonly string[] TargetBinaries =
    {
        "bcdedit.exe",
        "reagentc.exe",
        "msconfig.exe",
        "SystemPropertiesAdvanced.exe",
        "SystemPropertiesProtection.exe"
    };
}



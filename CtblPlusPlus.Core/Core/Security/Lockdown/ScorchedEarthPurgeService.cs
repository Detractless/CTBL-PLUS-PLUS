using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Periodically scans System32 and WinSxS for prohibited binaries,
/// takes ownership from TrustedInstaller, and obliterates them.
/// </summary>
public class ScorchedEarthPurgeService : BackgroundService
{
    private readonly IInstallStateProvider _installState;
    private readonly IProcessInvoker _processInvoker;
    private readonly IFileDeleter _fileDeleter;

    public ScorchedEarthPurgeService(
        IInstallStateProvider installState,
        IProcessInvoker processInvoker,
        IFileDeleter fileDeleter)
    {
        _installState = installState;
        _processInvoker = processInvoker;
        _fileDeleter = fileDeleter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_installState.IsInstalled())
        {
            LockdownLogger.Log("Lockdown.Purge", "Running in portable/dev mode — ScorchedEarthPurgeService is DISABLED.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ExecutePurge();
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.Purge", $"Purge loop error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    /// <summary>
    /// Locates all instances of targeted binaries, takes ownership from
    /// TrustedInstaller, and obliterates them from the system.
    /// </summary>
    private void ExecutePurge()
    {
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var searchPaths = new[]
        {
            Path.Combine(windowsDir, "System32"),
            Path.Combine(windowsDir, "WinSxS")
        };

        foreach (var rootPath in searchPaths)
        {
            if (!Directory.Exists(rootPath)) continue;

            try
            {
                SearchAndDestroy(rootPath);
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.Purge", $"Purge failed for root {rootPath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Recursively walks a directory tree searching for target binaries.
    /// Uses per-directory try/catch intentionally — many WinSxS subdirectories
    /// are access-restricted even for SYSTEM, so we skip them gracefully
    /// rather than aborting the entire scan.
    /// </summary>
    private void SearchAndDestroy(string directory)
    {
        try
        {
            // Process files in current directory
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly))
            {
                if (LockdownConstants.TargetBinaries.Any(target => file.EndsWith(target, StringComparison.OrdinalIgnoreCase)))
                {
                    NeutralizeAndPurge(file);
                }
            }

            // Recurse into subdirectories
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                SearchAndDestroy(subDir);
            }
        }
        catch (UnauthorizedAccessException) { /* Expected for many WinSxS folders */ }
        catch (Exception ex)
        {
            LockdownLogger.Log("Lockdown.Purge", $"Error searching directory {directory}: {ex.Message}");
        }
    }

    private void NeutralizeAndPurge(string filePath)
    {
        try
        {
            // 1. Take ownership from TrustedInstaller/System
            _processInvoker.RunHiddenCommand("takeown.exe", $"/f \"{filePath}\" /a");

            // 2. Grant Administrators Full Control
            _processInvoker.RunHiddenCommand("icacls.exe", $"\"{filePath}\" /grant Administrators:F");

            // 3. Obliterate
            _fileDeleter.DeleteWithRetry(filePath);

            LockdownLogger.Log("Lockdown.Purge", $"Successfully obliterated: {filePath}");
        }
        catch (Exception ex)
        {
            LockdownLogger.Log("Lockdown.Purge", $"Failed to neutralize {filePath}: {ex.Message}");
        }
    }
}



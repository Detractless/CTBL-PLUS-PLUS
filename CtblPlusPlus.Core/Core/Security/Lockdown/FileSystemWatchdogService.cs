using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Monitors high-risk directories in real time via FileSystemWatcher
/// and immediately deletes any side-loaded target binaries.
/// </summary>
public class FileSystemWatchdogService : BackgroundService
{
    private readonly IInstallStateProvider _installState;
    private readonly IFileDeleter _fileDeleter;
    private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

    public FileSystemWatchdogService(
        IInstallStateProvider installState,
        IFileDeleter fileDeleter)
    {
        _installState = installState;
        _fileDeleter = fileDeleter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_installState.IsInstalled())
        {
            LockdownLogger.Log("Lockdown.Watchdog", "Running in portable/dev mode — FileSystemWatchdogService is DISABLED.");
            return;
        }

        InitializeWatchers();

        // Keep the service alive until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* Expected on shutdown */ }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        DisposeWatchers();
        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Initializes FileSystemWatchers on high-risk directories to prevent side-loading.
    /// </summary>
    private void InitializeWatchers()
    {
        var watchPaths = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            "C:\\Users\\Public\\Desktop"
        };

        foreach (var path in watchPaths.Where(Directory.Exists).Distinct())
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    Filter = "*.*",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                    InternalBufferSize = 65536 // Maximize buffer for high-traffic dirs
                };

                watcher.Created += OnSecurityEvent;
                watcher.Renamed += OnSecurityEvent;

                _watchers.Add(watcher);
                LockdownLogger.Log("Lockdown.Watchdog", $"Monitoring directory: {path}");
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.Watchdog", $"Failed to start watcher for {path}: {ex.Message}");
            }
        }
    }

    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void OnSecurityEvent(object sender, FileSystemEventArgs e)
    {
        if (LockdownConstants.TargetBinaries.Any(target => e.Name != null && e.Name.EndsWith(target, StringComparison.OrdinalIgnoreCase)))
        {
            LockdownLogger.Log("Lockdown.Watchdog", $"Unauthorized binary detection: {e.FullPath}. Initiating immediate deletion.");
            _fileDeleter.DeleteWithRetry(e.FullPath);
        }
    }
}



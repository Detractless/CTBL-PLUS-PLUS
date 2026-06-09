using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Domain.AppControl;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Security.Enforcers;

/// <summary>
/// Unified app discovery service. Uses FileSystemWatcher for real-time detection,
/// and a 2-minute fallback polling loop for process discovery.
/// Triggers the AppControlStateManager instantly when new apps are found.
/// </summary>
public class AppDiscoveryService : BackgroundService
{
    private readonly IAppControlRepository _appControlRepo;
    private readonly AppControlStateManager _stateManager;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ConcurrentQueue<string> _debouncerQueue = new();
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selfPaths;

    public AppDiscoveryService(IAppControlRepository appControlRepo, AppControlStateManager stateManager)
    {
        _appControlRepo = appControlRepo;
        _stateManager = stateManager;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _selfPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(baseDir, "CtblPlusPlus.Engine.exe"),
            Path.Combine(baseDir, "CtblPlusPlus.Wd1.exe"),
            Path.Combine(baseDir, "CtblPlusPlus.Wd2.exe")
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        InitializeWatchers();

        // Bootstrap known paths
        try
        {
            foreach (var p in _appControlRepo.GetAllAppPaths())
                _knownPaths.Add(p);
        }
        catch { }

        // Run both loops concurrently
        var debounceTask = DebouncerLoopAsync(stoppingToken);
        var processTask = ProcessPollingLoopAsync(stoppingToken);

        await Task.WhenAll(debounceTask, processTask);
    }

    private async Task ProcessPollingLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var newApps = new List<(string ExePath, string DisplayName, string Publisher)>();

                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        string? exePath = proc.MainModule?.FileName;
                        if (exePath == null) continue;
                        if (_selfPaths.Contains(exePath)) continue;
                        if (SystemPathGuard.IsProtected(exePath)) continue;
                        if (_knownPaths.Contains(exePath)) continue;
                        if (_appControlRepo.IsPathAllowed(exePath)) continue;

                        var vi = FileVersionInfo.GetVersionInfo(exePath);
                        string name = !string.IsNullOrWhiteSpace(vi.ProductName) ? vi.ProductName : Path.GetFileNameWithoutExtension(exePath);
                        string publisher = vi.CompanyName ?? "";

                        newApps.Add((exePath, name, publisher));
                        _knownPaths.Add(exePath);
                    }
                    catch { /* Access denied expected */ }
                    finally
                    {
                        try { proc.Dispose(); } catch { }
                    }
                }

                if (newApps.Count > 0)
                {
                    _appControlRepo.BulkUpsertApps(newApps);
                    
                    // Trigger instant sync!
                    _stateManager.SyncDetectedApps();
                }
            }
            catch (Exception ex)
            {
                EngineLogger.Log("AppDiscovery", $"[ERROR] Process polling failed: {ex.Message}");
            }

            // 45-second fallback polling loop
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
    }

    private async Task DebouncerLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_debouncerQueue.IsEmpty)
            {
                // Wait for the storm to settle (Debounce window)
                await Task.Delay(2000, stoppingToken);

                var batchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (_debouncerQueue.TryDequeue(out var filePath))
                {
                    batchedFiles.Add(filePath);
                }

                if (batchedFiles.Any())
                {
                    var newApps = new List<(string ExePath, string DisplayName, string Publisher)>();

                    foreach (var file in batchedFiles)
                    {
                        try
                        {
                            if (!File.Exists(file)) continue;
                            if (_selfPaths.Contains(file)) continue;
                            if (SystemPathGuard.IsProtected(file)) continue;
                            if (_knownPaths.Contains(file)) continue;
                            if (_appControlRepo.IsPathAllowed(file)) continue;

                            var vi = FileVersionInfo.GetVersionInfo(file);
                            string name = !string.IsNullOrWhiteSpace(vi.ProductName)
                                ? vi.ProductName
                                : Path.GetFileNameWithoutExtension(file);

                            newApps.Add((file, name, vi.CompanyName ?? ""));
                            _knownPaths.Add(file);
                        }
                        catch { }
                    }

                    if (newApps.Any())
                    {
                        try
                        {
                            _appControlRepo.BulkUpsertApps(newApps);

                            // Trigger instant sync!
                            _stateManager.SyncDetectedApps();
                        }
                        catch (Exception ex)
                        {
                            EngineLogger.Log("AppDiscovery", $"[ERROR] DB write: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                // No files in queue, sleep lightly
                await Task.Delay(500, stoppingToken);
            }
        }
    }

    private void InitializeWatchers()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            try
            {
                var watcher = new FileSystemWatcher(drive.RootDirectory.FullName)
                {
                    Filter = "*.exe",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    InternalBufferSize = 65536
                };

                watcher.Created += OnExeEvent;
                watcher.Renamed += OnExeEvent;
                watcher.Changed += OnExeEvent;

                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch
            {
                // Access denied on some specific locked drives
            }
        }
    }

    private void OnExeEvent(object sender, FileSystemEventArgs e)
    {
        // Filter at the event level
        if (!SystemPathGuard.IsProtected(e.FullPath))
        {
            _debouncerQueue.Enqueue(e.FullPath);
        }
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
        base.Dispose();
    }
}



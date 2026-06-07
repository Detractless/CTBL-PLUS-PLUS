using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Security.Lockdown;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Holds open FileStream handles on all core binaries in the installation directory.
/// This prevents any other process (even Admin/SYSTEM) from modifying, renaming, 
/// or deleting the files while the Engine is running.
/// </summary>
public class BinaryFileLockService : BackgroundService
{
    private readonly IInstallStateProvider _installState;
    private readonly List<FileStream> _lockedHandles = new List<FileStream>();

    public BinaryFileLockService(IInstallStateProvider installState)
    {
        _installState = installState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_installState.IsInstalled())
        {
            LockdownLogger.Log("Lockdown.FileLock", "Running in portable/dev mode - BinaryFileLockService is DISABLED.");
            return;
        }

        try
        {
            AcquireLocks();
            
            // Keep the handles open until the service stops
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { /* Normal shutdown */ }
        finally
        {
            ReleaseLocks();
        }
    }

    private void AcquireLocks()
    {
        string installDir = AppDomain.CurrentDomain.BaseDirectory;
        var targets = Directory.EnumerateFiles(installDir, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || 
                        f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));

        int successCount = 0;
        int failCount = 0;

        foreach (var file in targets)
        {
            try
            {
                // Open with Read access and Read sharing. 
                // This blocks any other process from opening with Write access.
                var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                _lockedHandles.Add(fs);
                successCount++;
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.FileLock", $"Failed to lock {Path.GetFileName(file)}: {ex.Message}");
                failCount++;
            }
        }

        LockdownLogger.Log("Lockdown.FileLock", $"Active binary locking complete. Locked: {successCount}, Failed: {failCount}");
    }

    private void ReleaseLocks()
    {
        foreach (var handle in _lockedHandles)
        {
            try { handle.Dispose(); } catch { }
        }
        _lockedHandles.Clear();
        LockdownLogger.Log("Lockdown.FileLock", "Binary file handles released.");
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        ReleaseLocks();
        return base.StopAsync(cancellationToken);
    }
}

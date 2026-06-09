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
/// Periodically verifies the SHA256 hashes of the installation directory
/// against the sealed manifest. Auto-restores tampered files from the vault.
/// </summary>
public class IntegrityVerificationService : BackgroundService
{
    private readonly IInstallStateProvider _installState;
    private readonly IFileDeleter _fileDeleter;

    public IntegrityVerificationService(
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
            LockdownLogger.Log("Lockdown.Integrity", "Running in portable/dev mode - IntegrityVerificationService is DISABLED.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                VerifyIntegrity();
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.Integrity", $"Integrity check loop error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private void VerifyIntegrity()
    {
        var manifest = VaultRecoveryService.LoadManifest();
        if (manifest == null)
        {
            LockdownLogger.Log("Lockdown.Integrity", "Secure manifest not found in vault. Integrity check skipped.");
            return;
        }

        string installDir = AppDomain.CurrentDomain.BaseDirectory;
        var currentFiles = Directory.EnumerateFiles(installDir, "*.*", SearchOption.TopDirectoryOnly)
            .ToDictionary(f => Path.GetFileName(f), f => f, StringComparer.OrdinalIgnoreCase);

        bool restorationNeeded = false;

        // 1. Check for tampered or missing files
        foreach (var kvp in manifest)
        {
            string fileName = kvp.Key;
            string expectedHash = kvp.Value;

            if (!currentFiles.TryGetValue(fileName, out string? fullPath))
            {
                LockdownLogger.Log("Lockdown.Integrity", $"Missing core file: {fileName}. Restoration required.");
                restorationNeeded = true;
                continue;
            }

            string actualHash = AppIdentityResolver.GetFileHashSha256(fullPath);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                LockdownLogger.Log("Lockdown.Integrity", $"Tampered binary detected: {fileName}. Restoration required.");
                restorationNeeded = true;
            }
        }

        // 2. Check for foreign files
        foreach (var fileName in currentFiles.Keys)
        {
            if (!manifest.ContainsKey(fileName))
            {
                LockdownLogger.Log("Lockdown.Integrity", $"Foreign file detected: {fileName}. Initiating deletion.");
                _fileDeleter.DeleteWithRetry(currentFiles[fileName]);
            }
        }

        // 3. Perform restoration if any core file is missing or tampered
        if (restorationNeeded)
        {
            LockdownLogger.Log("Lockdown.Integrity", "Integrity compromise detected. Initiating vault recovery...");
            // Phase 06 fix: empty prefix = root-level entries in the flat-layout ZIP
            bool success = VaultRecoveryService.RestoreTarget("", installDir);
            if (success)
                LockdownLogger.Log("Lockdown.Integrity", "Vault recovery successful. Binaries restored to baseline.");
            else
                LockdownLogger.Log("Lockdown.Integrity", "CRITICAL: Vault recovery failed!");
        }
    }
}

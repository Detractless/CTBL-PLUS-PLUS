using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Lockdown;

/// <summary>
/// Periodically re-enforces restrictive NTFS ACLs on the installation
/// directory and the secure vault to combat ownership takeovers.
/// Phase 07: refactored from icacls.exe child processes to in-process ACL API.
/// </summary>
public class VaultAclEnforcementService : BackgroundService
{
    private readonly IInstallStateProvider _installState;

    public VaultAclEnforcementService(IInstallStateProvider installState)
    {
        _installState = installState;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_installState.IsInstalled())
        {
            LockdownLogger.Log("Lockdown.Acl", "Running in portable/dev mode — VaultAclEnforcementService is DISABLED.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                EnforceDirectoryAcls();
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.Acl", $"ACL enforcement loop error: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    private void EnforceDirectoryAcls()
    {
        try
        {
            string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            // 1. Deny Administrators write/delete/ownership on install dir
            AclHelper.DenyAdminWrite(installDir);

            // 2. Harden the vault (SYSTEM-only, strip inheritance, deny Admin)
            string vaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CtblPlusPlus", "vault");

            if (Directory.Exists(vaultDir))
            {
                AclHelper.HardenVault(vaultDir);
            }

            LockdownLogger.Log("Lockdown.Acl", "Directory and Vault ACLs re-enforced.");
        }
        catch (Exception ex)
        {
            LockdownLogger.Log("Lockdown.Acl", $"Failed to re-enforce ACLs: {ex.Message}");
        }
    }
}

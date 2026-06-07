using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Security.Lockdown;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    public class PersistenceEnforcer : BackgroundService
    {
        public string Name => "Advanced Persistence & Self-Healing";

        private readonly IInstallStateProvider _installState;
        private readonly string _logPath;

        public PersistenceEnforcer(IInstallStateProvider installState)
        {
            _installState = installState;
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "security_log.txt");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_installState.IsInstalled())
            {
                Log("Running in portable/dev mode - PersistenceEnforcer is DISABLED.");
                return;
            }

            // Mark current process as Critical (BSOD on Kill)
            bool criticalSet = NativeMethods.SetCriticalProcess(true);
            Log(criticalSet ? "Process marked as CRITICAL." : "FAILED to mark process as critical (Check Admin rights).");

            Log("Advanced Persistence Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Enforce ACLs (Surgical Deny) - 5-second high-frequency pulse
                    EnforceAcls();

                    await Task.Delay(5000, stoppingToken); // 5 second pulse
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log($"Health loop error: {ex.Message}");
                    await Task.Delay(10000, stoppingToken);
                }
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            NativeMethods.SetCriticalProcess(false);
            Log("Advanced Persistence Service stopped.");
            return base.StopAsync(cancellationToken);
        }

        private void EnforceAcls()
        {
            try
            {
                string installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
                string vaultDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "vault");

                // Phase 07: in-process ACL enforcement via AclHelper (replaces icacls.exe)
                AclHelper.DenyAdminWrite(installDir);

                if (Directory.Exists(vaultDir))
                {
                    AclHelper.DenyAdminWrite(vaultDir);
                }

                // Protect Cold Turkey database from deletion (preserves write for CT service)
                string ctData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Cold Turkey");
                if (Directory.Exists(ctData))
                {
                    AclHelper.DenyAdminDelete(ctData);
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:O}: [Persistence] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}


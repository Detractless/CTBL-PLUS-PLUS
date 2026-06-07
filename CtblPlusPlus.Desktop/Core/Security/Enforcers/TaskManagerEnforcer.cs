using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    /// <summary>
    /// Implements system-level enforcement to prevent the user from launching the Windows Task Manager.
    /// Operates by hijacking the Image File Execution Options (IFEO) debugger registry key.
    /// </summary>
    public class TaskManagerEnforcer : BackgroundService
    {
        public string Name { get { return "Task Manager Protection"; } }

        private const string IFEO_KEY = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
        private readonly string[] TARGET_EXES = { 
            "Taskmgr.exe", 
            "procexp.exe", 
            "ProcessHacker.exe", 
            "Resmon.exe", 
            "Regedit.exe",
            // "cmd.exe",            — removed Phase 09: breaks bat and service internals
            // "powershell.exe",     — removed Phase 09: breaks bat and service internals
            // "powershell_ise.exe", — removed Phase 09
            // "pwsh.exe",           — removed Phase 09
            // "taskkill.exe" � removed: used internally by CtblCliClient to kill Cold Turkey processes
            // "sc.exe"       � removed: used internally by WindowsServiceMonitor to manage w32time
            // "net.exe",     // removed: no real security post-seal, standard users cannot damage CTBL++ regardless;
            // "net1.exe",    // also breaks Sealing.ps1 pre-seal via IFEO redirect
            // "wmic.exe",    // removed: same reasoning; also risks breaking WMI-based logoff in Sealing.ps1
            "msiexec.exe",
            "msconfig.exe",
            "mmc.exe",
            "SystemPropertiesAdvanced.exe"
        };

        private readonly IInstallStateProvider _installState;

        public TaskManagerEnforcer(IInstallStateProvider installState)
        {
            _installState = installState;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_installState.IsInstalled())
                    {
                        ApplyProtections();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Enforcement loop error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            if (_installState.IsInstalled())
            {
                RevertProtections();
            }
            return base.StopAsync(cancellationToken);
        }

        private void ApplyProtections()
        {
            try
            {
                foreach (var exe in TARGET_EXES)
                {
                    SetRegistryValue(Registry.LocalMachine, IFEO_KEY + @"\" + exe, "Debugger", "rundll32.exe", RegistryValueKind.String);
                }

                Log("System Tool Protection Enabled (IFEO) for: " + string.Join(", ", TARGET_EXES));
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to enable System Tool Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to apply System Tool Protection: " + ex.Message);
            }
        }

        private void RevertProtections()
        {
            try
            {
                foreach (var exe in TARGET_EXES)
                {
                    RemoveRegistryValue(Registry.LocalMachine, IFEO_KEY + @"\" + exe, "Debugger");
                }

                Log("System Tool Protection Disabled.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to disable System Tool Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to stop System Tool Protection: " + ex.Message);
            }
        }

        private void SetRegistryValue(RegistryKey root, string subKeyName, string valueName, object value, RegistryValueKind kind)
        {
            using (RegistryKey key = root.CreateSubKey(subKeyName))
            {
                if (key != null)
                {
                    key.SetValue(valueName, value, kind);
                }
            }
        }

        private void RemoveRegistryValue(RegistryKey root, string subKeyName, string valueName)
        {
            try
            {
                using (RegistryKey? key = root.OpenSubKey(subKeyName, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(valueName, false);
                    }
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("O") + ": [TaskMgrProtection] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}



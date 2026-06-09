using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    public class UninstallerEnforcer : BackgroundService
    {
        public string Name => "Uninstaller Saboteur";

        private const string UNINSTALL_ROOT = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        private readonly IInstallStateProvider _installState;
        private readonly string _logPath;

        public UninstallerEnforcer(IInstallStateProvider installState)
        {
            _installState = installState;
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "security_log.txt");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_installState.IsInstalled())
                    {
                        Sabotage("CtblPlusPlus");
                    }
                }
                catch (Exception ex)
                {
                    Log($"Sabotage failed: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        // StopAsync is intentionally a no-op — the sabotage is permanent for the session
        // and should not be reverted upon service stop.

        private void Sabotage(string appName)
        {
            // Search in HKLM (64-bit and 32-bit views)
            SabotageRegistryKey(Registry.LocalMachine, UNINSTALL_ROOT, appName);
            SabotageRegistryKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", appName);
            
            // Search in HKCU
            SabotageRegistryKey(Registry.CurrentUser, UNINSTALL_ROOT, appName);
        }

        private void SabotageRegistryKey(RegistryKey root, string subKeyBase, string appName)
        {
            try
            {
                using (RegistryKey? parent = root.OpenSubKey(subKeyBase, true))
                {
                    if (parent == null) return;

                    foreach (var subKeyName in parent.GetSubKeyNames())
                    {
                        using (RegistryKey? key = parent.OpenSubKey(subKeyName, true))
                        {
                            if (key == null) continue;

                            string? displayName = key.GetValue("DisplayName") as string;
                            if (displayName != null && (displayName.Contains(appName, StringComparison.OrdinalIgnoreCase) || subKeyName.Contains(appName, StringComparison.OrdinalIgnoreCase)))
                            {
                                // 1. Hide from Add/Remove Programs
                                key.SetValue("SystemComponent", 1, RegistryValueKind.DWord);
                                key.SetValue("NoRemove", 1, RegistryValueKind.DWord);
                                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

                                // 2. Sabotage the uninstaller strings
                                key.SetValue("UninstallString", "rundll32.exe", RegistryValueKind.String);
                                key.SetValue("QuietUninstallString", "rundll32.exe", RegistryValueKind.String);
                                
                                // 3. Delete the icon
                                key.DeleteValue("DisplayIcon", false);
                                
                                Log($"Found and sabotaged uninstaller key: {subKeyName}");
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:O}: [UninstallerEnforcer] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}



using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    /// <summary>
    /// Implements system-level enforcement to prevent the user from performing a Windows Factory Reset 
    /// or accessing Advanced Startup Options. Relies on Group Policy, Registry obfuscation, and the Windows Recovery Environment (WinRE) tool (`reagentc.exe`).
    /// </summary>
    public class FactoryResetEnforcer : BackgroundService
    {
        public string Name { get { return "Factory Reset Protection"; } }

        private const string SYSTEM_POLICY_KEY = @"SOFTWARE\Policies\Microsoft\Windows\System";
        private const string EXPLORER_POLICY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        private readonly IInstallStateProvider _installState;

        public FactoryResetEnforcer(IInstallStateProvider installState)
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
                // 1. Registry Policy Enforcement
                SetRegistryValue(Registry.LocalMachine, SYSTEM_POLICY_KEY, "DisableReset", 1, RegistryValueKind.DWord);
                SetRegistryValue(Registry.LocalMachine, SYSTEM_POLICY_KEY, "DisableAdvanceStartup", 1, RegistryValueKind.DWord);

                // 2. UI Visibility Suppression (Stealth Mode)
                AppendHideRecovery();

                // 3. Infrastructure Disabling (WinRE)
                RunExternalProcess("reagentc.exe", "/disable");

                // 4. Boot Configuration Hardening (bcdedit)
                RunExternalProcess("bcdedit.exe", "/set {bootmgr} displaybootmenu no");
                RunExternalProcess("bcdedit.exe", "/set {current} bootems off");
                RunExternalProcess("bcdedit.exe", "/set {current} advancedoptions off");
                RunExternalProcess("bcdedit.exe", "/set {current} optionsedit off");
                RunExternalProcess("bcdedit.exe", "/set {current} bootstatuspolicy IgnoreAllFailures");
                RunExternalProcess("bcdedit.exe", "/set {current} recoveryenabled off");

                Log("Factory Reset Protection Enabled.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to enable Factory Reset Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to apply Factory Reset Protection: " + ex.Message);
            }
        }

        private void RevertProtections()
        {
            try
            {
                // 1. Revert Registry Policy
                RemoveRegistryValue(Registry.LocalMachine, SYSTEM_POLICY_KEY, "DisableReset");
                RemoveRegistryValue(Registry.LocalMachine, SYSTEM_POLICY_KEY, "DisableAdvanceStartup");

                // 2. Revert UI Visibility
                RemoveHideRecovery();

                // 3. Restore Infrastructure
                RunExternalProcess("reagentc.exe", "/enable");

                // 4. Restore Boot Configuration
                RunExternalProcess("bcdedit.exe", "/set {current} bootems on");
                RunExternalProcess("bcdedit.exe", "/set {current} advancedoptions on");
                RunExternalProcess("bcdedit.exe", "/set {current} optionsedit on");
                RunExternalProcess("bcdedit.exe", "/set {current} bootstatuspolicy DisplayAllFailures");
                RunExternalProcess("bcdedit.exe", "/set {current} recoveryenabled yes");

                Log("Factory Reset Protection Disabled.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to disable Factory Reset Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to stop Factory Reset Protection: " + ex.Message);
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

        private void AppendHideRecovery()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(EXPLORER_POLICY_KEY))
            {
                if (key == null) return;

                string? currentVal = key.GetValue("SettingsPageVisibility", "") as string;
                if (currentVal == null) currentVal = "";
                
                if (currentVal.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentVal.ToLower().Contains("recovery"))
                    {
                        string newVal = RemoveToken(currentVal, "recovery");
                        key.SetValue("SettingsPageVisibility", newVal);
                    }
                }
                else
                {
                    if (!currentVal.Trim().StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(currentVal.Trim()))
                        {
                            key.SetValue("SettingsPageVisibility", "hide:recovery");
                        }
                        else
                        {
                            key.SetValue("SettingsPageVisibility", "hide:recovery;" + currentVal);
                        }
                    }
                    else
                    {
                        if (!currentVal.ToLower().Contains("recovery"))
                        {
                            string sep = currentVal.Trim().EndsWith(";") ? "" : ";";
                            key.SetValue("SettingsPageVisibility", currentVal + sep + "recovery");
                        }
                    }
                }
            }
        }

        private void RemoveHideRecovery()
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(EXPLORER_POLICY_KEY, true))
            {
                if (key == null) return;

                string? currentVal = key.GetValue("SettingsPageVisibility", "") as string;
                if (string.IsNullOrEmpty(currentVal)) return;

                if (currentVal.ToLower().Contains("recovery"))
                {
                     string newVal = RemoveToken(currentVal, "recovery");
                     key.SetValue("SettingsPageVisibility", newVal);
                }
            }
        }

        private string RemoveToken(string list, string token)
        {
            string prefix = "";
            string body = list;

            if (list.StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
            {
                prefix = list.Substring(0, 5); 
                body = list.Substring(5);
            }
            else if (list.StartsWith("show:", StringComparison.OrdinalIgnoreCase))
            {
                prefix = list.Substring(0, 5); 
                body = list.Substring(5);
            }

            var parts = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new global::System.Collections.Generic.List<string>();
            foreach(var p in parts)
            {
                if (!p.Trim().Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(p);
                }
            }
            
            return prefix + string.Join(";", kept);
        }

        private void RunExternalProcess(string fileName, string arguments)
        {
            ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            
            using (var proc = Process.Start(psi))
            {
                proc?.WaitForExit(5000);
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("O") + ": [FactoryResetProtection] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}



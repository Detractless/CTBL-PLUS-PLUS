using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    /// <summary>
    /// Implements system-level enforcement to prevent the user from accessing Windows Account Control panels.
    /// Relies on Registry Policy manipulation and aggressive process termination.
    /// </summary>
    public class AccountEnforcer : BackgroundService
    {
        public string Name { get { return "Account Protection"; } }

        private const string EXPLORER_POLICY_KEY = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private readonly string[] ACCOUNT_URIS = { 
            "account", "accounts", 
            "yourinfo", "emailandaccounts", "signinoptions", "workplace", "otherusers", "sync" 
        };

        private readonly IInstallStateProvider _installState;

        public AccountEnforcer(IInstallStateProvider installState)
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
                AppendHideAccounts();
                
                KillProcess("SystemSettings");

                Log("Account Protection Enabled.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to enable Account Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to apply Account Protection: " + ex.Message);
            }
        }

        private void RevertProtections()
        {
            try
            {
                RemoveHideAccounts();
                
                KillProcess("SystemSettings");

                Log("Account Protection Disabled.");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log("Requires Admin rights to disable Account Protection: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log("Failed to stop Account Protection: " + ex.Message);
            }
        }

        private void AppendHideAccounts()
        {
            using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(EXPLORER_POLICY_KEY))
            {
                if (key == null) return;

                string? currentVal = key.GetValue("SettingsPageVisibility", "") as string;
                if (currentVal == null) currentVal = "";
                
                string newVal = currentVal;
                bool changed = false;
                
                if (newVal.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var uri in ACCOUNT_URIS)
                    {
                        if (newVal.ToLower().Contains(uri))
                        {
                            newVal = RemoveToken(newVal, uri);
                            changed = true;
                        }
                    }
                }
                else
                {
                    if (!newVal.Trim().StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.IsNullOrEmpty(newVal.Trim()))
                        {
                            newVal = "hide:";
                            changed = true;
                        }
                        else
                        {
                            newVal = "hide:" + newVal;
                        }
                    }

                    foreach (var uri in ACCOUNT_URIS)
                    {
                        if (!newVal.ToLower().Contains(uri))
                        {
                            string sep = newVal.Trim().EndsWith(";") || newVal.Trim().EndsWith(":") ? "" : ";";
                            newVal += sep + uri;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    key.SetValue("SettingsPageVisibility", newVal);
                }
            }
        }

        private void RemoveHideAccounts()
        {
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(EXPLORER_POLICY_KEY, true))
            {
                if (key == null) return;

                string? currentVal = key.GetValue("SettingsPageVisibility", "") as string;
                if (string.IsNullOrEmpty(currentVal)) return;

                string newVal = currentVal;
                bool changed = false;
                
                if (newVal.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
                {
                     foreach (var uri in ACCOUNT_URIS)
                    {
                        if (!newVal.ToLower().Contains(uri))
                        {
                            string sep = newVal.Trim().EndsWith(";") || newVal.Trim().EndsWith(":") ? "" : ";";
                            newVal += sep + uri;
                            changed = true;
                        }
                    }
                }
                else
                {
                    foreach (var uri in ACCOUNT_URIS)
                    {
                        if (newVal.ToLower().Contains(uri))
                        {
                            newVal = RemoveToken(newVal, uri);
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                     key.SetValue("SettingsPageVisibility", newVal);
                }
            }
        }

        private string RemoveToken(string list, string token)
        {
            string prefix = "";
            string body = list;

            if (list.Trim().StartsWith("hide:", StringComparison.OrdinalIgnoreCase))
            {
                prefix = list.Substring(0, 5);
                body = list.Substring(5);
            }
            else if (list.Trim().StartsWith("show:", StringComparison.OrdinalIgnoreCase))
            {
                prefix = list.Substring(0, 5);
                body = list.Substring(5);
            }

            var parts = body.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            foreach(var p in parts)
            {
                if (!p.Trim().Equals(token, StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(p);
                }
            }
            
            return prefix + string.Join(";", kept);
        }

        private void KillProcess(string processName)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(processName))
                {
                    proc.Kill();
                }
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
                File.AppendAllText(logPath, DateTime.Now.ToString("O") + ": [AccountProtection] " + message + Environment.NewLine);
            }
            catch { }
        }
    }
}



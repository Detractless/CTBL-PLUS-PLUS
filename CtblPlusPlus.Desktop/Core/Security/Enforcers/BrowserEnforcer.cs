using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    public class BrowserExtensionPolicy
    {
        public string BrowserName { get; set; }
        public string RegistryPath { get; set; }
        public string ExtensionId { get; set; }
        public string UpdateUrl { get; set; }
        public string FullValue => $"{ExtensionId};{UpdateUrl}";
    }

    public class DwordRegistryPolicy
    {
        public string RegistryPath { get; set; }
        public string ValueName { get; set; }
        public int ValueData { get; set; }
    }

    public class BrowserEnforcer : BackgroundService
    {
        private readonly List<BrowserExtensionPolicy> _policies;
        private readonly List<DwordRegistryPolicy> _safeSearchPolicies;
        private readonly IInstallStateProvider _installState;
        private const string HKLM_POLICIES_ROOT = @"SOFTWARE\Policies";

        public BrowserEnforcer(IInstallStateProvider installState)
        {
            _installState = installState;
            _policies = new List<BrowserExtensionPolicy>
            {
                new BrowserExtensionPolicy 
                { 
                    BrowserName = "Chrome", 
                    RegistryPath = @"Google\Chrome\ExtensionInstallForcelist",
                    ExtensionId = "pganeibhckoanndahmnfggfoeofncnii",
                    UpdateUrl = "https://clients2.google.com/service/update2/crx"
                },
                new BrowserExtensionPolicy 
                { 
                    BrowserName = "Edge", 
                    RegistryPath = @"Microsoft\Edge\ExtensionInstallForcelist",
                    ExtensionId = "jfphahkinplobmabmgjmjgflbhjjddeb",
                    UpdateUrl = "https://edge.microsoft.com/extensionwebstorebase/v1/crx"
                },
                new BrowserExtensionPolicy 
                { 
                    BrowserName = "Vivaldi", 
                    RegistryPath = @"Vivaldi\ExtensionInstallForcelist",
                    ExtensionId = "pganeibhckoanndahmnfggfoeofncnii",
                    UpdateUrl = "https://clients2.google.com/service/update2/crx"
                }
            };

            _safeSearchPolicies = new List<DwordRegistryPolicy>
            {
                new DwordRegistryPolicy { RegistryPath = @"Google\Chrome", ValueName = "ForceGoogleSafeSearch", ValueData = 1 },
                new DwordRegistryPolicy { RegistryPath = @"Microsoft\Edge", ValueName = "ForceGoogleSafeSearch", ValueData = 1 },
                new DwordRegistryPolicy { RegistryPath = @"Microsoft\Edge", ValueName = "ForceBingSafeSearch", ValueData = 2 },
                new DwordRegistryPolicy { RegistryPath = @"Chromium", ValueName = "ForceGoogleSafeSearch", ValueData = 1 }
            };
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_installState.IsInstalled())
                    {
                        Enforce();
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
                Revoke();
            }
            return base.StopAsync(cancellationToken);
        }

        private void Enforce()
        {
            foreach (var policy in _policies)
            {
                try
                {
                    ApplyPolicy(policy);
                }
                catch (Exception ex)
                {
                    Log($"Failed to apply extension policy for {policy.BrowserName}: {ex.Message}");
                }
            }

            foreach (var policy in _safeSearchPolicies)
            {
                try
                {
                    ApplyDwordPolicy(policy);
                }
                catch (Exception ex)
                {
                    Log($"Failed to apply SafeSearch policy for {policy.RegistryPath}: {ex.Message}");
                }
            }
        }

        private void Revoke()
        {
            foreach (var policy in _policies)
            {
                try
                {
                    RemovePolicy(policy);
                }
                catch (Exception ex)
                {
                    Log($"Failed to revoke extension policy for {policy.BrowserName}: {ex.Message}");
                }
            }

            foreach (var policy in _safeSearchPolicies)
            {
                try
                {
                    RemoveDwordPolicy(policy);
                }
                catch (Exception ex)
                {
                    Log($"Failed to revoke SafeSearch policy for {policy.RegistryPath}: {ex.Message}");
                }
            }
        }

        private void ApplyPolicy(BrowserExtensionPolicy policy)
        {
            string fullPath = Path.Combine(HKLM_POLICIES_ROOT, policy.RegistryPath);
            
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(fullPath, true))
            {
                if (key == null) throw new Exception("Could not create or open registry key.");

                // 1. Check if already present
                var valueNames = key.GetValueNames();
                foreach (var name in valueNames)
                {
                    var val = key.GetValue(name) as string;
                    if (val != null && val.StartsWith(policy.ExtensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Already exists, potentially update URL if different
                        if (!val.Equals(policy.FullValue, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(name, policy.FullValue);
                            Log($"Updated extension policy for {policy.BrowserName} at index {name}");
                        }
                        return;
                    }
                }

                // 2. Find next available index
                int maxIndex = 0;
                foreach (var name in valueNames)
                {
                    if (int.TryParse(name, out int index))
                    {
                        if (index > maxIndex) maxIndex = index;
                    }
                }

                string nextIndex = (maxIndex + 1).ToString();
                key.SetValue(nextIndex, policy.FullValue);
                Log($"Enforced extension for {policy.BrowserName} at index {nextIndex}");
            }
        }

        private void RemovePolicy(BrowserExtensionPolicy policy)
        {
            string fullPath = Path.Combine(HKLM_POLICIES_ROOT, policy.RegistryPath);
            
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(fullPath, true))
            {
                if (key == null) return; // Path doesn't exist, nothing to revoke

                var valueNames = key.GetValueNames();
                foreach (var name in valueNames)
                {
                    var val = key.GetValue(name) as string;
                    if (val != null && val.StartsWith(policy.ExtensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        key.DeleteValue(name);
                        Log($"Revoked extension for {policy.BrowserName} from index {name}");
                    }
                }
            }
        }

        private void ApplyDwordPolicy(DwordRegistryPolicy policy)
        {
            string fullPath = Path.Combine(HKLM_POLICIES_ROOT, policy.RegistryPath);
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(fullPath, true))
            {
                if (key == null) throw new Exception("Could not create or open registry key.");
                
                var currentValue = key.GetValue(policy.ValueName);
                if (currentValue == null || !(currentValue is int val) || val != policy.ValueData)
                {
                    key.SetValue(policy.ValueName, policy.ValueData, RegistryValueKind.DWord);
                    Log($"Enforced SafeSearch policy at {policy.RegistryPath}\\{policy.ValueName} to {policy.ValueData}");
                }
            }
        }

        private void RemoveDwordPolicy(DwordRegistryPolicy policy)
        {
            string fullPath = Path.Combine(HKLM_POLICIES_ROOT, policy.RegistryPath);
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(fullPath, true))
            {
                if (key == null) return;
                
                if (key.GetValue(policy.ValueName) != null)
                {
                    key.DeleteValue(policy.ValueName);
                    Log($"Revoked SafeSearch policy at {policy.RegistryPath}\\{policy.ValueName}");
                }
            }
        }

        private void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "extension_log.txt");
                string dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, $"{DateTime.Now:O}: [BrowserEnforcer] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}



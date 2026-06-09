using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.Security.Enforcers
{
    /// <summary>
    /// Enforces User Rights Assignment policies, specifically stripping SeSystemtimePrivilege 
    /// from non-system accounts to prevent manual clock manipulation.
    /// </summary>
    public class PrivilegeEnforcer : BackgroundService
    {
        public string Name => "Privilege Enforcement";

        private readonly string _workingDir;
        private readonly string _cfgPath;
        private readonly string _dbPath;
        private readonly string _logPath;
        private readonly IInstallStateProvider _installState;

        public PrivilegeEnforcer(IInstallStateProvider installState)
        {
            _installState = installState;
            _workingDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "security");
            if (!Directory.Exists(_workingDir)) Directory.CreateDirectory(_workingDir);

            _cfgPath = Path.Combine(_workingDir, "privileges.inf");
            _dbPath = Path.Combine(_workingDir, "secedit.sdb");
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
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
                Log("Locking down SeSystemtimePrivilege...");
                // Target: SYSTEM (S-1-5-18) and LOCAL SERVICE (S-1-5-19)
                ApplyPrivilegePolicy("SeSystemtimePrivilege", "*S-1-5-18,*S-1-5-19");
                Log("SeSystemtimePrivilege locked to SYSTEM and LOCAL SERVICE.");
            }
            catch (Exception ex)
            {
                Log($"Failed to apply Privilege Enforcement: {ex.Message}");
            }
        }

        private void RevertProtections()
        {
            try
            {
                Log("Restoring SeSystemtimePrivilege to Administrators...");
                // Restore: SYSTEM, LOCAL SERVICE, and Administrators (S-1-5-32-544)
                ApplyPrivilegePolicy("SeSystemtimePrivilege", "*S-1-5-18,*S-1-5-19,*S-1-5-32-544");
                Log("SeSystemtimePrivilege restored.");
            }
            catch (Exception ex)
            {
                Log($"Failed to stop Privilege Enforcement: {ex.Message}");
            }
        }

        private void ApplyPrivilegePolicy(string privilegeName, string accounts)
        {
            // 1. Export current policy
            RunSecedit($"/export /cfg \"{_cfgPath}\" /areas USER_RIGHTS");

            if (!File.Exists(_cfgPath))
            {
                throw new Exception("Failed to export security policy via secedit.");
            }

            // 2. Modify the INF file
            // Secedit exports in Unicode (UTF-16 LE with BOM)
            string[] lines = File.ReadAllLines(_cfgPath, Encoding.Unicode);
            bool found = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().StartsWith(privilegeName, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"{privilegeName} = {accounts}";
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // If the privilege wasn't in the export, we need to add it under [Privilege Rights]
                var updatedLines = lines.ToList();
                int sectionIndex = updatedLines.FindIndex(l => l.Trim().Equals("[Privilege Rights]", StringComparison.OrdinalIgnoreCase));
                
                if (sectionIndex != -1)
                {
                    updatedLines.Insert(sectionIndex + 1, $"{privilegeName} = {accounts}");
                }
                else
                {
                    updatedLines.Add("[Privilege Rights]");
                    updatedLines.Add($"{privilegeName} = {accounts}");
                }
                lines = updatedLines.ToArray();
            }

            File.WriteAllLines(_cfgPath, lines, Encoding.Unicode);

            // 3. Configure/Apply the policy
            // We use a temporary database for the application
            RunSecedit($"/configure /db \"{_dbPath}\" /cfg \"{_cfgPath}\" /areas USER_RIGHTS");
            
            // Clean up
            if (File.Exists(_cfgPath)) File.Delete(_cfgPath);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        private void RunSecedit(string args)
        {
            var psi = new ProcessStartInfo("secedit.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(psi))
            {
                process?.WaitForExit();
                if (process?.ExitCode != 0 && process?.ExitCode != 1) // 1 is often "completed with warnings" in secedit
                {
                    // Log warning but don't necessarily throw if it's just a warning
                }
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:O}: [PrivilegeEnforcement] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}



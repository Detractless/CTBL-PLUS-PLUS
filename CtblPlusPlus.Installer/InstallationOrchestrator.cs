using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using CtblPlusPlus.Core.Security;

namespace CtblPlusPlus.Installer;

public class InstallationOrchestrator
{
    private const string TargetDir = @"C:\Program Files\CTBL Queue Delay";
    private string? _tempZipPath;

    public void ExtractPayload()
    {
        if (Directory.Exists(TargetDir))
        {
            throw new Exception(
                "Install directory still exists after teardown. " +
                "This usually means a file inside it is still locked. " +
                "Reboot and re-run the installer, or delete the folder manually: " + TargetDir);
        }

        // Retry loop: guards against the brief DELETE_PENDING window that can occur
        // when DeleteInstallDirectory's fallback path (Directory.Delete) is used and
        // an external process (antivirus, indexer) still holds a handle to the path.
        // In the normal case (Directory.Move succeeded) this executes on the first try.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            try
            {
                Directory.CreateDirectory(TargetDir);
                break;
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(500);
            }
        }

        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CtblPlusPlus.Installer.Payload.zip");
        if (stream == null) throw new Exception("Payload.zip not found in embedded resources.");

        _tempZipPath = Path.Combine(Path.GetTempPath(), "detractless_payload.zip");
        using (var fileStream = new FileStream(_tempZipPath, FileMode.Create))
        {
            stream.CopyTo(fileStream);
        }

        ZipFile.ExtractToDirectory(_tempZipPath, TargetDir, true);
        // Temp zip is NOT deleted here — SealSecureVault needs it.
        // It will be cleaned up after vault sealing.
    }

    public void KillProcesses()
    {
        // NOTE: Only call this AFTER StopAndDeleteServices().
        // The Engine marks itself critical via RtlSetProcessIsCritical — killing it directly
        // triggers a BSOD. StopAndDeleteServices() triggers StopAsync() which uncriticalizes
        // the process first. By the time KillProcesses() runs, any survivors are already
        // uncritical (clean stop completed; they just haven't exited yet).
        string[] targets =
        {
            "CtblPlusPlus",         // UI
            "CtblPlusPlus.Engine",  // Engine service
            "CtblPlusPlus.Wd1",     // Watchdog Primary
            "CtblPlusPlus.Wd2"      // Watchdog Secondary
        };
        var living = new List<Process>();

        foreach (var t in targets)
            foreach (var p in Process.GetProcessesByName(t))
                try { p.Kill(); living.Add(p); } catch { }

        // Wait up to 5 s for all killed processes to fully exit and release handles.
        foreach (var p in living)
            try { p.WaitForExit(5000); } catch { }
    }

    public void StopAndDeleteServices()
    {
        // Stop Engine first — it hosts PersistenceEnforcer, which would otherwise restart
        // the watchdogs mid-teardown. Watchdogs are plain hosted services with no
        // critical-process flag, so they can be stopped in any order after the Engine.
        string[] stopOrder =
        {
            "CTBL Queue Delay Engine",
            "CTBL Queue Delay Watchdog Primary",
            "CTBL Queue Delay Watchdog Secondary"
        };

        // Disable each service before stopping so the restart/0 failure action
        // cannot re-launch it between sc stop and sc delete.
        foreach (var name in stopOrder)
            RunCmd("sc", $"config \"{name}\" start= disabled");

        // Issue stop commands. For the Engine, sc stop triggers StopAsync() →
        // SetCriticalProcess(false) → clean exit. Failure restart does NOT fire on
        // a clean stop (exit code 0), and the disabled state is a second guard.
        foreach (var name in stopOrder)
            RunCmd("sc", $"stop \"{name}\"");

        // Poll until each service reaches Stopped (or is absent on a first install).
        foreach (var name in stopOrder)
        {
            try
            {
                using var sc = new ServiceController(name);
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
            catch (InvalidOperationException)
            {
                // Service doesn't exist — expected on a first install, fine to continue.
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                throw new Exception(
                    $"Service \"{name}\" did not stop within 30 seconds. " +
                    "Reboot and re-run the installer.");
            }
        }

        // Delete all service registrations.
        foreach (var name in stopOrder)
            RunCmd("sc", $"delete \"{name}\"");
    }

    public void StripInstallDirAcls()
    {
        if (!Directory.Exists(TargetDir)) return;

        // Reset all explicit ACEs back to inherited-only.
        // /reset  — replace explicit ACEs with inherited ACEs only
        // /t      — recurse into all subdirectories and files
        // /c      — continue past errors (handles any still-locked files)
        // /q      — suppress per-file output
        // Using /reset rather than /remove:d *SID so that any future deny entries added
        // by new versions of PersistenceEnforcer are also cleared automatically.
        RunCmd("icacls", $"\"{TargetDir}\" /reset /t /c /q");
    }

    public void StripVaultAcls()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CtblPlusPlus");
        if (Directory.Exists(dataDir))
            RunCmd("icacls", $"\"{dataDir}\" /reset /t /c /q");
    }

    public void DeleteInstallDirectory()
    {
        if (!Directory.Exists(TargetDir)) return;

        // Rename the directory to a temp name BEFORE deleting it.
        //
        // Why: Directory.Delete() calls RemoveDirectory() under the hood. If anything
        // still holds a handle to the directory at that instant (antivirus scanner,
        // Windows Search indexer, SCM finishing cleanup), RemoveDirectory() returns
        // success but marks the directory DELETE_PENDING in the kernel. From that
        // point Directory.Exists() returns false (looks gone), but CreateDirectoryW()
        // on the same path returns ERROR_ACCESS_DENIED — exactly the "Access to the
        // path is denied" error we are chasing.
        //
        // MoveFileExW on a DIRECTORY succeeds even when files inside are still held
        // open — it only renames the parent MFT entry. The rename is atomic and
        // instantly frees the original path, so ExtractPayload's CreateDirectory
        // never races with a DELETE_PENDING on that path.
        string tempPath = Path.Combine(
            Path.GetDirectoryName(TargetDir)!,
            Path.GetFileName(TargetDir) + "_removing_" + DateTime.UtcNow.Ticks);

        try
        {
            Directory.Move(TargetDir, tempPath);
        }
        catch
        {
            // Move failed — fall back to direct delete on the original path.
            // If this still leaves DELETE_PENDING, the retry loop in ExtractPayload
            // will wait it out before attempting to recreate the directory.
            try { Directory.Delete(TargetDir, recursive: true); } catch { }
            RunCmd("cmd", $"/c rd /s /q \"{TargetDir}\"");
            return;
        }

        // Original path is now free. Delete the renamed copy.
        // If this fails (locked files), the OS cleans it up on next boot — acceptable.
        try { Directory.Delete(tempPath, recursive: true); } catch { }
        RunCmd("cmd", $"/c rd /s /q \"{tempPath}\"");
    }

    public void RegisterServices()
    {
        // Each service has its own dedicated EXE — no arguments in binPath needed.
        string engineExe = Path.Combine(TargetDir, "CtblPlusPlus.Engine.exe");
        string wd1Exe    = Path.Combine(TargetDir, "CtblPlusPlus.Wd1.exe");
        string wd2Exe    = Path.Combine(TargetDir, "CtblPlusPlus.Wd2.exe");

        // 1. Engine — auto start
        RunCmd("sc", $"create \"CTBL Queue Delay Engine\" binPath= \"{engineExe}\" start= auto obj= \"LocalSystem\"");
        RunCmd("sc", "failure \"CTBL Queue Delay Engine\" reset= 0 actions= restart/5000/restart/5000/restart/5000");

        // 2. Watchdog Primary — delayed-auto (starts after Engine)
        RunCmd("sc", $"create \"CTBL Queue Delay Watchdog Primary\" binPath= \"{wd1Exe}\" start= delayed-auto obj= \"LocalSystem\"");
        RunCmd("sc", "failure \"CTBL Queue Delay Watchdog Primary\" reset= 0 actions= restart/5000/restart/5000/restart/5000");

        // 3. Watchdog Secondary — delayed-auto (starts after Engine)
        RunCmd("sc", $"create \"CTBL Queue Delay Watchdog Secondary\" binPath= \"{wd2Exe}\" start= delayed-auto obj= \"LocalSystem\"");
        RunCmd("sc", "failure \"CTBL Queue Delay Watchdog Secondary\" reset= 0 actions= restart/5000/restart/5000/restart/5000");

    }

    /// <summary>
    /// Generates the DPAPI-protected system.key used by DpapiHmacProvider.
    /// Must run BEFORE services start or the Engine will crash on startup.
    /// </summary>
    public void GenerateSystemKey()
    {
        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CtblPlusPlus");
        if (!Directory.Exists(dataDir))
            Directory.CreateDirectory(dataDir);

        string keyPath = Path.Combine(dataDir, "system.key");
        if (File.Exists(keyPath)) return; // already present

        RunCmd("powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"" +
            "Add-Type -AssemblyName System.Security; " +
            "$r = New-Object byte[] 32; " +
            "[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($r); " +
            "$e = [Security.Cryptography.ProtectedData]::Protect($r, $null, " +
            "[Security.Cryptography.DataProtectionScope]::LocalMachine); " +
            $"[IO.File]::WriteAllBytes('{keyPath}', $e)\"");
    }

    public void HardenVaultDirectory()
    {
        string vaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CtblPlusPlus", "vault");

        // Called after SealSecureVault(), so the directory is guaranteed to exist here.
        // /inheritance:r  — remove all inherited ACEs
        // /grant:r        — replace (not add) grants
        // *S-1-5-18       — SYSTEM
        // (OI)(CI)F       — propagate Full Control to object and container children
        if (Directory.Exists(vaultDir))
        {
            RunCmd("icacls", $"\"{vaultDir}\" /inheritance:r /grant:r *S-1-5-18:(OI)(CI)F");
        }
    }

    public void SealSecureVault()
    {
        if (_tempZipPath != null && File.Exists(_tempZipPath))
        {
            VaultRecoveryService.SealVault(_tempZipPath);
            VaultRecoveryService.SealManifest(TargetDir);
            File.Delete(_tempZipPath);
            _tempZipPath = null;
        }
    }

    public void StartServices()
    {
        // Start Engine first — it hosts PidBroker which watchdogs need to register with
        RunCmd("sc", "start \"CTBL Queue Delay Engine\"");
        Thread.Sleep(5000); // Wait for Engine to initialize PidBroker pipe
        RunCmd("sc", "start \"CTBL Queue Delay Watchdog Primary\"");
        RunCmd("sc", "start \"CTBL Queue Delay Watchdog Secondary\"");
    }

    public bool IsColdTurkeyInstalled()
    {
        string path = @"C:\Program Files\Cold Turkey\Cold Turkey Blocker.exe";
        return File.Exists(path);
    }

    public void LaunchColdTurkeyInstaller()
    {
        try
        {
            string installerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ColdTurkey_Installer.exe");
            if (File.Exists(installerPath))
            {
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("ColdTurkey_Installer.exe not found in the installer directory.", "Dependency Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to launch installer: {ex.Message}", "Launch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunCmd(string cmd, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit();
    }

    /// <summary>
    /// READ-ONLY. Returns the current members of the local Administrators group by
    /// parsing `net localgroup Administrators`. This method never modifies membership —
    /// account demotion is performed manually by the user during the sealing tutorial.
    /// Used only to display, for the user's reference, which accounts must be demoted.
    /// </summary>
    public List<string> GetAdministrators()
    {
        var members = new List<string>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = "localgroup Administrators",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            if (process == null) return members;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // `net localgroup` output: header lines, a row of dashes, the member names
            // (one per line), then a "The command completed successfully." trailer.
            var lines = output.Replace("\r\n", "\n").Split('\n');
            int dashIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("----")) { dashIndex = i; break; }
            }
            if (dashIndex >= 0)
            {
                for (int i = dashIndex + 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("The command completed")) break;
                    // Skip the built-in Administrator: it's disabled by default, isn't a
                    // user account, and doesn't appear in Settings > Other users anyway.
                    if (line.Equals("Administrator", StringComparison.OrdinalIgnoreCase)) continue;
                    members.Add(line);
                }
            }
        }
        catch
        {
            // Enumeration is best-effort; on failure the UI simply shows no list.
        }
        return members;
    }

    // ── Two-Pass Installer: Account Management ─────────────────────

    private const string CtblAccountName = "CTBLAdmin";
    private const string CtblInitialPassword = "123";

    /// <summary>Returns true if the current Windows user is the CTBL++ service account.</summary>
    public bool IsRunningAsCtblAccount()
    {
        return string.Equals(Environment.UserName, CtblAccountName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns true if the CTBL++ local account already exists.</summary>
    public bool CtblAccountExists()
    {
        var psi = new ProcessStartInfo("net", $"user \"{CtblAccountName}\"")
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
        return p?.ExitCode == 0;
    }

    /// <summary>Creates the CTBL++ local admin account with the initial password.</summary>
    public void CreateCtblAccount()
    {
        RunCmd("net", $"user \"{CtblAccountName}\" {CtblInitialPassword} /add");
        RunCmd("wmic", $"useraccount where \"name='{CtblAccountName}'\" set PasswordExpires=FALSE");
        RunCmd("net", $"localgroup Administrators \"{CtblAccountName}\" /add");
    }

    /// <summary>Creates a shortcut on the Public Desktop pointing to the installer EXE.</summary>
    public void CreateSetupShortcut()
    {
        string shortcutPath = @"C:\Users\Public\Desktop\Continue CTBL++ Setup.lnk";

        // Copy installer to a shared location so CTBLAdmin can access it
        // (the original may be in the current user's profile which other accounts can't read)
        string installerExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string installerDir = Path.GetDirectoryName(installerExe) ?? "";
        string sharedSetupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "CtblPlusPlus", "Setup");

        if (Directory.Exists(sharedSetupDir))
            Directory.Delete(sharedSetupDir, true);
        Directory.CreateDirectory(sharedSetupDir);

        // Copy all files from the installer's directory (EXE + dependencies + wwwroot)
        CopyDirectory(installerDir, sharedSetupDir);

        string sharedExe = Path.Combine(sharedSetupDir, Path.GetFileName(installerExe));

        string ps = $"$ws = New-Object -ComObject WScript.Shell; " +
                    $"$s = $ws.CreateShortcut('{shortcutPath}'); " +
                    $"$s.TargetPath = '{sharedExe}'; " +
                    $"$s.WorkingDirectory = '{sharedSetupDir}'; " +
                    $"$s.Description = 'Continue CTBL++ installation'; " +
                    $"$s.Save()";
        RunCmd("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            string dirName = new DirectoryInfo(dir).Name;
            // Skip WebView2 browser cache — locked while running, recreated automatically
            if (dirName.Contains("WebView2") || dirName == "EBWebView")
                continue;
            CopyDirectory(dir, Path.Combine(destDir, dirName));
        }
    }

    /// <summary>Creates UI shortcuts on the Public Desktop and Start Menu for all users.</summary>
    public void CreateUiShortcuts()
    {
        string uiExe = Path.Combine(TargetDir, "CtblPlusPlus.exe");

        string desktopLnk = @"C:\Users\Public\Desktop\CTBL++.lnk";
        string startMenuLnk = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\CTBL++.lnk";

        foreach (string lnkPath in new[] { desktopLnk, startMenuLnk })
        {
            string ps = $"$ws = New-Object -ComObject WScript.Shell; " +
                        $"$s = $ws.CreateShortcut('{lnkPath}'); " +
                        $"$s.TargetPath = '{uiExe}'; " +
                        $"$s.WorkingDirectory = '{TargetDir}'; " +
                        $"$s.Description = 'CTBL++ Control Panel'; " +
                        $"$s.Save()";
            RunCmd("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
        }
    }

    /// <summary>Removes the setup shortcut from the Public Desktop.</summary>
    public void RemoveSetupShortcut()
    {
        string shortcutPath = @"C:\Users\Public\Desktop\Continue CTBL++ Setup.lnk";
        if (File.Exists(shortcutPath))
            File.Delete(shortcutPath);
    }
}

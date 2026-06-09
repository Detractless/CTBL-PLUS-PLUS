using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CtblPlusPlus.Core.Security;

/// <summary>
/// Provides tamper-recovery by sealing and restoring executable payloads
/// from a DPAPI-protected vault stored in ProgramData.
/// </summary>
public static class VaultRecoveryService
{
    private static readonly string VaultDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CtblPlusPlus", "vault");

    private static readonly string VaultFile = Path.Combine(VaultDir, "triad.vault");
    private static readonly string ManifestVaultFile = Path.Combine(VaultDir, "manifest.vault");

    /// <summary>
    /// Generates a hash manifest for the install directory, encrypts it with DPAPI,
    /// and writes it to the manifest vault.
    /// </summary>
    public static void SealManifest(string installDir)
    {
        if (!Directory.Exists(installDir)) return;

        var manifest = InstallManifest.GenerateManifest(installDir);
        byte[] rawBytes = InstallManifest.SerializeManifest(manifest);
        byte[] sealedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.LocalMachine);
        
        if (!Directory.Exists(VaultDir))
            Directory.CreateDirectory(VaultDir);

        File.WriteAllBytes(ManifestVaultFile, sealedBytes);
    }

    /// <summary>
    /// Loads and decrypts the hash manifest from the secure vault.
    /// </summary>
    public static Dictionary<string, string>? LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestVaultFile)) return null;

            byte[] sealedBytes = File.ReadAllBytes(ManifestVaultFile);
            byte[] rawBytes = ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.LocalMachine);
            return InstallManifest.DeserializeManifest(rawBytes);
        }
        catch { return null; }
    }

    /// <summary>
    /// Encrypts the raw Payload.zip with DPAPI (LocalMachine scope) and writes it to the vault directory.
    /// Called by the Installer during setup.
    /// </summary>
    /// <param name="payloadZipPath">Absolute path to the unencrypted Payload.zip on disk.</param>
    public static void SealVault(string payloadZipPath)
    {
        if (!File.Exists(payloadZipPath))
            throw new FileNotFoundException("Payload ZIP not found for vault sealing.", payloadZipPath);

        if (!Directory.Exists(VaultDir))
            Directory.CreateDirectory(VaultDir);

        byte[] rawBytes = File.ReadAllBytes(payloadZipPath);
        byte[] sealedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(VaultFile, sealedBytes);
    }

    /// <summary>
    /// Restores a specific subfolder (e.g. "CtblPlusPlus") from the sealed vault
    /// into a specific destination directory on disk.
    /// </summary>
    /// <param name="targetSubfolder">The subfolder name inside the ZIP to restore (e.g. "CtblPlusPlus").</param>
    /// <param name="destinationDir">The directory where files should be extracted.</param>
    /// <returns>True if restoration succeeded, false otherwise.</returns>
    public static bool RestoreTarget(string targetSubfolder, string destinationDir)
    {
        try
        {
            if (!File.Exists(VaultFile))
            {
                Log($"Vault file not found at {VaultFile}. Cannot restore {targetSubfolder}.");
                return false;
            }

            // 1. Read and decrypt the vault
            byte[] sealedBytes = File.ReadAllBytes(VaultFile);
            byte[] rawZipBytes = ProtectedData.Unprotect(sealedBytes, null, DataProtectionScope.LocalMachine);

            // 2. Open ZIP in memory and extract only the target subfolder
            using var zipStream = new MemoryStream(rawZipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            string targetDir = destinationDir;

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            int filesRestored = 0;
            // Phase 06 fix: empty targetSubfolder means restore root-level entries (flat layout)
            bool rootRestore = string.IsNullOrEmpty(targetSubfolder);
            string prefix = rootRestore ? "" : targetSubfolder.TrimEnd('/') + "/";

            foreach (var entry in archive.Entries)
            {
                // Skip directory entries
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                // Match entries: root-level (no subfolder) or prefixed
                bool isMatch = rootRestore
                    ? !entry.FullName.Contains('/')   // root-level entries only
                    : entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

                if (!isMatch)
                    continue;

                string relativePath = rootRestore
                    ? entry.FullName
                    : entry.FullName.Substring(prefix.Length);
                string destPath = Path.Combine(targetDir, relativePath);

                // Guard against path traversal: e.g. entries containing ../
                string canonicalDest   = Path.GetFullPath(destPath);
                string canonicalTarget = Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar;
                if (!canonicalDest.StartsWith(canonicalTarget, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Blocked path traversal attempt for entry '{entry.FullName}'.");
                    continue;
                }

                // Ensure subdirectories exist
                string? destDir = Path.GetDirectoryName(destPath);
                if (destDir != null && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                entry.ExtractToFile(destPath, overwrite: true);
                filesRestored++;
            }

            if (filesRestored == 0)
            {
                Log($"No files found in vault for subfolder '{targetSubfolder}'. Vault may be corrupt or mismatched.");
                return false;
            }

            Log($"Successfully restored {filesRestored} file(s) for '{targetSubfolder}' to '{destinationDir}' from Secure Vault.");
            return true;
        }
        catch (CryptographicException ex)
        {
            Log($"DPAPI decryption failed for vault: {ex.Message}. Vault may have been created on a different machine.");
            return false;
        }
        catch (InvalidDataException ex)
        {
            Log($"Vault ZIP data is corrupt: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Vault restoration failed for '{targetSubfolder}': {ex.Message}");
            return false;
        }
    }

    private static void Log(string message)
    {
        try
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "CtblPlusPlus");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string logPath = Path.Combine(logDir, "process_log.txt");
            File.AppendAllText(logPath, DateTime.Now.ToString("O") + ": [VaultRecoveryService] " + message + Environment.NewLine);
        }
        catch { }
    }
}



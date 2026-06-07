using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CtblPlusPlus.Core.Security;

/// <summary>
/// Extracts identity fingerprint information from executable files.
/// All methods are static and safe to call from any context.
/// </summary>
public static class AppIdentityResolver
{
    /// <summary>
    /// Extracts the product name from PE VersionInfo. Falls back to the filename without extension.
    /// </summary>
    public static string GetAppName(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return Path.GetFileNameWithoutExtension(exePath);
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return !string.IsNullOrWhiteSpace(info.ProductName) 
                ? info.ProductName 
                : Path.GetFileNameWithoutExtension(exePath);
        }
        catch
        {
            return Path.GetFileNameWithoutExtension(exePath);
        }
    }

    /// <summary>
    /// Extracts the company name from PE VersionInfo.
    /// </summary>
    public static string GetPublisher(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return string.Empty;
            var info = FileVersionInfo.GetVersionInfo(exePath);
            return info.CompanyName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Computes the SHA-256 hash of the file. This is an audit-only field — NOT used for reconciliation.
    /// </summary>
    public static string GetFileHashSha256(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return string.Empty;
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Extracts the full identity fingerprint for an executable.
    /// </summary>
    public static (string AppName, string Publisher, string FileHashSha256) GetIdentity(string exePath)
    {
        return (
            GetAppName(exePath),
            GetPublisher(exePath),
            GetFileHashSha256(exePath)
        );
    }
}



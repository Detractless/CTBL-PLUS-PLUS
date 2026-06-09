using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace CtblPlusPlus.Core.Security;

/// <summary>
/// Handles the generation, serialization, and deserialization of the installation hash manifest.
/// </summary>
public static class InstallManifest
{
    public static Dictionary<string, string> GenerateManifest(string installDir)
    {
        var manifest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(installDir)) return manifest;

        // walk the directory, compute SHA256 for each file
        foreach (var file in Directory.EnumerateFiles(installDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            string hash = AppIdentityResolver.GetFileHashSha256(file);
            string relativePath = Path.GetFileName(file);
            manifest[relativePath] = hash;
        }

        return manifest;
    }

    public static byte[] SerializeManifest(Dictionary<string, string> manifest)
    {
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    public static Dictionary<string, string>? DeserializeManifest(byte[] data)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(data);
        }
        catch
        {
            return null;
        }
    }
}

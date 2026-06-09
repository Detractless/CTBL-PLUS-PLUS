using System;
using System.IO;

namespace CtblPlusPlus.Core.AppSystem;

/// <summary>
/// Shared logging utility for Engine services.
/// Replaces the 8+ private Log() methods scattered across individual services.
/// </summary>
public static class EngineLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CtblPlusPlus", "process_log.txt");

    public static void Log(string source, string message)
    {
        try
        {
            string dir = Path.GetDirectoryName(LogPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(LogPath, $"{DateTime.Now:O}: [{source}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}



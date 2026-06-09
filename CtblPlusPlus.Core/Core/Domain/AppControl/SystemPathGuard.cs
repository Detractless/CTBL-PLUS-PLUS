using System;
using System.Collections.Generic;
using System.IO;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.AppControl;

/// <summary>
/// Centralized system path protection logic. Determines whether a given file path
/// belongs to a system-critical or protected application that should never be
/// blocked, scanned, or inserted into the App Registry.
/// </summary>
public static class SystemPathGuard
{
    /// <summary>
    /// Returns true if the path belongs to a system-critical or protected location.
    /// This is the single source of truth for path exclusions across all scanners.
    /// </summary>
    public static bool IsProtected(string path)
    {
        if (string.IsNullOrEmpty(path)) return true;

        string windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        // 1. Never touch C:\Windows or any subfolder
        if (path.StartsWith(windowsPath, StringComparison.OrdinalIgnoreCase)) return true;

        // 2. Never touch Windows Defender (both locations)
        if (path.StartsWith(Path.Combine(programFiles, "Windows Defender"), StringComparison.OrdinalIgnoreCase)) return true;
        if (path.StartsWith(Path.Combine(programData, "Microsoft", "Windows Defender"), StringComparison.OrdinalIgnoreCase)) return true;

        // 3. Never touch Microsoft Security Client
        if (path.StartsWith(Path.Combine(programFiles, "Microsoft Security Client"), StringComparison.OrdinalIgnoreCase)) return true;

        // 4. Never touch Cold Turkey (our host application)
        if (path.StartsWith(Path.Combine(programFiles, "Cold Turkey"), StringComparison.OrdinalIgnoreCase)) return true;

        // 5. Never touch Office ClickToRun services
        if (path.StartsWith(Path.Combine(programFiles, "Common Files", "Microsoft Shared", "ClickToRun"), StringComparison.OrdinalIgnoreCase)) return true;

        // 6. Never touch Microsoft OneDrive
        if (path.StartsWith(Path.Combine(programFiles, "Microsoft OneDrive"), StringComparison.OrdinalIgnoreCase)) return true;

        // 7. Never touch .NET SDK (compiler services)
        if (path.StartsWith(Path.Combine(programFiles, "dotnet"), StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// Returns true if the path is one of CTBL++'s own executables (self-preservation).
    /// </summary>
    public static bool IsSelfProcess(string path, HashSet<string> selfPaths)
    {
        return selfPaths.Contains(path);
    }
}



using System;
using System.IO;
using System.Threading.Tasks;

namespace CtblPlusPlus.Core.Diagnostics;

/// <summary>
/// Minimal append-only startup/diagnostic logger.
///
/// Why this exists: the UI executable had no global exception handling, so any
/// failure during startup (missing WebView2 runtime, a repository constructor
/// throwing, etc.) terminated the process with no window and no trace. This logger,
/// together with <see cref="InstallGlobalHandlers"/>, turns every such silent death
/// into a readable line on disk.
///
/// It writes to %ProgramData%\CtblPlusPlus\process_log.txt (the same file Program.cs
/// already uses for rejected-argument auditing), falling back to %LOCALAPPDATA% and
/// then the temp folder if a location isn't writable. Every write flushes, so a crash
/// immediately afterwards cannot lose the buffered line. All failures inside the logger
/// are swallowed — diagnostics must never be able to take the app down.
/// </summary>
public static class StartupLog
{
    private static readonly object _gate = new();
    private static string? _resolvedPath;

    // Resolve (once) the first writable log location. Caller must hold _gate.
    private static string ResolvePath()
    {
        if (_resolvedPath != null) return _resolvedPath;

        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CtblPlusPlus", "process_log.txt")
        };

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(candidate)!);
                File.AppendAllText(candidate, string.Empty); // probe writability (creates if absent)
                _resolvedPath = candidate;
                return _resolvedPath;
            }
            catch
            {
                // try the next location
            }
        }

        _resolvedPath = Path.Combine(Path.GetTempPath(), "CtblPlusPlus_process_log.txt");
        return _resolvedPath;
    }

    /// <summary>Appends a single timestamped breadcrumb line.</summary>
    public static void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(
                    ResolvePath(),
                    $"{DateTime.Now:O} [Startup] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // never let logging throw
        }
    }

    /// <summary>Appends an exception with its full type, message, and stack trace.</summary>
    public static void Exception(string context, Exception ex)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(
                    ResolvePath(),
                    $"{DateTime.Now:O} [Startup] EXCEPTION in {context}: {ex.GetType().FullName}: {ex.Message}" +
                    $"{Environment.NewLine}{ex}{Environment.NewLine}");
            }
        }
        catch
        {
            // never let logging throw
        }
    }

    /// <summary>
    /// Hooks the process-wide last-chance exception events. Call this once, as the very
    /// first statement in Main, before any other work can fault. Handlers only log; they
    /// do not mark anything handled, so the process still terminates exactly as it did
    /// before — the only change is that the cause is now recorded.
    /// </summary>
    public static void InstallGlobalHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Exception("AppDomain.UnhandledException", ex);
            else
                Write($"AppDomain.UnhandledException (non-Exception object): {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Exception("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }
}

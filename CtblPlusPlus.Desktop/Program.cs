using System.ServiceProcess;
using CtblPlusPlus.Core.Diagnostics;

namespace CtblPlusPlus.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Diagnostics: install last-chance exception handlers before anything can fault,
        // so a startup failure is logged to disk instead of vanishing silently.
        StartupLog.InstallGlobalHandlers();
        StartupLog.Write($"=== Main entered (args: [{string.Join(", ", args)}]) ===");

        // Production lockdown: strip --developer if the application is formally installed.
        // Gate 2 in UiBootstrapper also enforces this independently, but stripping here
        // prevents the flag from reaching the UI layer at all and keeps the audit log clean.
        bool isInstalled = IsProductionInstall();
        string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        bool isOfficialPath = exePath != null
            && exePath.StartsWith(@"C:\Program Files\CTBL Queue Delay",
                                  StringComparison.OrdinalIgnoreCase);
        StartupLog.Write($"exePath='{exePath}', isInstalled={isInstalled}, isOfficialPath={isOfficialPath}");

        if (isInstalled || isOfficialPath)
        {
            args = SanitizeArguments(args, isInstalled);
        }

        // Single destination: the WPF dashboard.
        // Engine and watchdog are now separate executables — no routing needed here.
        StartupLog.Write("Handing off to UiBootstrapper.LaunchWpfApp...");
        CtblPlusPlus.Bootstrappers.UiBootstrapper.LaunchWpfApp(args);
    }

    private static string[] SanitizeArguments(string[] args, bool isInstalled)
    {
        // When installed, no arguments are permitted (developer mode is blocked).
        // When not installed (dev / portable), --developer is the only recognised flag.
        var allowed = isInstalled
            ? System.Array.Empty<string>()
            : new[] { "--developer" };

        var sanitized = new System.Collections.Generic.List<string>();
        foreach (var arg in args)
        {
            if (allowed.Contains(arg, StringComparer.OrdinalIgnoreCase))
            {
                sanitized.Add(arg);
            }
            else
            {
                // Audit log: record every rejected argument for tamper traceability
                try
                {
                    string logDir = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(
                            System.Environment.SpecialFolder.CommonApplicationData),
                        "CtblPlusPlus");
                    if (!System.IO.Directory.Exists(logDir))
                        System.IO.Directory.CreateDirectory(logDir);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(logDir, "process_log.txt"),
                        $"{System.DateTime.Now:O}: [Program] REJECTED argument: " +
                        $"'{arg}' (installed={isInstalled}){System.Environment.NewLine}");
                }
                catch { }
            }
        }
        return sanitized.ToArray();
    }

    private static bool IsProductionInstall()
    {
        try
        {
            using var sc = new ServiceController("CTBL Queue Delay Engine");
            var _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch { return true; } // Fail-armed: any unexpected error keeps protections active
    }
}

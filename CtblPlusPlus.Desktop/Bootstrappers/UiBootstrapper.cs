using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Persistence.Repositories;
using CtblPlusPlus.Core.Diagnostics;
using CtblPlusPlus.DesktopHost;

namespace CtblPlusPlus.Bootstrappers;

public static class UiBootstrapper
{
    private static Mutex? _uiMutex;
    private static EventWaitHandle? _showEvent;

    public static bool IsDeveloperMode { get; private set; }

    public static void LaunchWpfApp(string[] args)
    {
        IsDeveloperMode = System.Linq.Enumerable.Contains(args, "--developer", StringComparer.OrdinalIgnoreCase);

        // Production override: if the app is formally installed, dev-mode is unconditionally disabled.
        // Even if --developer survives Gate 1 (Program.cs), Gate 2 forces it off here.
        if (IsDeveloperMode && IsServiceInstalled())
        {
            IsDeveloperMode = false;
        }
        
        bool createdNew;
        _uiMutex = new Mutex(true, @"Global\CtblPlusPlus_UI_Mutex", out createdNew);
        StartupLog.Write($"Single-instance mutex acquired: createdNew={createdNew}");

        if (!createdNew)
        {
            // App is already running. Signal the existing instance to show its window.
            StartupLog.Write("Another instance already owns the UI mutex; signaling it to show and exiting (this process shows no window).");
            try
            {
                using var evt = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\CtblPlusPlus_UI_ShowEvent");
                evt.Set();
            }
            catch { }
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\CtblPlusPlus_UI_ShowEvent");

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        StartupLog.Write("Ensuring engine is running...");
        EnsureEngineRunning();

        // Manual DI for UI layer. Each construction is logged so a throw here names the culprit.
        StartupLog.Write("Constructing DatabaseClient...");
        var dbClient = new DatabaseClient();

        StartupLog.Write("Constructing DpapiHmacProvider...");
        var hmacProvider = new DpapiHmacProvider();
        StartupLog.Write("Constructing SqliteQueueRepository...");
        var queueRepo = new SqliteQueueRepository(hmacProvider);
        StartupLog.Write("Constructing SqliteSettingsRepository...");
        var settingsRepo = new SqliteSettingsRepository(hmacProvider);
        StartupLog.Write("Constructing SqliteAuditRepository...");
        var auditRepo = new SqliteAuditRepository();
        StartupLog.Write("Constructing SqliteAppControlRepository...");
        var appControlRepo = new SqliteAppControlRepository();

        StartupLog.Write("Manual DI complete; entering RunSystemTray...");
        RunSystemTray(dbClient, queueRepo, settingsRepo, auditRepo, appControlRepo, hmacProvider);

        _uiMutex?.ReleaseMutex();
        _uiMutex?.Dispose();
        _showEvent?.Dispose();
    }

    private static void EnsureEngineRunning()
    {
        // Check if the Engine is already running via its global mutex.
        bool engineRunning;
        try
        {
            engineRunning = Mutex.TryOpenExisting(@"Global\CtblPlusPlus.Core_Mutex_v2", out var existing);
            existing?.Dispose();
        }
        catch
        {
            // Cannot determine state — assume running to avoid double-launch.
            return;
        }

        if (engineRunning) return;

        // In split-EXE architecture the engine is a dedicated sibling binary.
        // Derive its path from the install directory rather than re-launching
        // this process with an argument.
        string? currentExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (currentExe == null) return;

        string installDir = Path.GetDirectoryName(currentExe) ?? string.Empty;
        string engineExe  = Path.Combine(installDir, "CtblPlusPlus.Engine.exe");

        // If the engine EXE is absent we are in dev/portable mode; the developer
        // is responsible for starting the engine project separately.
        if (!File.Exists(engineExe)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = engineExe,  // Dedicated engine EXE — no arguments needed
                UseShellExecute = true,
                Verb            = "runas",     // Request admin elevation
                CreateNoWindow  = true
            };
            Process.Start(psi);
        }
        catch
        {
            // Best-effort — the IPC client will keep retrying until the engine comes up.
        }
    }

    private static void RunSystemTray(
        DatabaseClient dbClient, 
        IQueueRepository queueRepo, 
        ISettingsRepository settingsRepo, 
        IAuditRepository auditRepo, 
        IAppControlRepository appControlRepo,
        IHmacProvider hmacProvider)
    {
        var wpfApp = new System.Windows.Application();
        wpfApp.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        // Log UI-thread exceptions. We do NOT mark them handled, so termination behavior is
        // unchanged; the failure is simply recorded on its way out.
        wpfApp.DispatcherUnhandledException += (_, ex) =>
            StartupLog.Exception("Application.DispatcherUnhandledException", ex.Exception);

        using var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "CTBL++"
        };
        
        WebViewHostWindow? dashboardWindow = null;

        void ShowDashboard()
        {
            wpfApp.Dispatcher.Invoke(() =>
            {
                if (dashboardWindow == null || !dashboardWindow.IsLoaded) {
                    dashboardWindow = new WebViewHostWindow(
                        dbClient, 
                        queueRepo, 
                        settingsRepo, 
                        auditRepo, 
                        appControlRepo, 
                        hmacProvider, 
                        icon);
                    dashboardWindow.Show();
                } else {
                    if (dashboardWindow.WindowState == System.Windows.WindowState.Minimized)
                        dashboardWindow.WindowState = System.Windows.WindowState.Normal;
                    dashboardWindow.Activate();
                    dashboardWindow.Focus();
                }
            });
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (_showEvent != null)
            {
                try
                {
                    if (_showEvent.WaitOne())
                    {
                        ShowDashboard();
                    }
                }
                catch { break; }
            }
        });

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        
        var openDashboardItem = new System.Windows.Forms.ToolStripMenuItem("Open Dashboard");
        openDashboardItem.Click += (s, e) => ShowDashboard();
        contextMenu.Items.Add(openDashboardItem);

        var quitItem = new System.Windows.Forms.ToolStripMenuItem("Quit");
        quitItem.Click += (s, e) =>
        {
            icon.Visible = false;
            wpfApp.Shutdown();
            Environment.Exit(0);
        };
        contextMenu.Items.Add(quitItem);

        icon.ContextMenuStrip = contextMenu;

        StartupLog.Write("Constructing initial WebViewHostWindow...");
        dashboardWindow = new WebViewHostWindow(
            dbClient, 
            queueRepo, 
            settingsRepo, 
            auditRepo, 
            appControlRepo, 
            hmacProvider, 
            icon);
        StartupLog.Write("Showing dashboard window and entering the WPF message loop (wpfApp.Run).");
        dashboardWindow.Show();

        wpfApp.Run();
    }

    private static bool IsServiceInstalled()
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController("CTBL Queue Delay Engine");
            var _ = sc.Status;
            return true;
        }
        catch (InvalidOperationException) { return false; }
        catch { return true; } // Fail-armed
    }
}



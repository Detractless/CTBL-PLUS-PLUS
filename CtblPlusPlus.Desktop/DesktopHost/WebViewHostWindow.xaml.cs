using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using CtblPlusPlus.DesktopHost.Ipc;
using CtblPlusPlus.DesktopHost.Services;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Models;
using CtblPlusPlus.Core.Constants;
using CtblPlusPlus.Core.Diagnostics;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;

namespace CtblPlusPlus.DesktopHost;

public partial class WebViewHostWindow : Window
{
    private readonly DatabaseClient _dbClient;
    private readonly IQueueRepository _queueRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IAppControlRepository _appControlRepo;
    private readonly IHmacProvider _hmacProvider;
    
    private readonly EngineNamedPipeClient _EngineNamedPipeClient;
    private readonly IQueueManager _queueManager;
    private IIpcRouter? _ipcRouter;

    public WebViewHostWindow(
        DatabaseClient dbClient, 
        IQueueRepository queueRepo, 
        ISettingsRepository settingsRepo,
        IAuditRepository auditRepo,
        IAppControlRepository appControlRepo,
        IHmacProvider hmacProvider,
        System.Windows.Forms.NotifyIcon trayIcon)
    {
        InitializeComponent();
        _dbClient = dbClient;
        _queueRepo = queueRepo;
        _settingsRepo = settingsRepo;
        _auditRepo = auditRepo;
        _appControlRepo = appControlRepo;
        _hmacProvider = hmacProvider;

        _queueManager = new DashboardQueueManager(
            _queueRepo, _settingsRepo, _auditRepo, _appControlRepo, _hmacProvider,
            () => OnEngineStateChanged("all"));

        _EngineNamedPipeClient = new EngineNamedPipeClient(s => { }, OnEngineStateChanged, trayIcon, _hmacProvider);
        // We no longer start the connection loop for security status; it is now polled via DB.
        
        InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _EngineNamedPipeClient.Stop();
        base.OnClosed(e);
    }


    private void OnEngineStateChanged(string scope)
    {
        Dispatcher.Invoke(() =>
        {
            if (webView?.CoreWebView2 == null) return;
            
            // Re-trigger data sends through the router
            _ipcRouter?.HandleMessage(JsonSerializer.Serialize(new { command = IpcCommands.GetQueue }));
            _ipcRouter?.HandleMessage(JsonSerializer.Serialize(new { command = IpcCommands.GetAuditLog }));

            if (scope == "appControl" || scope == "appRegistry" || scope == "all")
            {
                _ipcRouter?.HandleMessage(JsonSerializer.Serialize(new { command = IpcCommands.GetAppRegistry }));
                _ipcRouter?.HandleMessage(JsonSerializer.Serialize(new { command = IpcCommands.GetAppControlState }));
            }
        });
    }

    private async void InitializeAsync()
    {
        try
        {
            StartupLog.Write("WebView2: creating environment...");
            var envOptions = new CoreWebView2EnvironmentOptions();
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "CtblWebView2"), envOptions);

            StartupLog.Write("WebView2: ensuring CoreWebView2 (requires the Evergreen WebView2 Runtime)...");
            await webView.EnsureCoreWebView2Async(env);
            webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

            string wwwrootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot");
            StartupLog.Write($"WebView2: mapping ctbl.app -> {wwwrootPath} (exists={Directory.Exists(wwwrootPath)})");

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("ctbl.app", wwwrootPath, CoreWebView2HostResourceAccessKind.Allow);

            // Initialize Router
            _ipcRouter = new WebViewIpcRouter(
                webView.CoreWebView2, _queueManager, _queueRepo, _settingsRepo, _auditRepo, _appControlRepo, _dbClient, HandleBrowseForApp, HandleSaveTemplateFile);

            webView.CoreWebView2.WebMessageReceived += (s, e) => _ipcRouter.HandleMessage(e.TryGetWebMessageAsString());

            // Inject IPC Constants
            var commands = typeof(IpcCommands).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .ToDictionary(f => f.Name, f => f.GetValue(null));
            string commandsJson = JsonSerializer.Serialize(commands);
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($"window.AppCommands = {commandsJson};");

            StartupLog.Write("WebView2: navigating to https://ctbl.app/index.html");
            webView.CoreWebView2.Navigate("https://ctbl.app/index.html");
            StartupLog.Write("WebView2: initialization complete.");
        }
        catch (Exception ex)
        {
            // This is an async void: an unhandled throw here tears the process down with no
            // trace. Log it and leave the (blank) window up so the failure is visible and recorded
            // rather than silent. A missing WebView2 Runtime is the most common cause.
            StartupLog.Exception("WebViewHostWindow.InitializeAsync", ex);
        }
    }

    private void HandleBrowseForApp()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application",
            Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            string selectedPath = dialog.FileName;
            var (appName, publisher, _) = Core.Security.AppIdentityResolver.GetIdentity(selectedPath);
            var response = new { 
                command = IpcCommands.AppSelected, 
                data = new { path = selectedPath, appName, publisher, isSigned = false } 
            };
            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(response));
        }
    }

    private void HandleSaveTemplateFile(string filename, string content)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Cold Turkey Block List",
            Filter = "Cold Turkey Block List (*.ctbbl)|*.ctbbl|All Files (*.*)|*.*",
            FileName = filename,
            DefaultExt = ".ctbbl"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.WriteAllText(dialog.FileName, content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save file: {ex.Message}");
            }
        }
    }
}



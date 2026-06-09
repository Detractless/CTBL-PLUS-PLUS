using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace CtblPlusPlus.Installer;

public partial class MainWindow : Window
{
    private readonly InstallationOrchestrator _orchestrator = new();
    private bool _isPass2;

    public MainWindow()
    {
        InitializeComponent();
        InitializeAsync();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    async void InitializeAsync()
    {
        await webView.EnsureCoreWebView2Async(null);
        webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.AreDevToolsEnabled = false;

        // Detect which pass to run
        _isPass2 = _orchestrator.IsRunningAsCtblAccount();

        // Navigate with pass mode in URL (eliminates race condition with post-message)
        string localHtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "index.html");
        string fileUri = new Uri(localHtmlPath).AbsoluteUri + "?pass=" + (_isPass2 ? "2" : "1");
        webView.CoreWebView2.Navigate(fileUri);

        await Task.Delay(1500);

        // Cold Turkey check (only relevant in Pass 2)
        if (_isPass2)
        {
            bool isInstalled = _orchestrator.IsColdTurkeyInstalled();
            PostWebMessage("CT_STATUS|" + (isInstalled ? "INSTALLED" : "MISSING"));
        }
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message = e.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(message)) return;

        if (message == "BEGIN_PASS1")                await RunPass1Async();
        else if (message == "BEGIN_INSTALL")          await RunPass2InstallAsync();
        else if (message == "CLOSE_INSTALLER")        Application.Current.Shutdown();
        else if (message == "LAUNCH_CT_INSTALLER")    _orchestrator.LaunchColdTurkeyInstaller();
        else if (message == "GET_ADMINS")             await SendAdministratorsAsync();
        else if (message.StartsWith("COPY_CLIP|"))    CopyToClipboard(message.Substring("COPY_CLIP|".Length));
    }

    // Replies with the current Administrators group members for display in the
    // sealing tutorial. Read-only — the user performs demotion manually.
    private async Task SendAdministratorsAsync()
    {
        var admins = await Task.Run(_orchestrator.GetAdministrators);
        PostWebMessage("ADMINS|" + string.Join(",", admins));
    }

    // Clipboard write for the generated CTBLAdmin password. Runs on the UI/STA
    // thread (required by the WPF clipboard) and is resilient to transient locks.
    private void CopyToClipboard(string value)
    {
        Dispatcher.Invoke(() =>
        {
            try { Clipboard.SetText(value); }
            catch { /* clipboard may be transiently locked by another process; ignore */ }
        });
    }

    private async Task RunPass1Async()
    {
        try
        {
            SendProgress(10, "Checking for existing CTBLAdmin account...");
            bool exists = await Task.Run(_orchestrator.CtblAccountExists);

            if (!exists)
            {
                SendProgress(40, "Creating CTBLAdmin admin account...");
                await Task.Run(_orchestrator.CreateCtblAccount);
            }
            else
            {
                SendProgress(40, "CTBLAdmin account already exists.");
            }

            SendProgress(70, "Creating setup shortcut...");
            await Task.Run(_orchestrator.CreateSetupShortcut);

            SendProgress(100, "Account ready! Log into CTBLAdmin to continue.");
            PostWebMessage("PASS1_COMPLETE");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Account setup failed: {ex.Message}", "CTBL++ Setup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RunPass2InstallAsync()
    {
        // Prevent aborting mid-hardening — closing during the sequence could leave a
        // half-sealed system. Re-enabled on completion or failure.
        Dispatcher.Invoke(() => CloseBtn.IsEnabled = false);
        try
        {
            SendProgress(3, "Stopping existing services...");
            await Task.Run(_orchestrator.StopAndDeleteServices);

            SendProgress(8, "Terminating residual processes...");
            await Task.Run(_orchestrator.KillProcesses);

            SendProgress(12, "Stripping protection ACLs...");
            await Task.Run(_orchestrator.StripInstallDirAcls);

            SendProgress(16, "Removing previous installation...");
            await Task.Run(_orchestrator.DeleteInstallDirectory);

            SendProgress(22, "Resetting vault directory ACLs...");
            await Task.Run(_orchestrator.StripVaultAcls);

            SendProgress(25, "Extracting Watchdog Triad bundle...");
            await Task.Run(_orchestrator.ExtractPayload);

            SendProgress(40, "Registering Windows Services...");
            await Task.Run(_orchestrator.RegisterServices);

            SendProgress(45, "Generating system key...");
            await Task.Run(_orchestrator.GenerateSystemKey);

            SendProgress(48, "Sealing Secure Vault...");
            await Task.Run(_orchestrator.SealSecureVault);

            SendProgress(55, "Hardening vault directory...");
            await Task.Run(_orchestrator.HardenVaultDirectory);

            SendProgress(65, "Starting CTBL Queue Delay Engine...");
            await Task.Run(_orchestrator.StartServices);

            SendProgress(76, "Removing setup shortcut...");
            await Task.Run(_orchestrator.RemoveSetupShortcut);

            SendProgress(100, "Installation Complete!");
            Dispatcher.Invoke(() => CloseBtn.IsEnabled = true);
            PostWebMessage("PASS2_COMPLETE");
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => CloseBtn.IsEnabled = true);
            MessageBox.Show(
                $"Installation failed: {ex.Message}\n\nYour admin access has NOT been changed. You can retry.",
                "CTBL++ Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
        }
    }

    private void SendProgress(int percentage, string statusMessage)
    {
        Dispatcher.Invoke(() => PostWebMessage($"PROGRESS|{percentage}|{statusMessage}"));
    }

    private void PostWebMessage(string message)
    {
        webView.CoreWebView2.PostWebMessageAsJson($"\"{message}\"");
    }
}

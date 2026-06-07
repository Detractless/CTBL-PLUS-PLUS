using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Constants;
using CtblPlusPlus.Models;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.DesktopHost.Ipc;
using CtblPlusPlus.DesktopHost.Services;

namespace CtblPlusPlus.DesktopHost.Ipc;

public class WebViewIpcRouter : IIpcRouter
{
    private readonly CoreWebView2 _webView;
    private readonly IQueueManager _queueManager;
    private readonly IQueueRepository _queueRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IAppControlRepository _appControlRepo;
    private readonly DatabaseClient _dbClient;
    private readonly Action _onBrowseForApp;
    private readonly Action<string, string> _onSaveTemplate;

    public WebViewIpcRouter(
        CoreWebView2 webView,
        IQueueManager queueManager,
        IQueueRepository queueRepo,
        ISettingsRepository settingsRepo,
        IAuditRepository auditRepo,
        IAppControlRepository appControlRepo,
        DatabaseClient dbClient,
        Action onBrowseForApp,
        Action<string, string> onSaveTemplate)
    {
        _webView = webView;
        _queueManager = queueManager;
        _queueRepo = queueRepo;
        _settingsRepo = settingsRepo;
        _auditRepo = auditRepo;
        _appControlRepo = appControlRepo;
        _dbClient = dbClient;
        _onBrowseForApp = onBrowseForApp;
        _onSaveTemplate = onSaveTemplate;
    }

    public void HandleMessage(string message)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<IpcMessage>(message);
            if (payload == null) return;

            switch (payload.Command)
            {
                case IpcCommands.GetBlocks:
                    SendBlocks();
                    break;
                    
                case IpcCommands.GetQueue:
                    SendQueue();
                    break;
                    
                case IpcCommands.GetAuditLog:
                    SendAuditLog();
                    break;
                    
                case IpcCommands.GetSecurityState:
                    SendSecurityState();
                    break;
                    
                case IpcCommands.GetSettings:
                    SendSettings();
                    break;
                    

                case IpcCommands.GetAppRegistry:
                    SendAppRegistry();
                    break;

                case IpcCommands.RequestSettingChange:
                    _queueManager.HandleSettingChange(payload.TargetUrl ?? "", payload.DelayHours);
                    break;
                    
                case IpcCommands.RequestException:
                    if (!string.IsNullOrEmpty(payload.TargetUrl) && !string.IsNullOrEmpty(payload.BlockName))
                    {
                        _queueManager.QueueAction(payload.BlockName, payload.TargetUrl, "Add Exception Queued");
                    }
                    break;

                case IpcCommands.RequestRemoval:
                    if (!string.IsNullOrEmpty(payload.TargetUrl) && !string.IsNullOrEmpty(payload.BlockName))
                    {
                        _queueManager.QueueAction(payload.BlockName, $"REMOVE|{payload.TargetUrl}", "Remove Exception Queued");
                    }
                    break;

                case IpcCommands.RequestAppControlRule:
                    _queueManager.HandleAppControlRuleRequest(payload.AppPath ?? "");
                    break;

                case IpcCommands.BrowseForApp:
                    _onBrowseForApp?.Invoke();
                    break;

                case IpcCommands.SaveTemplateFile:
                    if (payload.Data.ValueKind != JsonValueKind.Undefined &&
                        payload.Data.TryGetProperty("filename", out var fnProp) &&
                        payload.Data.TryGetProperty("content", out var contentProp))
                    {
                        _onSaveTemplate?.Invoke(fnProp.GetString() ?? "", contentProp.GetString() ?? "");
                    }
                    break;

                case IpcCommands.RevokeAppControlRule:
                    _queueManager.HandleRevokeAppControlRule(payload.Id ?? "");
                    break;

                case IpcCommands.BulkUpdateAppControlRules:
                    if (payload.Data.ValueKind != JsonValueKind.Undefined &&
                        payload.Data.TryGetProperty("paths", out var pathsProp) && 
                        payload.Data.TryGetProperty("status", out var statusProp))
                    {
                        var paths = JsonSerializer.Deserialize<string[]>(pathsProp.GetRawText());
                        var status = statusProp.GetString();
                        if (paths != null && status != null)
                        {
                            _queueManager.HandleBulkAppControlRuleRequest(paths, status);
                        }
                    }
                    break;

                case IpcCommands.ToggleAppControl:
                    _queueManager.HandleToggleAppControl();
                    break;

                case IpcCommands.GetAppControlState:
                    SendAppControlState();
                    break;
                    
                case IpcCommands.CancelRequest:
                    if (!string.IsNullOrEmpty(payload.Id))
                    {
                        _queueRepo.UpdateRequestStatus(payload.Id, "Cancelled");
                        _auditRepo.LogAction("System", "Queue", "Cancelled Request");
                        SendQueue();
                        SendAuditLog();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[IpcRouter] Error handling message: {ex.Message}");
            try { _auditRepo.LogAction("System", "IpcRouter", $"Error: {ex.Message}"); }
            catch { /* Prevent recursive failures */ }
        }
    }

    private void SendSettings()
    {
        string currentDelay = _settingsRepo.GetSetting("GlobalDelayHours", "1.0");
        PostWebMessage(IpcCommands.SettingsData, new { GlobalDelayHours = currentDelay });
    }

    private void SendAuditLog()
    {
        var logs = _auditRepo.GetAuditLogs();
        PostWebMessage(IpcCommands.AuditLogData, logs);
    }

    private void SendBlocks()
    {
        var state = _dbClient.GetDbState();
        PostWebMessage(IpcCommands.BlocksData, state.Blocks!);
    }
    
    private void SendQueue()
    {
        var pendingReqs = _queueRepo.GetPendingRequests();
        PostWebMessage(IpcCommands.QueueData, pendingReqs);
    }


    private void SendAppRegistry()
    {
        var apps = _appControlRepo.GetAllApps().Where(a => System.IO.File.Exists(a.ExePath)).ToList();
        PostWebMessage(IpcCommands.AppRegistryData, apps);
    }

    private void SendAppControlState()
    {
        string enabled = _settingsRepo.GetSetting("AppControlEnabled", "false");
        PostWebMessage(IpcCommands.AppControlState, new { enabled = enabled.Equals("true", StringComparison.OrdinalIgnoreCase) });
    }

    private void SendSecurityState()
    {
        string isSecureStr = _settingsRepo.GetSetting("Security_IsSecure", "true");
        bool isCurrentlySecure = isSecureStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        
        PostWebMessage(IpcCommands.SecurityState, new { isSecure = isCurrentlySecure });
    }

    private void PostWebMessage(string command, object data)
    {
        var response = new { command, data };
        _webView.PostWebMessageAsJson(JsonSerializer.Serialize(response));
    }

    private class IpcMessage
    {
        [System.Text.Json.Serialization.JsonPropertyName("command")]
        public string Command { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("blockName")]
        public string? BlockName { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("targetUrl")]
        public string? TargetUrl { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("delayHours")]
        public double DelayHours { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("appPath")]
        public string? AppPath { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public JsonElement Data { get; set; }
    }
}



using System;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Models;
using CtblPlusPlus.Bootstrappers;

namespace CtblPlusPlus.DesktopHost.Services;

public class DashboardQueueManager : IQueueManager
{
    private readonly IQueueRepository _queueRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IAuditRepository _auditRepo;
    private readonly IAppControlRepository _appControlRepo;
    private readonly IHmacProvider _hmacProvider;
    private readonly Action _onQueueUpdated;

    public DashboardQueueManager(
        IQueueRepository queueRepo,
        ISettingsRepository settingsRepo,
        IAuditRepository auditRepo,
        IAppControlRepository appControlRepo,
        IHmacProvider hmacProvider,
        Action onQueueUpdated)
    {
        _queueRepo = queueRepo;
        _settingsRepo = settingsRepo;
        _auditRepo = auditRepo;
        _appControlRepo = appControlRepo;
        _hmacProvider = hmacProvider;
        _onQueueUpdated = onQueueUpdated;
    }

    public void QueueAction(string blockName, string targetUrl, string logAction)
    {
        string currentDelayStr = _settingsRepo.GetSetting("GlobalDelayHours", "1.0");
        if (!double.TryParse(currentDelayStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delayHours)) delayHours = 1.0;

        if (UiBootstrapper.IsDeveloperMode) delayHours = 0;

        var request = new DelayRequest
        {
            Id = Guid.NewGuid().ToString(),
            BlockName = blockName,
            TargetUrl = targetUrl,
            RequestedAt = DateTime.UtcNow,
            UnlockAt = DateTime.UtcNow.AddHours(delayHours),
            Status = "Pending"
        };
        _queueRepo.AddRequest(request);
        _auditRepo.LogAction(blockName, targetUrl, logAction);
        _onQueueUpdated?.Invoke();
    }

    public void HandleSettingChange(string targetKey, double newValue)
    {
        if (targetKey == "GlobalDelayHours" && newValue > 0)
        {
            string currentDelayStr = _settingsRepo.GetSetting("GlobalDelayHours", "1.0");
            if (!double.TryParse(currentDelayStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double currentDelayHours)) currentDelayHours = 1.0;

            string FormatTimeStr(double totalHours)
            {
                int h = (int)Math.Floor(totalHours);
                int m = (int)Math.Round((totalHours - h) * 60);
                string res = "";
                if (h > 0) res += $"{h}h ";
                if (m > 0 || h == 0) res += $"{m}m";
                return res.Trim();
            }

            string readableTime = FormatTimeStr(newValue);

            if (newValue >= currentDelayHours)
            {
                _settingsRepo.SetSetting("GlobalDelayHours", newValue.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
                _auditRepo.LogAction("System", "GlobalDelayHours", $"Updated to {readableTime}");
                _onQueueUpdated?.Invoke();
            }
            else
            {
                var request = new DelayRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    BlockName = "System",
                    TargetUrl = $"GlobalDelayHours|{newValue}",
                    RequestedAt = DateTime.UtcNow,
                    UnlockAt = DateTime.UtcNow.AddHours(currentDelayHours),
                    Status = "Pending"
                };

                _queueRepo.AddRequest(request);
                _auditRepo.LogAction("System", "GlobalDelayHours", $"Decrease Queued to {readableTime}");
                _onQueueUpdated?.Invoke();
            }
        }
    }

    public void HandleToggleAppControl()
    {
        string currentStr = _settingsRepo.GetSetting("AppControlEnabled", "false");
        bool isEnabled = currentStr.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (!isEnabled)
        {
            var request = new DelayRequest
            {
                Id = Guid.NewGuid().ToString(),
                BlockName = "AppControl",
                TargetUrl = "APP_ENABLE_CONTROL",
                RequestedAt = DateTime.UtcNow,
                UnlockAt = DateTime.UtcNow,
                Status = "Pending"
            };

            string payloadToSign = request.Id + request.TargetUrl + request.UnlockAt.ToString("o");
            request.Signature = _hmacProvider.ComputeHmac(payloadToSign);

            _queueRepo.AddRequest(request);
            _auditRepo.LogAction("AppControl", "AppControl", "Enable Queued (Instant)");
        }
        else
        {
            string currentDelayStr = _settingsRepo.GetSetting("GlobalDelayHours", "1.0");
            if (!double.TryParse(currentDelayStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delayHours)) delayHours = 1.0;

            double doubleDelay = delayHours * 2;
            if (UiBootstrapper.IsDeveloperMode) doubleDelay = 0;

            var request = new DelayRequest
            {
                Id = Guid.NewGuid().ToString(),
                BlockName = "AppControl",
                TargetUrl = "APP_DISABLE_CONTROL",
                RequestedAt = DateTime.UtcNow,
                UnlockAt = DateTime.UtcNow.AddHours(doubleDelay),
                Status = "Pending"
            };

            _queueRepo.AddRequest(request);

            int h = (int)Math.Floor(doubleDelay);
            int m = (int)Math.Round((doubleDelay - h) * 60);
            string timeStr = h > 0 ? $"{h}h {m}m" : $"{m}m";
            _auditRepo.LogAction("AppControl", "AppControl", $"Disable Queued ({timeStr} delay)");
        }

        _onQueueUpdated?.Invoke();
    }

    public void HandleAppControlRuleRequest(string appPath)
    {
        string targetUrl = $"APP_ALLOW|{appPath}";
        QueueAction("AppControl", targetUrl, "App Access Queued");
    }

    public void HandleRevokeAppControlRule(string ruleId)
    {
        var request = new DelayRequest
        {
            Id = Guid.NewGuid().ToString(),
            BlockName = "AppControl",
            TargetUrl = $"APP_REVOKE|{ruleId}",
            RequestedAt = DateTime.UtcNow,
            UnlockAt = DateTime.UtcNow, 
            Status = "Pending"
        };

        request.Signature = _hmacProvider.ComputeHmac(request.Id + request.TargetUrl + request.UnlockAt.ToString("o"));
        _queueRepo.AddRequest(request);

        _onQueueUpdated?.Invoke();
    }

    public void HandleBulkAppControlUpdate(IEnumerable<string> ids, string targetStatus)
    {
        try
        {
            var idList = ids.ToList();
            if (!idList.Any()) return;

            if (targetStatus == "Allowed")
            {
                _appControlRepo.BulkSetAppStatus(idList, "Allowed");
                _auditRepo.LogAction("AppControl", $"Bulk Allow ({idList.Count})", "Success");
            }
            else if (targetStatus == "Detected")
            {
                _appControlRepo.BulkSetAppStatus(idList, "Detected");
                _auditRepo.LogAction("AppControl", $"Bulk Revoke ({idList.Count})", "Success");
            }

            _onQueueUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _auditRepo.LogAction("AppControl", "BulkUpdate", $"Error: {ex.Message}");
        }
    }

    public void HandleBulkAppControlRuleRequest(IEnumerable<string> paths, string status)
    {
        try
        {
            var requests = new List<DelayRequest>();
            
            string currentDelayStr = _settingsRepo.GetSetting("GlobalDelayHours", "1.0");
            if (!double.TryParse(currentDelayStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delayHours)) delayHours = 1.0;
            if (UiBootstrapper.IsDeveloperMode) delayHours = 0;

            foreach (var path in paths)
            {
                // For 'Allowed', we use the ExePath in targetUrl.
                // For 'Detected' (Revoke), we also use the path but prefix with APP_REVOKE_PATH if we want to be consistent, 
                // but the repo usually handles APP_REVOKE|Id. 
                // Let's stick to APP_ALLOW|path for now as requested for the fix.
                string targetUrl = status == "Allowed" ? $"APP_ALLOW|{path}" : $"APP_REVOKE_PATH|{path}";
                
                double effectiveDelay = status == "Allowed" ? delayHours : 0;

                requests.Add(new DelayRequest
                {
                    Id = Guid.NewGuid().ToString(),
                    BlockName = "AppControl",
                    TargetUrl = targetUrl,
                    RequestedAt = DateTime.UtcNow,
                    UnlockAt = DateTime.UtcNow.AddHours(effectiveDelay),
                    Status = "Pending"
                });
            }

            if (requests.Any())
            {
                _queueRepo.BulkAddRequests(requests);
                _auditRepo.LogAction("AppControl", $"Bulk Request {status} ({requests.Count})", "Success");
                _onQueueUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _auditRepo.LogAction("AppControl", "BulkRequestError", ex.Message);
        }
    }
}



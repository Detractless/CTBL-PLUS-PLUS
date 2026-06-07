using System;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Domain.AppControl;
using CtblPlusPlus.Models;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue.Handlers;

public class AppControlQueueHandlerWrapper : IQueueRequestHandler
{
    private readonly AppControlQueueHandler _innerHandler;
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditRepository _auditRepo;

    public AppControlQueueHandlerWrapper(AppControlQueueHandler innerHandler, IQueueRepository queueRepo, IAuditRepository auditRepo)
    {
        _innerHandler = innerHandler;
        _queueRepo = queueRepo;
        _auditRepo = auditRepo;
    }

    public bool CanHandle(DelayRequest request)
    {
        return request.TargetUrl.StartsWith("APP_ALLOW|") ||
               request.TargetUrl.StartsWith("APP_REVOKE|") ||
               request.TargetUrl.StartsWith("APP_REVOKE_PATH|") ||
               request.TargetUrl == "APP_ENABLE_CONTROL" ||
               request.TargetUrl == "APP_DISABLE_CONTROL";
    }

    public void Handle(DelayRequest request, QueueBatchContext context)
    {
        try
        {
            if (request.TargetUrl.StartsWith("APP_ALLOW|"))
            {
                string appPath = request.TargetUrl.Substring(10);
                _innerHandler.HandleAllow(appPath);
                _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                _auditRepo.LogAction("AppControl", appPath, "Allowed");
            }
            else if (request.TargetUrl.StartsWith("APP_REVOKE|"))
            {
                string ruleId = request.TargetUrl.Substring(11);
                _innerHandler.HandleRevoke(ruleId);
                _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                _auditRepo.LogAction("AppControl", request.TargetUrl, "Access Revoked & Re-Blocked");
            }
            else if (request.TargetUrl.StartsWith("APP_REVOKE_PATH|"))
            {
                string appPath = request.TargetUrl.Substring(16);
                _innerHandler.HandleRevokePath(appPath);
                _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                _auditRepo.LogAction("AppControl", request.TargetUrl, "Access Revoked (Path-based)");
            }
            else if (request.TargetUrl == "APP_ENABLE_CONTROL")
            {
                _innerHandler.HandleEnable();
                _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                _auditRepo.LogAction("AppControl", "AppControl", "App Control Enabled");
            }
            else if (request.TargetUrl == "APP_DISABLE_CONTROL")
            {
                _innerHandler.HandleDisable();
                _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                _auditRepo.LogAction("AppControl", "AppControl", "App Control Disabled");
            }
        }
        catch (Exception ex)
        {
            _queueRepo.UpdateRequestStatus(request.Id, "Failed - Exception");
            _auditRepo.LogAction("AppControl", request.TargetUrl, $"Failed: {ex.Message}");
            context.Log($"[{DateTime.UtcNow:O}] AppControl queue handler exception: {ex}\n");
        }
    }
}



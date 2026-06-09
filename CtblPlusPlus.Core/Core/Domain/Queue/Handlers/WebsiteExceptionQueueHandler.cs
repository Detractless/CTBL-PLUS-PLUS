using System;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Models;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue.Handlers;

public class WebsiteExceptionQueueHandler : IQueueRequestHandler
{
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditRepository _auditRepo;

    public WebsiteExceptionQueueHandler(IQueueRepository queueRepo, IAuditRepository auditRepo)
    {
        _queueRepo = queueRepo;
        _auditRepo = auditRepo;
    }

    public bool CanHandle(DelayRequest request)
    {
        return !request.TargetUrl.StartsWith("APP_ALLOW|") &&
               !request.TargetUrl.StartsWith("APP_REVOKE|") &&
               !request.TargetUrl.StartsWith("APP_REVOKE_PATH|") &&
               request.TargetUrl != "APP_ENABLE_CONTROL" &&
               request.TargetUrl != "APP_DISABLE_CONTROL" &&
               !(request.BlockName == "System" && request.TargetUrl.StartsWith("GlobalDelayHours|"));
    }

    public void Handle(DelayRequest request, QueueBatchContext context)
    {
        CtblRoot currentState;
        try
        {
            currentState = context.GetCtblState();
        }
        catch (Exception ex)
        {
            _queueRepo.UpdateRequestStatus(request.Id, "Failed - CTBL DB Unavailable");
            _auditRepo.LogAction(request.BlockName, request.TargetUrl, "Failed: CTBL Database is missing or locked");
            context.Log($"[{DateTime.UtcNow:O}] Website handler failed to load CTBL DB: {ex.Message}\n");
            return;
        }

        if (currentState == null)
        {
             _queueRepo.UpdateRequestStatus(request.Id, "Failed - CTBL DB Unavailable");
             _auditRepo.LogAction(request.BlockName, request.TargetUrl, "Failed: CTBL Database is missing or locked");
             return;
        }

        if (!currentState.Blocks.TryGetValue(request.BlockName, out var block))
        {
            _queueRepo.UpdateRequestStatus(request.Id, "Failed - Block not found");
            _auditRepo.LogAction(request.BlockName, request.TargetUrl, "Failed");
            return;
        }

        if (request.TargetUrl.StartsWith("REMOVE|"))
        {
            string urlToRemove = request.TargetUrl.Substring(7);
            if (block.Web != null && block.Web.Contains(urlToRemove))
            {
                block.Web.Remove(urlToRemove);
                context.MarkCtblModified();
            }

            _queueRepo.UpdateRequestStatus(request.Id, "Injected");
            _auditRepo.LogAction(request.BlockName, urlToRemove, "Removed Website");
            context.RequestBlockRestart(request.BlockName);
        }
        else
        {
            if (block.Exceptions == null) block.Exceptions = new();

            if (!block.Exceptions.Contains(request.TargetUrl))
            {
                block.Exceptions.Add(request.TargetUrl);
                context.MarkCtblModified();
            }

            _queueRepo.UpdateRequestStatus(request.Id, "Injected");
            _auditRepo.LogAction(request.BlockName, request.TargetUrl, "Injected Exception");
            context.RequestBlockRestart(request.BlockName);
        }
    }
}



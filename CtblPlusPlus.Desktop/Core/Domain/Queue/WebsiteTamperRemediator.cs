using System;
using System.Linq;
using CtblPlusPlus.Core.Interfaces.Data;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue;

public class WebsiteTamperRemediator
{
    private readonly IQueueRepository _queueRepo;
    private readonly IAuditRepository _auditRepo;

    public WebsiteTamperRemediator(IQueueRepository queueRepo, IAuditRepository auditRepo)
    {
        _queueRepo = queueRepo;
        _auditRepo = auditRepo;
    }

    public void Rollback(QueueBatchContext context)
    {
        var injectedRequests = _queueRepo.GetInjectedRequests();
        if (!injectedRequests.Any()) return;

        var now = DateTime.UtcNow;
        bool stateLoaded = false;
        CtblPlusPlus.Models.CtblRoot? state = null;

        foreach (var req in injectedRequests)
        {
            if (req.UnlockAt > now)
            {
                if (!stateLoaded)
                {
                    try
                    {
                        state = context.GetCtblState();
                        stateLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        context.Log($"[{DateTime.UtcNow:O}] WebsiteTamperRemediator failed to load CTBL DB: {ex.Message}\n");
                        return; // Cannot proceed without DB
                    }
                }

                if (state != null && state.Blocks.TryGetValue(req.BlockName, out var block) && block.Exceptions != null)
                {
                    if (block.Exceptions.Contains(req.TargetUrl))
                    {
                        block.Exceptions.Remove(req.TargetUrl);
                        context.MarkCtblModified();
                    }
                }

                _queueRepo.UpdateRequestStatus(req.Id, "Pending");
                _auditRepo.LogAction(req.BlockName, req.TargetUrl, "Reverted (Tamper Protection)");

                context.RequestBlockRestart(req.BlockName);
            }
        }
    }
}



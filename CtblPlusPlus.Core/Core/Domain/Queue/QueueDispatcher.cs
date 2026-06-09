using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.Models;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Domain.AppControl;
using CtblPlusPlus.Core.Domain.Queue.Handlers;
using Microsoft.Extensions.Hosting;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue;

public class QueueDispatcher : BackgroundService
{
    private readonly IQueueRepository _queueRepo;
    private readonly DatabaseClient _DatabaseClient;
    private readonly CtblCliClient _cliService;
    private readonly IEnumerable<IQueueRequestHandler> _handlers;
    private readonly QueueSecurityValidator _securityValidator;
    private readonly WebsiteTamperRemediator _tamperRemediator;
    
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public QueueDispatcher(
        IQueueRepository queueRepo,
        DatabaseClient DatabaseClient,
        CtblCliClient cliService,
        IEnumerable<IQueueRequestHandler> handlers,
        QueueSecurityValidator securityValidator,
        WebsiteTamperRemediator tamperRemediator)
    {
        _queueRepo = queueRepo;
        _DatabaseClient = DatabaseClient;
        _cliService = cliService;
        _handlers = handlers;
        _securityValidator = securityValidator;
        _tamperRemediator = tamperRemediator;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessingLoopAsync(stoppingToken);
    }

    private async Task ProcessingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ProcessQueueAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during initial queue processing: {ex.Message}");
        }

        using var timer = new PeriodicTimer(_pollInterval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await ProcessQueueAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing queue: {ex.Message}");
            }
        }
    }

    private async Task ProcessQueueAsync()
    {
        string dirPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus");
        if (!System.IO.Directory.Exists(dirPath)) System.IO.Directory.CreateDirectory(dirPath);
        string logPath = System.IO.Path.Combine(dirPath, "process_log.txt");

        var context = new QueueBatchContext(logPath, _DatabaseClient, _cliService);

        try
        {
            _tamperRemediator.Rollback(context);

            var pendingRequests = _queueRepo.GetPendingRequests();
            if (!pendingRequests.Any()) return;

            var now = DateTime.UtcNow;
            var dueRequests = pendingRequests.Where(r => r.UnlockAt <= now).ToList();
            if (!dueRequests.Any()) return;

            context.Log($"[{now:O}] Found {dueRequests.Count} due requests.\n");

            foreach (var req in dueRequests)
            {
                context.Log($"[{DateTime.UtcNow:O}] Processing Req '{req.TargetUrl}' | unlockAt={req.UnlockAt:O}\n");

                if (!_securityValidator.VerifyHmac(req, context)) continue;

                var handler = _handlers.FirstOrDefault(h => h.CanHandle(req));
                if (handler != null)
                {
                    handler.Handle(req, context);
                }
                else
                {
                    context.Log($"[{DateTime.UtcNow:O}] No handler found for Req '{req.TargetUrl}'\n");
                }
            }

            // Unified Flush Step
            if (context.CtblStateModified)
            {
                var stateToSave = context.GetCtblState();
                _DatabaseClient.SaveDbState(stateToSave);
                await Task.Delay(500);
            }

            foreach (var kvp in context.BlocksToStart)
            {
                await _cliService.StartBlock(kvp.Key, kvp.Value);
            }

        }
        catch (Exception ex)
        {
            context.Log($"[{DateTime.UtcNow:O}] FATAL EXCEPTION in ProcessQueue: {ex}\n");
        }
    }
}



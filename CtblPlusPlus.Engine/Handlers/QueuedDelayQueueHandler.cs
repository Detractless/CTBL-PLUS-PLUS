using System;
using CtblPlusPlus.Core.Domain.Queue;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Models;

namespace CtblPlusPlus.Engine.Handlers
{
    public class QueuedDelayQueueHandler : IQueueRequestHandler
    {
        private readonly IQueueRepository _queueRepo;

        public QueuedDelayQueueHandler(IQueueRepository queueRepo)
        {
            _queueRepo = queueRepo;
        }

        public bool CanHandle(DelayRequest request)
        {
            return request.TargetUrl == "CTBL_QUEUED_DELAY";
        }

        public void Handle(DelayRequest request, QueueBatchContext context)
        {
            try
            {
                var state = context.GetCtblState();
                
                if (state.Blocks.TryGetValue(request.BlockName, out var block))
                {
                    using (var process = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "C:\\Program Files\\Cold Turkey\\Cold Turkey Blocker.exe",
                            Arguments = $"-stop \"{request.BlockName}\" -password \"CTBL_QUEUED_DELAY\"",
                            UseShellExecute = true,
                            WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized,
                            CreateNoWindow = false
                        }
                    })
                    {
                        process.Start();
                        process.WaitForExit(3000);
                    }

                    context.Log($"[{DateTime.UtcNow:O}] QueuedDelayQueueHandler unlocked block '{request.BlockName}' via CLI\n");
                    _queueRepo.UpdateRequestStatus(request.Id, "Completed");
                }
                else
                {
                    context.Log($"[{DateTime.UtcNow:O}] QueuedDelayQueueHandler failed to find block '{request.BlockName}'\n");
                    _queueRepo.UpdateRequestStatus(request.Id, "Failed - Not Found");
                }
            }
            catch (Exception ex)
            {
                context.Log($"[{DateTime.UtcNow:O}] QueuedDelayQueueHandler exception: {ex}\n");
                _queueRepo.UpdateRequestStatus(request.Id, "Failed - Exception");
            }
        }
    }
}

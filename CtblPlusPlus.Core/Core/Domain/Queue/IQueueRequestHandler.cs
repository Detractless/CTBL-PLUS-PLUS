using CtblPlusPlus.Core.Models;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue;

public interface IQueueRequestHandler
{
    bool CanHandle(DelayRequest request);
    void Handle(DelayRequest request, QueueBatchContext context);
}



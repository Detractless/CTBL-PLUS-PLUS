using System.Collections.Generic;
using CtblPlusPlus.Core.Models;

namespace CtblPlusPlus.Core.Interfaces.Data;

public interface IQueueRepository
{
    void AddRequest(DelayRequest request);
    void BulkAddRequests(IEnumerable<DelayRequest> requests);
    List<DelayRequest> GetPendingRequests();
    List<DelayRequest> GetInjectedRequests();
    void UpdateRequestStatus(string id, string newStatus);
}



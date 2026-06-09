using System.Collections.Generic;
using CtblPlusPlus.Core.Models;

namespace CtblPlusPlus.Core.Interfaces.Data;

public interface IAuditRepository
{
    void LogAction(string blockName, string targetUrl, string action);
    IEnumerable<AuditLogEntry> GetAuditLogs();
}



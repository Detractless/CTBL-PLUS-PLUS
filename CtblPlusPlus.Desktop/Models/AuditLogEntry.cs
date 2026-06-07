using System;

namespace CtblPlusPlus.Models;

public class AuditLogEntry
{
    public int Id { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}



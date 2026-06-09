using System;

namespace CtblPlusPlus.Core.Models;

public class DelayRequest
{
    public string Id { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public DateTime UnlockAt { get; set; }
    public string Status { get; set; } = "Pending";
    public string Signature { get; set; } = string.Empty;
}



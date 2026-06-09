namespace CtblPlusPlus.Core.Models;

public class AppRegistryEntry
{
    public string Id { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Status { get; set; } = "Detected";
    public string FirstSeenUtc { get; set; } = string.Empty;
    public bool IsColdTurkeyInjected { get; set; } = false;
}



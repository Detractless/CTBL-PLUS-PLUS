using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CtblPlusPlus.Models;

public class CtblRoot
{
    [JsonPropertyName("blocks")]
    public Dictionary<string, CtblBlock> Blocks { get; set; } = new();

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new();

    [JsonPropertyName("additional")]
    public CtblAdditional Additional { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class CtblBlock
{
    [JsonPropertyName("enabled")]
    public string Enabled { get; set; } = "false";
    
    [JsonPropertyName("autostart")]
    public string Autostart { get; set; } = "none";
    
    [JsonPropertyName("type")]
    public string Type { get; set; } = "continuous";
    
    [JsonPropertyName("timer")]
    public string Timer { get; set; } = "";
    
    [JsonPropertyName("startTime")]
    public string StartTime { get; set; } = "";
    
    [JsonPropertyName("pomodoroTime")]
    public string PomodoroTime { get; set; } = "";
    
    [JsonPropertyName("lock")]
    public string Lock { get; set; } = "none";
    
    [JsonPropertyName("lockUnblock")]
    public string LockUnblock { get; set; } = "true";
    
    [JsonPropertyName("restartUnblock")]
    public string RestartUnblock { get; set; } = "true";
    
    [JsonPropertyName("break")]
    public string Break { get; set; } = "none";
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
    
    [JsonPropertyName("randomTextLength")]
    public string RandomTextLength { get; set; } = "";
    
    [JsonPropertyName("window")]
    public string Window { get; set; } = "";
    
    [JsonPropertyName("users")]
    public string Users { get; set; } = "all";
    
    [JsonPropertyName("web")]
    public List<string> Web { get; set; } = new();
    
    [JsonPropertyName("exceptions")]
    public List<string> Exceptions { get; set; } = new();
    
    [JsonPropertyName("apps")]
    public List<string> Apps { get; set; } = new();
    
    [JsonPropertyName("schedule")]
    public List<object> Schedule { get; set; } = new();
    
    [JsonPropertyName("customUsers")]
    public List<string> CustomUsers { get; set; } = new();

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = "";

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public class CtblAdditional
{
    [JsonPropertyName("paused")]
    public string Paused { get; set; } = "false";
    
    [JsonPropertyName("proStatus")]
    public string ProStatus { get; set; } = "pro";
    
    [JsonPropertyName("trialEnd")]
    public string TrialEnd { get; set; } = "";
    
    [JsonPropertyName("updateAvailable")]
    public string UpdateAvailable { get; set; } = "false";
    
    [JsonPropertyName("browserList")]
    public string BrowserList { get; set; } = "";
    
    [JsonPropertyName("forceExtensionInstall")]
    public string ForceExtensionInstall { get; set; } = "false";
    
    [JsonPropertyName("scheduleShowAll")]
    public string ScheduleShowAll { get; set; } = "false";
    
    [JsonPropertyName("blocksOrder")]
    public string BlocksOrder { get; set; } = "";
    
    [JsonPropertyName("win10")]
    public List<string> Win10 { get; set; } = new();
    
    [JsonPropertyName("users")]
    public List<string> Users { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}



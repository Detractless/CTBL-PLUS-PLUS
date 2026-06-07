using System;

namespace CtblPlusPlus.Core.Constants;

public static class IpcCommands
{
    // JS to C# Commands
    public const string GetQueue = "getQueue";
    public const string GetAuditLog = "getAuditLog";
    public const string GetSecurityState = "getSecurityState";
    public const string GetSettings = "getSettings";
    public const string GetBlocks = "getBlocks";
    public const string RequestSettingChange = "requestSettingChange";
    public const string RequestException = "requestException";
    public const string RequestRemoval = "requestRemoval";
    public const string CancelRequest = "cancelRequest";
    
    public const string RequestAppControlRule = "requestAppControlRule";
    public const string RevokeAppControlRule = "revokeAppControlRule";
    public const string ToggleAppControl = "toggleAppControl";
    public const string GetAppControlState = "getAppControlState";
    public const string GetAppRegistry = "getAppRegistry";
    public const string BulkUpdateAppControlRules = "bulkUpdateAppControlRules";
    public const string BrowseForApp = "browseForApp";
    public const string SaveTemplateFile = "saveTemplateFile";

    // C# to JS Events/Data
    public const string QueueData = "queueData";
    public const string AuditLogData = "auditLogData";
    public const string BlocksData = "blocksData";
    public const string SettingsData = "settingsData";
    public const string SecurityState = "securityState";
    public const string AppControlState = "appControlState";
    public const string AppRegistryData = "appRegistryData";
    public const string AppSelected = "appSelected";
}



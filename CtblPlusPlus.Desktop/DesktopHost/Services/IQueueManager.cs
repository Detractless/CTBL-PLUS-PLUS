namespace CtblPlusPlus.DesktopHost.Services;

public interface IQueueManager
{
    void QueueAction(string blockName, string targetUrl, string logAction);
    void HandleSettingChange(string targetKey, double newValue);
    void HandleToggleAppControl();
    void HandleAppControlRuleRequest(string appPath);
    void HandleRevokeAppControlRule(string ruleId);
    void HandleBulkAppControlUpdate(IEnumerable<string> ids, string targetStatus);
    void HandleBulkAppControlRuleRequest(IEnumerable<string> paths, string status);
}



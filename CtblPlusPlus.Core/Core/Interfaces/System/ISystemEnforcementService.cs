using System;

namespace CtblPlusPlus.Core.Interfaces.System;

public interface ISystemEnforcementService
{
    void EnforceSettingsPolicy();
    void RemoveSettingsPolicy();
    void CorrectSystemTime(DateTime utcTime);
    void InitiateLogout();
    void TriggerLockdown();
    bool IsLockdownActive();
}



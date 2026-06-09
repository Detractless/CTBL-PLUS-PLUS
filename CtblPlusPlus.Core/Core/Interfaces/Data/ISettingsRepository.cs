namespace CtblPlusPlus.Core.Interfaces.Data;

public interface ISettingsRepository
{
    string GetSetting(string key, string defaultValue = "");
    void SetSetting(string key, string value);
}



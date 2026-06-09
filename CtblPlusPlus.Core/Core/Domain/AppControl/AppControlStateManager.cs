using System;
using System.Linq;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.AppControl;

public class AppControlStateManager
{
    private readonly IAppControlRepository _repo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ColdTurkeyInjector _injector;

    public AppControlStateManager(
        IAppControlRepository repo,
        ISettingsRepository settingsRepo,
        ColdTurkeyInjector injector)
    {
        _repo = repo;
        _settingsRepo = settingsRepo;
        _injector = injector;
    }

    public bool IsAppControlEnabled()
    {
        string val = _settingsRepo.GetSetting("AppControlEnabled", "false");
        return val.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Synchronizes any new "Detected" apps into Cold Turkey.
    /// Acts as the central gatekeeper, ensuring we only inject when App Control is enabled.
    /// </summary>
    public void SyncDetectedApps()
    {
        if (!IsAppControlEnabled()) return;

        try
        {
            var detected = _repo.GetAppsByStatus("Detected");
            if (detected.Any())
            {
                // First: mark as Blocked in DB
                _repo.BulkSetAppStatus(detected.Select(a => a.Id), "Blocked");

                // Then: inject into Cold Turkey
                bool success = _injector.InjectApps(detected.Select(a => a.ExePath));
                if (success)
                {
                    foreach (var app in detected)
                        _repo.SetColdTurkeyInjected(app.Id, true);

                    EngineLogger.Log("AppControlState", $"Injected {detected.Count} app(s) into Cold Turkey.");
                }
                else
                {
                    EngineLogger.Log("AppControlState", $"CT injection failed for {detected.Count} app(s).");
                }
                
            }

            // Retry unjected apps
            var unjected = _repo.GetUnjectedBlockedApps();
            if (unjected.Any())
            {
                bool success = _injector.InjectApps(unjected.Select(a => a.ExePath));
                if (success)
                {
                    foreach (var app in unjected)
                        _repo.SetColdTurkeyInjected(app.Id, true);

                    EngineLogger.Log("AppControlState", $"Retried injection for {unjected.Count} previously failed app(s).");
                }
            }
        }
        catch (Exception ex)
        {
            EngineLogger.Log("AppControlState", $"[ERROR] State sync failed: {ex.Message}");
        }
    }
}



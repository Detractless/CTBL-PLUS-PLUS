using System;
using System.Linq;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.AppControl;

/// <summary>
/// Handles App Control queue requests: Allow, Revoke, Enable, Disable.
/// Extracted from QueueDispatcher to keep that file focused on queue dispatch.
/// </summary>
public class AppControlQueueHandler
{
    private readonly IAppControlRepository _repo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ColdTurkeyInjector _injector;
    private readonly AppControlStateManager _stateManager;

    public AppControlQueueHandler(
        IAppControlRepository repo,
        ISettingsRepository settingsRepo,
        ColdTurkeyInjector injector,
        AppControlStateManager stateManager)
    {
        _repo = repo;
        _settingsRepo = settingsRepo;
        _injector = injector;
        _stateManager = stateManager;
    }

    /// <summary>
    /// Allows an application: removes it from Cold Turkey's block list
    /// and marks it as Allowed in the database.
    /// </summary>
    public void HandleAllow(string appPath)
    {
        if (!System.IO.File.Exists(appPath))
        {
            EngineLogger.Log("AppControlQueue", $"Allow failed — file not found: {appPath}");
            throw new System.IO.FileNotFoundException("File not found", appPath);
        }

        // 1. Remove from Cold Turkey
        _injector.RemoveApps(new[] { appPath });

        // 2. Update DB — find existing entry or create one
        var app = _repo.GetAllApps().FirstOrDefault(a => a.ExePath.Equals(appPath, StringComparison.OrdinalIgnoreCase));
        if (app != null)
        {
            _repo.SetAppStatus(app.Id, "Allowed");
            _repo.SetColdTurkeyInjected(app.Id, false);
        }
        else
        {
            var (name, publisher, _) = AppIdentityResolver.GetIdentity(appPath);
            _repo.UpsertApp(appPath, name, publisher);
            app = _repo.GetAllApps().FirstOrDefault(a => a.ExePath.Equals(appPath, StringComparison.OrdinalIgnoreCase));
            if (app != null) _repo.SetAppStatus(app.Id, "Allowed");
        }

        IpcServer.BroadcastEvent("StateChanged", "appRegistry");
        EngineLogger.Log("AppControlQueue", $"Allowed: {appPath}");
    }

    /// <summary>
    /// Revokes access for an application: re-injects it into Cold Turkey
    /// and sets its status back to Blocked.
    /// </summary>
    public void HandleRevoke(string appId)
    {
        var allApps = _repo.GetAllApps();
        var app = allApps.FirstOrDefault(a => a.Id == appId);

        if (app != null && System.IO.File.Exists(app.ExePath))
        {
            // 1. Set status to Blocked
            _repo.SetAppStatus(app.Id, "Blocked");

            // 2. Re-inject into Cold Turkey
            bool success = _injector.InjectApps(new[] { app.ExePath });
            if (success)
            {
                _repo.SetColdTurkeyInjected(app.Id, true);
            }
        }
        else if (app != null)
        {
            // File no longer exists — just mark as Detected
            _repo.SetAppStatus(app.Id, "Detected");
        }

        IpcServer.BroadcastEvent("StateChanged", "appRegistry");
        EngineLogger.Log("AppControlQueue", $"Revoked: {app?.ExePath ?? appId}");
    }

    /// <summary>
    /// Revokes access for an application by its file path.
    /// </summary>
    public void HandleRevokePath(string appPath)
    {
        var app = _repo.GetAllApps().FirstOrDefault(a => a.ExePath.Equals(appPath, StringComparison.OrdinalIgnoreCase));
        if (app != null)
        {
            HandleRevoke(app.Id);
        }
        else
        {
            EngineLogger.Log("AppControlQueue", $"Revoke Path failed — app not found in registry: {appPath}");
        }
    }


    /// <summary>
    /// Enables App Control: syncs existing detected apps.
    /// </summary>
    public void HandleEnable()
    {
        _settingsRepo.SetSetting("AppControlEnabled", "true");
        IpcServer.BroadcastEvent("StateChanged", "appControl");
        _stateManager.SyncDetectedApps();
        EngineLogger.Log("AppControlQueue", "App Control enabled.");
    }

    /// <summary>
    /// Disables App Control.
    /// </summary>
    public void HandleDisable()
    {
        _settingsRepo.SetSetting("AppControlEnabled", "false");

        // Reset all Blocked apps back to Detected so the enforcer
        // naturally clears the CT apps list on its next tick
        var blockedApps = _repo.GetAppsByStatus("Blocked");
        if (blockedApps.Any())
        {
            _repo.BulkSetAppStatus(blockedApps.Select(a => a.Id), "Detected");
            foreach (var app in blockedApps)
                _repo.SetColdTurkeyInjected(app.Id, false);
        }

        IpcServer.BroadcastEvent("StateChanged", "appControl");
        IpcServer.BroadcastEvent("StateChanged", "appRegistry");
        EngineLogger.Log("AppControlQueue", "App Control disabled. App statuses reset to Detected.");
    }

    /// <summary>
    /// Handles bulk allow/revoke operations from the UI.
    /// </summary>
    public void HandleBulkUpdate(string[] paths, string status)
    {
        foreach (var path in paths)
        {
            try
            {
                if (status == "Allowed")
                {
                    HandleAllow(path);
                }
                else if (status == "Detected")
                {
                    // Find by path and revoke
                    var app = _repo.GetAllApps().FirstOrDefault(a => a.ExePath.Equals(path, StringComparison.OrdinalIgnoreCase));
                    if (app != null)
                    {
                        HandleRevoke(app.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                EngineLogger.Log("AppControlQueue", $"[ERROR] Bulk update for {path}: {ex.Message}");
            }
        }
    }
}



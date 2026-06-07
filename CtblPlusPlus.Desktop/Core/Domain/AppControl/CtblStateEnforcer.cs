using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.Interfaces.Data;

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.AppControl;

/// <summary>
/// Proactive background service that monitors the Cold Turkey database
/// every 45 seconds to ensure the CTBL++ block remains enabled, locked,
/// and contains the correct list of blocked applications.
/// </summary>
public class CtblStateEnforcer : BackgroundService
{
    private readonly ColdTurkeyInjector _injector;
    private readonly IAppControlRepository _appRepo;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(45);

    public CtblStateEnforcer(ColdTurkeyInjector injector, IAppControlRepository appRepo)
    {
        _injector = injector;
        _appRepo = appRepo;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        EngineLogger.Log("CtblStateEnforcer", "Proactive state and content enforcement service started (Interval: 45s).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Get the "Intended" state from our repository
                var blockedApps = _appRepo.GetAppsByStatus("Blocked");
                
                // 2. Format paths for Cold Turkey comparison (file:C:/path/to/exe)
                var intendedPaths = blockedApps
                    .Select(a => $"file:{a.ExePath.Replace("\\", "/")}")
                    .ToList();

                // 3. Perform the enforcement check
                // This only kills processes if it detects a mismatch in flags or app list
                _injector.ForceEnforce(intendedPaths);
            }
            catch (Exception ex)
            {
                EngineLogger.Log("CtblStateEnforcer", $"[ERROR] Enforcement check failed: {ex.Message}");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        EngineLogger.Log("CtblStateEnforcer", "State enforcement service stopping.");
    }
}



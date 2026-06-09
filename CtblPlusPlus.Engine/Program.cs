using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.Domain.AppControl;
using CtblPlusPlus.Core.Domain.Queue;
using CtblPlusPlus.Core.Domain.Queue.Handlers;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Persistence.Repositories;
using CtblPlusPlus.Core.Communication;
using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Security.Enforcers;
using CtblPlusPlus.Core.Security.Lockdown;
using CtblPlusPlus.Engine.Handlers;

bool createdNew;
using var mutex = new Mutex(true, @"Global\CtblPlusPlus.Core_Mutex_v2", out createdNew);
if (!createdNew)
{
    try
    {
        System.Diagnostics.EventLog.WriteEntry(
            "Application",
            "CTBL Engine failed to acquire Global Mutex v2. Another instance is running.",
            System.Diagnostics.EventLogEntryType.Error);
    }
    catch { }
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CTBL Queue Delay Engine";
});

// Core interfaces
builder.Services.AddSingleton<IHmacProvider, DpapiHmacProvider>();
builder.Services.AddSingleton<IQueueRepository, SqliteQueueRepository>();
builder.Services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
builder.Services.AddSingleton<IAuditRepository, SqliteAuditRepository>();
builder.Services.AddSingleton<IAppControlRepository, SqliteAppControlRepository>();

// System services
builder.Services.AddSingleton<ITimeSource, InternetTimeSource>();
builder.Services.AddSingleton<ISystemEnforcementService, WindowsSystemEnforcementService>();
builder.Services.AddSingleton<IInstallStateProvider, ScmInstallStateProvider>();

// -- Phase 1: Lockdown-only dependencies removed --------------------
builder.Services.AddSingleton<IProcessInvoker, WindowsProcessInvoker>();
builder.Services.AddSingleton<IFileDeleter, WindowsFileDeleter>();
builder.Services.AddSingleton<WindowsServiceMonitor>();
// --------------------------------------------------------------------

// Database clients
builder.Services.AddSingleton<DatabaseClient>();
builder.Services.AddSingleton<CtblCliClient>();

// App Control services
builder.Services.AddSingleton<ColdTurkeyInjector>();
builder.Services.AddSingleton<AppControlStateManager>();
builder.Services.AddSingleton<AppControlQueueHandler>();
builder.Services.AddSingleton<CtblStateEnforcer>();

// Queue components
builder.Services.AddSingleton<QueueDispatcher>();
builder.Services.AddSingleton<QueueSecurityValidator>();
builder.Services.AddSingleton<WebsiteTamperRemediator>();
builder.Services.AddSingleton<IQueueRequestHandler, SettingsQueueHandler>();
builder.Services.AddSingleton<IQueueRequestHandler, AppControlQueueHandlerWrapper>();
builder.Services.AddSingleton<IQueueRequestHandler, QueuedDelayQueueHandler>();
builder.Services.AddSingleton<IQueueRequestHandler, WebsiteExceptionQueueHandler>();

// Hosted background workers - core
builder.Services.AddHostedService<QueueDispatcher>(sp => sp.GetRequiredService<QueueDispatcher>());
builder.Services.AddHostedService<AppDiscoveryService>();
builder.Services.AddHostedService<CtblStateEnforcer>(sp => sp.GetRequiredService<CtblStateEnforcer>());

// -- Phase 06: IntegrityVerificationService re-enabled -----------------------
builder.Services.AddHostedService<TimeEnforcer>();
builder.Services.AddHostedService<FactoryResetEnforcer>();
builder.Services.AddHostedService<TaskManagerEnforcer>();
builder.Services.AddHostedService<AccountEnforcer>();
builder.Services.AddHostedService<PrivilegeEnforcer>();
builder.Services.AddHostedService<BinaryFileLockService>();
builder.Services.AddHostedService<IntegrityVerificationService>();
builder.Services.AddHostedService<ScorchedEarthPurgeService>();
builder.Services.AddHostedService<VaultAclEnforcementService>();
builder.Services.AddHostedService<FileSystemWatchdogService>();
builder.Services.AddHostedService<BrowserEnforcer>();
builder.Services.AddHostedService<PersistenceEnforcer>();
builder.Services.AddHostedService<UninstallerEnforcer>();
// --------------------------------------------------------------------

// Communication - PidBroker is core infrastructure (watchdog PID coordination)
builder.Services.AddHostedService<PidBroker>();

// Phase 3: Local Web Server for UI
builder.Services.AddHostedService<CtblPlusPlus.Engine.LocalWebServerService>();

var host = builder.Build();
host.Run();

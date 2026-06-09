// Phase 3: Real WatchdogHeartbeat restored.
// Monitors Engine and Wd1, restarts them on death. Marks self as critical process.

using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Interfaces.Security;

const string wdName = "Wd2";

bool createdNew;
using var mutex = new Mutex(true, $@"Global\CtblPlusPlus_{wdName}_Mutex", out createdNew);
if (!createdNew) return;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "CTBL Queue Delay Watchdog Secondary";
});

builder.Services.AddSingleton<IHmacProvider, DpapiHmacProvider>();
builder.Services.AddSingleton<WatchdogHeartbeat>(sp =>
    new WatchdogHeartbeat(wdName, sp.GetRequiredService<IHmacProvider>()));
builder.Services.AddHostedService<WatchdogHeartbeat>(sp =>
    sp.GetRequiredService<WatchdogHeartbeat>());

var host = builder.Build();
host.Run();

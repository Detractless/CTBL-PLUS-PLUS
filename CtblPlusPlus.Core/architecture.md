# CTBL++ Architecture (current)

This is a **multi-project .NET 10 (Windows) solution**, built per-project by `ctbl.bat`
(there is no `.sln`). The UI is delivered by **patching Cold Turkey's own web interface**,
not by a standalone window.

## Projects

| Project | Type | Role |
|---|---|---|
| **CtblPlusPlus.Core** | Class library (`net10.0-windows`, headless) | The shared core: `Core/` (queue, persistence, security, lockdown, system, app-control, communication) and `Models/`. Referenced by every other project. Formerly named `CtblPlusPlus.Desktop`. |
| **CtblPlusPlus.Engine** | Windows Service (`Exe`) | The only process that does real work. Hosts the repositories, `QueueDispatcher`, the enforcer/lockdown battery, `AppDiscoveryService`, `PidBroker`, and `LocalWebServerService` (the REST API on `http://127.0.0.1:58123`). |
| **CtblPlusPlus.Wd1 / Wd2** | Windows Services (`Exe`) | Watchdogs (`WatchdogHeartbeat`). Monitor the Engine and each other; restart on death; mark self critical. |
| **CtblPlusPlus.Installer** | WPF + WebView2 (`WinExe`) | The setup wizard. Embeds `Payload.zip` (published Engine + Wd1 + Wd2) and ships the Cold Turkey installer. |

## The UI

Cold Turkey's front-end lives (de-bundled) in `Unobfuscated_Backup/web/Raw`. `Deploy.ps1`
runs the webpack build and copies the result over `C:\Program Files\Cold Turkey\web`.
The patched UI talks to the Engine over **HTTP/JSONP** via `services/CtblApiClient.js`
against `http://127.0.0.1:58123/api/...`.

> The old standalone WPF/WebView2 dashboard and its named-pipe IPC (`IpcServer`,
> `EngineNamedPipeClient`) were **removed** — the REST API on :58123 is the sole UI bridge.

## Dependency rules (enforced)

```
CtblPlusPlus.Core            (lowest layer — references NO other project in this solution)
   ▲           ▲        ▲           ▲
   │           │        │           │
Engine       Wd1       Wd2       Installer    (each references Core only)
```

1. **Core is the floor.** It must never reference Engine, Wd1, Wd2, or Installer.
2. **No host references another host.** Engine/Wd1/Wd2/Installer reference *Core* and nothing else in-solution. Cross-host comms happen at runtime over the REST API (UI↔Engine) and `PidBroker` / SCM (watchdogs↔Engine).
3. **Core runs no UI.** It is a class library that never instantiates a `Window`, `Dispatcher`, or any UI element — the standalone dashboard is gone. It does still reference the Windows Desktop and ASP.NET Core shared frameworks, not for UI, but because Core legitimately uses Windows-only runtime APIs (DPAPI, ACLs/Windows identity, EventLog, the registry, Windows services) and hosting abstractions (`BackgroundService`). Slimming those framework references down to explicit NuGet packages is a possible later cleanup, best done with a compiler to hand. User-facing notifications must go through the REST/UI layer, not from inside the Engine.

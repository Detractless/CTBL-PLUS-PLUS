using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Security.Enforcers;

public class TimeEnforcer : BackgroundService
{
    public string Name => "Time Enforcer";
    public Action<bool>? OnSecurityStatusChanged;

    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan LoopInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimeCheckInterval = TimeSpan.FromMinutes(5);

    private DateTime _highWaterMarkUtc = DateTime.MinValue;
    private bool _isDirtyFlag = false;

    private readonly string _persistenceFile;
    private readonly ITimeSource _timeSource;
    private readonly ISystemEnforcementService _enforcement;
    private readonly WindowsServiceMonitor _serviceMonitor;
    private readonly ISettingsRepository _settingsRepo;

    
    public TimeEnforcer(ITimeSource timeSource, ISystemEnforcementService enforcement, WindowsServiceMonitor serviceMonitor, ISettingsRepository settingsRepo)
    {
        _timeSource = timeSource;
        _enforcement = enforcement;
        _serviceMonitor = serviceMonitor;
        _settingsRepo = settingsRepo;

        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus");
        if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
        _persistenceFile = Path.Combine(dataDir, "Anti-TM.txt");
    }


    private void BroadcastStatus()
    {
        try
        {
            bool isSecure = !_enforcement.IsLockdownActive();
            
            // Persist to shared DB for UI polling (Single Source of Truth)
            _settingsRepo.SetSetting("Security_IsSecure", isSecure.ToString());

            OnSecurityStatusChanged?.Invoke(isSecure);
        }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // -- Startup Initialization Block (formerly in Start()) ----------
        Log("Time Enforcer starting...");

        LoadPersistence();
        DateTime currentUtc = DateTime.UtcNow;

        // Fire-and-forget initial internet sync
        _ = Task.Run(() => {
            try {
                Log("Starting initial background sync...");
                DateTime? startupAuthTime = _timeSource.GetUtcTime(out _);

                if (startupAuthTime.HasValue && _highWaterMarkUtc > startupAuthTime.Value.Add(Tolerance))
                {
                    _highWaterMarkUtc = startupAuthTime.Value;
                    SavePersistence();
                }

                if (!startupAuthTime.HasValue && _isDirtyFlag)
                {
                     Log("Unclean shutdown detected while offline. Fail-Secure lockdown triggered.");
                     ShowNotification("SECURITY ALERT", "Unclean shutdown detected while offline. Entering Fail-Secure Lockdown.", "Warning");
                     _enforcement.TriggerLockdown();
                }
            } catch (Exception ex) {
                Log($"Initial sync failed: {ex.Message}");
            }
        }, stoppingToken);

        _isDirtyFlag = true;

        if (_highWaterMarkUtc > DateTime.MinValue && currentUtc < _highWaterMarkUtc.AddMinutes(-1))
        {
            Log("System time rollback detected on startup! Entering Lockdown.");
            ShowNotification("SECURITY ALERT", "System time rollback detected! Entering Lockdown.", "Error");
            _enforcement.TriggerLockdown();
        }

        UpdateHighWaterMark(currentUtc);
        SavePersistence();

        ShowNotification("Time Enforcer Started", "Monitoring time integrity via High Water Mark protocol.", "Info");

        // -- Enforcement Loop (formerly EnforcementLoop()) ---------------
        DateTime nextTimeCheck = DateTime.MinValue;
        Log("Enforcement loop started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                currentUtc = DateTime.UtcNow;

                _serviceMonitor.EnsureTimeServiceCheck();
                _enforcement.EnforceSettingsPolicy();
                BroadcastStatus();
                UpdateHighWaterMark(currentUtc);

                if (currentUtc < _highWaterMarkUtc.AddMinutes(-5))
                {
                     Log("Time rollback detected! Triggering violation.");
                     ShowNotification("TAMPER DETECTED", "Time moved backwards during session!", "Error");
                     _enforcement.TriggerLockdown();
                     HandleViolation(TimeSpan.FromMinutes(5), _highWaterMarkUtc);
                     return; // Exit ExecuteAsync — violation handled
                }

                if (DateTime.Now >= nextTimeCheck)
                {
                    Log("Attempting internet time sync...");
                    DateTime? authorityTime = _timeSource.GetUtcTime(out string sourceName);

                    if (authorityTime.HasValue)
                    {
                         Log($"Sync successful via {sourceName}. Expected UTC: {authorityTime.Value}");
                         DateTime expectedUtc = authorityTime.Value;
                         TimeSpan drift = currentUtc - expectedUtc;

                         if (Math.Abs(drift.TotalSeconds) <= Tolerance.TotalSeconds)
                         {
                             UpdateHighWaterMark(expectedUtc);
                             if (_enforcement.IsLockdownActive())
                             {
                                 ShowNotification("Security Restored", "Time verified with " + sourceName + ". Lockdown lifted.", "Info");
                             }
                             SavePersistence();
                         }
                         else
                         {
                             Log($"Drift detected: {drift.TotalSeconds}s. Triggering violation.");
                             UpdateHighWaterMark(expectedUtc, forceOverride: true);
                             SavePersistence();
                             ShowNotification("VIOLATION DETECTED", "Time drift detected! Correcting time...", "Error");
                             HandleViolation(drift, expectedUtc);
                             return; // Exit ExecuteAsync — violation handled
                         }
                    }
                    else
                    {
                        Log("Internet time sync failed (all sources).");
                        BroadcastStatus();
                    }
                    nextTimeCheck = DateTime.Now.Add(TimeCheckInterval);
                }
            }
            catch (Exception ex)
            {
                Log($"Error in enforcement loop: {ex.Message}");
            }

            await Task.Delay(LoopInterval, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _isDirtyFlag = false;
        UpdateHighWaterMark(DateTime.UtcNow);
        SavePersistence();

        ShowNotification("Time Enforcer Stopped", "Monitoring disabled. Time manipulation is possible.", "Info");
        _enforcement.RemoveSettingsPolicy();

        return base.StopAsync(cancellationToken);
    }

    private void ShowNotification(string title, string text, string icon) =>
        IpcServer.BroadcastNotification("Notification", title, text, icon);

    private void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus", "process_log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:O}: [TimeEnforcer] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private void HandleViolation(TimeSpan drift, DateTime expectedUtc)
    {
        _enforcement.CorrectSystemTime(expectedUtc);
        _enforcement.InitiateLogout();
    }

    private void UpdateHighWaterMark(DateTime verificationTime, bool forceOverride = false)
    {
        if (verificationTime > _highWaterMarkUtc || forceOverride)
        {
            _highWaterMarkUtc = verificationTime;
        }
    }

    private void LoadPersistence()
    {
        try
        {
            if (File.Exists(_persistenceFile))
            {
                string[] lines = File.ReadAllLines(_persistenceFile);
                foreach (var line in lines)
                {
                    if (line.StartsWith("LastTimestamp="))
                    {
                        if (long.TryParse(line.Substring("LastTimestamp=".Length), out long ticks))
                        {
                            _highWaterMarkUtc = new DateTime(ticks, DateTimeKind.Utc);
                        }
                    }
                    else if (line.StartsWith("IsDirty="))
                    {
                        bool.TryParse(line.Substring("IsDirty=".Length), out _isDirtyFlag);
                    }
                }
            }
        }
        catch { }
    }

    private void SavePersistence()
    {
        try
        {
            using StreamWriter sw = new StreamWriter(_persistenceFile, false);
            sw.WriteLine("LastTimestamp=" + _highWaterMarkUtc.Ticks);
            sw.WriteLine("ReadableTime=" + _highWaterMarkUtc.ToString("o"));
            sw.WriteLine("IsDirty=" + _isDirtyFlag);
        }
        catch { }
    }
}



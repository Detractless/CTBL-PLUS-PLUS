using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Persistence;
using CtblPlusPlus.Core.AppSystem;

using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Security.Lockdown;

namespace CtblPlusPlus.Core.Security;

public class WatchdogHeartbeat : BackgroundService
{
    private const string PipeName = "CtblPlusPlusPidBroker";
    private readonly string _wdName;
    private readonly string _exePath;
    private readonly IHmacProvider _hmac;

    public WatchdogHeartbeat(string watchdogName, IHmacProvider hmac)
    {
        _wdName = watchdogName;
        _exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "CtblPlusPlus.exe";
        _hmac = hmac;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 0. Mark as Critical
        CtblPlusPlus.Core.AppSystem.NativeMethods.SetCriticalProcess(true);

        // 1. Initial Registration
        int initialEnginePid = -1;
        while (initialEnginePid <= 0 && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipeClient.ConnectAsync(5000, stoppingToken);

                // Raw byte write — bypasses StreamWriter buffering issues on async pipes
                string myPidStr = Environment.ProcessId.ToString();
                string registerPayload = $"REGISTER_WATCHDOG|{myPidStr}|{_wdName}";
                string registerSig    = _hmac.ComputeHmac(registerPayload);
                string message = $"SIG:{registerSig}:{registerPayload}\n";
                byte[] msgBytes = Encoding.UTF8.GetBytes(message);
                await pipeClient.WriteAsync(msgBytes, 0, msgBytes.Length, stoppingToken);
                await pipeClient.FlushAsync(stoppingToken);

                // Raw byte read response
                var buffer = new byte[4096];
                int bytesRead = await pipeClient.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                string rawResp = bytesRead > 0 ? Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n') : null;

                // Verify the server's HMAC-signed response before trusting its content.
                string? response = null;
                if (rawResp != null && rawResp.StartsWith("SIG:"))
                {
                    var sigParts = rawResp.Split(':', 3);
                    if (sigParts.Length == 3)
                    {
                        string receivedSig    = sigParts[1];
                        string receivedPayload = sigParts[2];
                        if (_hmac.ComputeHmac(receivedPayload) == receivedSig)
                            response = receivedPayload;
                    }
                }
                if (response != null && response.StartsWith("ENGINE_PID|"))
                {
                    initialEnginePid = int.Parse(response.Split('|')[1]);
                    Console.WriteLine($"[{_wdName}] Successfully registered with PidBroker. Engine PID: {initialEnginePid}");
                    LockdownLogger.Log("Watchdog", $"{_wdName} successfully registered with PidBroker. Engine PID: {initialEnginePid}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{_wdName}] Failed to register with PidBroker: {ex.Message}");
                LockdownLogger.Log("Watchdog", $"{_wdName} failed to register with PidBroker: {ex.Message}");
                await Task.Delay(2000, stoppingToken);
            }
        }

        // 2. Continuous Cross-Monitoring Loop
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool engineHealthy = true;
                // Send Heartbeat
                try
                {
                    using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                    await pipeClient.ConnectAsync(2000, stoppingToken);
                    string heartbeatPayload = $"HEARTBEAT|{_wdName}";
                    string heartbeatSig     = _hmac.ComputeHmac(heartbeatPayload);
                    byte[] hbBytes = Encoding.UTF8.GetBytes($"SIG:{heartbeatSig}:{heartbeatPayload}\n");
                    await pipeClient.WriteAsync(hbBytes, 0, hbBytes.Length, stoppingToken);
                    await pipeClient.FlushAsync(stoppingToken);
                }
                catch 
                { 
                    engineHealthy = false;
                }

                // Fetch latest PIDs and Health
                int currentEnginePid = 0;
                int otherWdPid = 0;
                bool otherWdHealthy = true;

                if (engineHealthy) // Only try to get PIDs if engine seems alive
                {
                    try
                    {
                        using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                        await pipeClient.ConnectAsync(2000, stoppingToken);

                        string getPidsPayload = "GET_PIDS";
                        string getPidsSig     = _hmac.ComputeHmac(getPidsPayload);
                        byte[] gpBytes = Encoding.UTF8.GetBytes($"SIG:{getPidsSig}:{getPidsPayload}\n");
                        await pipeClient.WriteAsync(gpBytes, 0, gpBytes.Length, stoppingToken);
                        await pipeClient.FlushAsync(stoppingToken);

                        var gpBuffer = new byte[4096];
                        int gpRead = await pipeClient.ReadAsync(gpBuffer, 0, gpBuffer.Length, stoppingToken);
                        string rawResp = gpRead > 0 ? Encoding.UTF8.GetString(gpBuffer, 0, gpRead).TrimEnd('\r', '\n') : null;

                        // Verify the server's HMAC-signed response before trusting its content.
                        // Expected wire format: "SIG:<hmac>:<payload>"
                        string? response = null;
                        if (rawResp != null && rawResp.StartsWith("SIG:"))
                        {
                            var sigParts = rawResp.Split(':', 3); // ["SIG", sig, payload]
                            if (sigParts.Length == 3)
                            {
                                string receivedSig     = sigParts[1];
                                string receivedPayload = sigParts[2];
                                if (_hmac.ComputeHmac(receivedPayload) == receivedSig)
                                    response = receivedPayload;
                            }
                        }
                        
                        if (response != null && response.StartsWith("PIDS|"))
                        {
                            var parts = response.Split('|');
                            currentEnginePid = int.Parse(parts[1]);
                            int w1 = int.Parse(parts[2]);
                            int w2 = int.Parse(parts[3]);
                            
                            if (parts.Length >= 6)
                            {
                                bool w1H = parts[4] == "1";
                                bool w2H = parts[5] == "1";
                                otherWdHealthy = _wdName == "Wd1" ? w2H : w1H;
                            }

                            otherWdPid = _wdName == "Wd1" ? w2 : w1;
                        }
                    }
                    catch
                    {
                        engineHealthy = false;
                    }
                }

                // Check for suspension or death
                if (!engineHealthy)
                {
                    Console.WriteLine($"[{_wdName}] ENGINE UNRESPONSIVE. Suspected Suspension or Death.");
                    await HandleDeathAsync("Engine", stoppingToken, currentEnginePid);
                }

                if (otherWdPid > 0 && !otherWdHealthy)
                {
                    string target = _wdName == "Wd1" ? "Wd2" : "Wd1";
                    Console.WriteLine($"[{_wdName}] {target} STALE HEARTBEAT. Suspected Suspension.");
                    await HandleDeathAsync(target, stoppingToken, otherWdPid);
                }

                if (currentEnginePid > 0 || otherWdPid > 0)
                {
                    Task engineTask = currentEnginePid > 0 ? WaitForProcessAsync(currentEnginePid, "Engine", stoppingToken) : Task.Delay(Timeout.Infinite, stoppingToken);
                    Task wdTask = otherWdPid > 0 ? WaitForProcessAsync(otherWdPid, _wdName == "Wd1" ? "Wd2" : "Wd1", stoppingToken) : Task.Delay(Timeout.Infinite, stoppingToken);
                    
                    bool missingTarget = (currentEnginePid == 0 || otherWdPid == 0);
                    Task timeoutTask = missingTarget ? Task.Delay(5000, stoppingToken) : Task.Delay(5000, stoppingToken); // Check heartbeat every 5s

                    await Task.WhenAny(engineTask, wdTask, timeoutTask);
                }
                else
                {
                    await Task.Delay(2000, stoppingToken);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Console.WriteLine($"[{_wdName}] Monitor loop exception: {ex.Message}");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }

    private async Task WaitForProcessAsync(int pid, string targetName, CancellationToken stoppingToken)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.EnableRaisingEvents = true; 
            await process.WaitForExitAsync(stoppingToken);
            
            Console.WriteLine($"[{_wdName}] !!! {targetName.ToUpper()} TERMINATION DETECTED !!!");
            await HandleDeathAsync(targetName, stoppingToken, pid);
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"[{_wdName}] {targetName} process ID vanished before wait. Resurrecting.");
            await HandleDeathAsync(targetName, stoppingToken, pid);
        }
        catch (Exception) { }
    }

    private async Task HandleDeathAsync(string targetName, CancellationToken stoppingToken, int pid = 0)
    {
        try
        {
            Console.WriteLine($"[{_wdName}] Healing Sequence Initiated for {targetName}.");

            if (pid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        Console.WriteLine($"[{_wdName}] Force-killing unresponsive {targetName} (PID: {pid})...");
                        proc.Kill(true);
                        await Task.Delay(1000, stoppingToken);
                    }
                }
                catch { }
            }
            
            if (!File.Exists(_exePath))
            {
                Console.WriteLine($"[{_wdName}] Executable missing at {_exePath}! Proceeding to extract from Secure Vault...");
                string destinationDir = AppDomain.CurrentDomain.BaseDirectory;
                bool restored = VaultRecoveryService.RestoreTarget("CtblPlusPlus", destinationDir);
                if (restored)
                    Console.WriteLine($"[{_wdName}] Executable restored from Secure Vault.");
                else
                    Console.WriteLine($"[{_wdName}] CRITICAL: Vault restoration failed!");
            }

            if (File.Exists(_exePath))
            {
                string arg = targetName == "Engine" ? "--engine" : (targetName == "Wd1" ? "--watchdog1" : "--watchdog2");
                Console.WriteLine($"[{_wdName}] Restarting {targetName} with arg {arg}...");
                
                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = arg,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                
                await Task.Delay(2000, stoppingToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_wdName}] Revival failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Phase 3 fix: reverse the critical-process flag BEFORE the process exits.
    /// Without this, any service stop (sc stop, shutdown, restart) triggers a
    /// BugCheck (BSOD) because the process terminates while still marked critical.
    /// Mirrors exactly what PersistenceEnforcer does in the Engine.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        CtblPlusPlus.Core.AppSystem.NativeMethods.SetCriticalProcess(false);
        await base.StopAsync(cancellationToken);
    }
}





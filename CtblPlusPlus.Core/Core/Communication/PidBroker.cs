using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Security.Lockdown;

namespace CtblPlusPlus.Core.Communication;

public class PidBroker : BackgroundService
{
    private const string PipeName = "CtblPlusPlusPidBroker";
    private int? _wd1Pid = null;
    private int? _wd2Pid = null;
    private readonly global::System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastHeartbeats = new();
    private readonly IInstallStateProvider _installState;

    private readonly IHmacProvider _hmac;

    public PidBroker(IInstallStateProvider installState, IHmacProvider hmac)
    {
        _installState = installState; _hmac = hmac;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Gate: do not listen on the pipe or monitor/resurrect watchdog processes
        // unless the app is formally installed (service registered in SCM).
        if (!_installState.IsInstalled())
        {
            Console.WriteLine("[PidBroker] Not installed — watchdog resurrection disabled.");
            LockdownLogger.Log("PidBroker", "Not installed — watchdog resurrection disabled.");
            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

        // Start a background health monitor for the watchdogs
        LockdownLogger.Log("PidBroker", "PidBroker started. Listening for watchdog registration on pipe: " + PipeName);
        _ = Task.Run(async () =>
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(10000, stoppingToken);
                foreach (var wdName in new[] { "Wd1", "Wd2" })
                {
                    int? pid = wdName == "Wd1" ? _wd1Pid : _wd2Pid;
                    if (pid.HasValue && pid.Value > 0)
                    {
                        if (!_lastHeartbeats.TryGetValue(wdName, out var last) || (DateTime.UtcNow - last).TotalSeconds > 20)
                        {
                            Console.WriteLine($"[Engine] Watchdog {wdName} (PID: {pid}) STALE HEARTBEAT. Suspected Suspension.");
                            ResurrectProcess(wdName, pid.Value);
                        }
                    }
                }
            }
        }, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipeServer.WaitForConnectionAsync(stoppingToken);

                // Fire-and-forget: process this connection in the background.
                // Loop back immediately to accept the next connection.
                // This allows Wd1 and Wd2 to connect simultaneously.
                _ = ProcessConnectionAsync(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task ProcessConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken stoppingToken)
    {
        try
        {
            using (pipeServer)
            {
                // Raw byte read — bypasses StreamReader buffering issues on async pipes
                var buffer = new byte[4096];
                int bytesRead = await pipeServer.ReadAsync(buffer, 0, buffer.Length, stoppingToken);
                if (bytesRead == 0) return;
                string rawLine = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimEnd('\r', '\n');

                if (!rawLine.StartsWith("SIG:"))
                {
                    LockdownLogger.Log("PidBroker", "Protocol Violation: message lacks SIG: prefix.");
                    return;
                }

                var sigParts = rawLine.Split(':', 3);
                if (sigParts.Length < 3)
                {
                    LockdownLogger.Log("PidBroker", "Malformed SIG envelope.");
                    return;
                }

                string providedSig = sigParts[1];
                string payload     = sigParts[2];

                if (_hmac.ComputeHmac(payload) != providedSig)
                {
                    LockdownLogger.Log("PidBroker", "Security Violation: Invalid HMAC signature detected!");
                    return;
                }

                if (payload.StartsWith("REGISTER_WATCHDOG|"))
                {
                    var parts = payload.Split('|');
                    if (parts.Length >= 3)
                    {
                        int pid = int.Parse(parts[1]);
                        string wdName = parts[2];
                        if (wdName == "Wd1") _wd1Pid = pid;
                        if (wdName == "Wd2") _wd2Pid = pid;

                        _lastHeartbeats[wdName] = DateTime.UtcNow;
                        LockdownLogger.Log("PidBroker", $"Watchdog {wdName} (PID: {pid}) registered successfully.");

                        int enginePid = Environment.ProcessId;
                        string resp = $"ENGINE_PID|{enginePid}";
                        string signedResp = $"SIG:{_hmac.ComputeHmac(resp)}:{resp}\n";
                        byte[] respBytes = Encoding.UTF8.GetBytes(signedResp);
                        await pipeServer.WriteAsync(respBytes, 0, respBytes.Length, stoppingToken);
                        await pipeServer.FlushAsync(stoppingToken);

                        _ = WaitForProcessAsync(pid, wdName, stoppingToken);
                    }
                }
                else if (payload.StartsWith("HEARTBEAT|"))
                {
                    var parts = payload.Split('|');
                    if (parts.Length >= 2)
                    {
                        string sender = parts[1];
                        _lastHeartbeats[sender] = DateTime.UtcNow;
                        // No response needed — client sends heartbeat and disconnects
                    }
                }
                else if (payload == "GET_PIDS")
                {
                    int enginePid = Environment.ProcessId;
                    _lastHeartbeats["Engine"] = DateTime.UtcNow;

                    string w1 = _wd1Pid?.ToString() ?? "0";
                    string w2 = _wd2Pid?.ToString() ?? "0";

                    bool w1Healthy = _wd1Pid.HasValue && _lastHeartbeats.TryGetValue("Wd1", out var h1) && (DateTime.UtcNow - h1).TotalSeconds < 15;
                    bool w2Healthy = _wd2Pid.HasValue && _lastHeartbeats.TryGetValue("Wd2", out var h2) && (DateTime.UtcNow - h2).TotalSeconds < 15;

                    string resp = $"PIDS|{enginePid}|{w1}|{w2}|{(w1Healthy ? 1 : 0)}|{(w2Healthy ? 1 : 0)}";
                    string signedResp = $"SIG:{_hmac.ComputeHmac(resp)}:{resp}\n";
                    byte[] respBytes = Encoding.UTF8.GetBytes(signedResp);
                    await pipeServer.WriteAsync(respBytes, 0, respBytes.Length, stoppingToken);
                    await pipeServer.FlushAsync(stoppingToken);
                }
            }
        }
        catch (IOException ex) when (ex.Message.Contains("Pipe is broken") || ex.Message.Contains("pipe has been ended"))
        {
            // Normal — client disconnected after sending a fire-and-forget message
        }
        catch (Exception ex)
        {
            LockdownLogger.Log("PidBroker", $"Connection error: {ex.Message}");
        }
    }

    private async Task WaitForProcessAsync(int pid, string wdName, CancellationToken stoppingToken)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.EnableRaisingEvents = true; // Enables WaitForExitAsync
            
            // Reaches exactly 0.00% CPU utilization while awaiting the OS handle!
            await proc.WaitForExitAsync(stoppingToken);
            
            Console.WriteLine($"[Engine] Watchdog {wdName} (PID: {pid}) died! Healing sequence initiated.");
            ResurrectProcess(wdName, pid);
        }
        catch (ArgumentException)
        {
            // Process ALREADY dead before we even awaited it
            Console.WriteLine($"[Engine] Watchdog {wdName} (PID: {pid}) vanished. Healing sequence initiated.");
            ResurrectProcess(wdName, pid);
        }
        catch { }
    }

    private void ResurrectProcess(string wdName, int pid = 0)
    {
        try
        {
            // Force-kill the unresponsive/stale process if we have its PID.
            if (pid > 0)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        Console.WriteLine($"[Engine] Force-killing unresponsive {wdName} (PID: {pid})...");
                        proc.Kill(true);
                        Thread.Sleep(1000);
                    }
                }
                catch { }
            }

            // In split-EXE architecture each watchdog has its own dedicated binary.
            // The EXEs live flat in the same install directory as this engine process.
            string wdExeName = wdName == "Wd1" ? "CtblPlusPlus.Wd1.exe" : "CtblPlusPlus.Wd2.exe";
            string installDir = AppDomain.CurrentDomain.BaseDirectory;
            string wdExePath  = Path.Combine(installDir, wdExeName);

            if (!File.Exists(wdExePath))
            {
                // File deletion tamper detected — attempt vault restoration.
                Console.WriteLine($"[Engine] {wdExeName} missing from {installDir}! Restoring from Secure Vault...");
                bool restored = CtblPlusPlus.Core.Security.VaultRecoveryService.RestoreTarget(
                    Path.GetFileNameWithoutExtension(wdExeName), installDir);
                if (restored)
                    Console.WriteLine($"[Engine] {wdExeName} restored from Secure Vault.");
                else
                    Console.WriteLine($"[Engine] CRITICAL: Vault restoration failed for {wdExeName}!");
            }

            if (File.Exists(wdExePath))
            {
                Console.WriteLine($"[Engine] Restarting {wdExeName}...");
                var psi = new ProcessStartInfo
                {
                    FileName        = wdExePath,  // Dedicated watchdog EXE — no arguments needed
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Engine] Failed to resurrect {wdName}: {ex.Message}");
        }
    }
}







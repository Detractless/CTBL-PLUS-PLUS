using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using CtblPlusPlus.Core.Interfaces.Security;

namespace CtblPlusPlus.Core.Communication;

public class IpcServer : BackgroundService
{
    private static IpcServer? _instance;
    private NamedPipeServerStream? _pipeServer;
    private StreamWriter? _writer;
    private readonly object _sendLock = new();

    private readonly IHmacProvider _hmac;

    public IpcServer(IHmacProvider hmac)
    {
        _instance = this; _hmac = hmac;
    }

    public static void BroadcastNotification(string command, string title, string message, string type = "Info")
    {
        _instance?.SendMessage($"{command}|{title}|{message}|{type}");
    }
    
    public static void BroadcastEvent(string command, string data)
    {
        _instance?.SendMessage($"{command}|{data}");
    }

    private void SendMessage(string message)
    {
        // Snapshot both fields under the lock so ExecuteAsync's finally block
        // cannot null/dispose them between our null-check and the actual write.
        StreamWriter? writer;
        NamedPipeServerStream? pipe;
        lock (_sendLock)
        {
            writer = _writer;
            pipe   = _pipeServer;
        }

        if (writer == null || pipe?.IsConnected != true)
            return;

        try
        {
            string sig = _hmac.ComputeHmac(message);
            writer.WriteLine($"SIG:{sig}:{message}");
            writer.Flush();
        }
        catch { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _pipeServer = new NamedPipeServerStream("CtblPlusPlusIpcPipe", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await _pipeServer.WaitForConnectionAsync(stoppingToken);

                _writer = new StreamWriter(_pipeServer, Encoding.UTF8) { AutoFlush = true };
                using var reader = new StreamReader(_pipeServer, Encoding.UTF8);

                while (_pipeServer.IsConnected && !stoppingToken.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(stoppingToken);
                    if (line == null) break; 
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
            finally
            {
                // Hold the lock while nulling fields so SendMessage cannot
                // snapshot a live writer that is about to be disposed.
                lock (_sendLock)
                {
                    try { _writer?.Dispose(); } catch { }
                    _writer = null;
                    try { _pipeServer?.Dispose(); } catch { }
                    _pipeServer = null;
                }
            }
            
            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}




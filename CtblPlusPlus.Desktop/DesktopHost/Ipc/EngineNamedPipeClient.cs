using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Application = System.Windows.Application;

using CtblPlusPlus.Core.Interfaces.Security;

namespace CtblPlusPlus.DesktopHost.Ipc;

public class EngineNamedPipeClient
{
    private NamedPipeClientStream? _pipeClient;
    private CancellationTokenSource _cts = new();
    private readonly Action<bool> _onSecurityStateChanged;
    private readonly Action<string>? _onStateChanged;
    private readonly NotifyIcon _trayIcon;
    private readonly IHmacProvider _hmac;

    public EngineNamedPipeClient(Action<bool> onSecurityStateChanged, Action<string>? onStateChanged, NotifyIcon trayIcon, IHmacProvider hmac)
    {
        _onSecurityStateChanged = onSecurityStateChanged;
        _onStateChanged = onStateChanged;
        _trayIcon = trayIcon; _hmac = hmac;
    }

    public void StartConnectionLoop()
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    _pipeClient = new NamedPipeClientStream(".", "CtblPlusPlusIpcPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await _pipeClient.ConnectAsync(_cts.Token);

                    using var reader = new StreamReader(_pipeClient);
                    while (_pipeClient.IsConnected && !_cts.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync(_cts.Token);
                        if (line == null) break;

                        if (line.StartsWith("SIG:")) { var sigParts = line.Split(':', 3); if (sigParts.Length == 3 && _hmac.ComputeHmac(sigParts[2]) == sigParts[1]) ProcessMessage(sigParts[2]); }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
                finally
                {
                    _pipeClient?.Dispose();
                    await Task.Delay(2000, _cts.Token); // reconnect delay
                }
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts.Cancel();
        _pipeClient?.Dispose();
    }

    private void ProcessMessage(string message)
    {
        var parts = message.Split('|');
        if (parts.Length < 2) return;

        string command = parts[0];

        if (command == "SecurityStatus" && parts.Length >= 2)
        {
            bool isSecure = bool.Parse(parts[1]);
            Application.Current.Dispatcher.Invoke(() => _onSecurityStateChanged(isSecure));
        }
        else if (command == "StateChanged" && parts.Length >= 2)
        {
            string scope = parts[1];
            Application.Current.Dispatcher.Invoke(() => _onStateChanged?.Invoke(scope));
        }
        else if (command == "Notification" && parts.Length >= 4)
        {
            string title = parts[1];
            string text = parts[2];
            string iconStr = parts[3];
            
            ToolTipIcon icon = ToolTipIcon.Info;
            if (Enum.TryParse(iconStr, out ToolTipIcon parsedIcon))
            {
                icon = parsedIcon;
            }

            _trayIcon.ShowBalloonTip(3000, title, text, icon);
        }
    }
}




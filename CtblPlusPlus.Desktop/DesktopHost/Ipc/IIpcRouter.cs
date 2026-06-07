using Microsoft.Web.WebView2.Core;

namespace CtblPlusPlus.DesktopHost.Ipc;

public interface IIpcRouter
{
    void HandleMessage(string message);
}



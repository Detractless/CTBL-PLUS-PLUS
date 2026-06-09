using System.Diagnostics;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.AppSystem;

/// <summary>
/// Concrete implementation of <see cref="IProcessInvoker"/> that spawns
/// hidden OS processes using System.Diagnostics.Process.
/// </summary>
public class WindowsProcessInvoker : IProcessInvoker
{
    public void RunHiddenCommand(string fileName, string arguments, int timeoutMs = 5000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        using var proc = Process.Start(psi);
        proc?.WaitForExit(timeoutMs);
    }
}



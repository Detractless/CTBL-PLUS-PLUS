namespace CtblPlusPlus.Core.Interfaces.System;

/// <summary>
/// Abstracts hidden process execution (e.g., icacls, takeown) so that
/// services can be tested without spawning real OS processes.
/// </summary>
public interface IProcessInvoker
{
    void RunHiddenCommand(string fileName, string arguments, int timeoutMs = 5000);
}



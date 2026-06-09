namespace CtblPlusPlus.Core.Interfaces.System;

/// <summary>
/// Determines whether this instance of the engine is running as a formally
/// installed application, as opposed to being run directly from a folder.
/// Used to gate aggressive system enforcement measures.
/// </summary>
public interface IInstallStateProvider
{
    /// <summary>
    /// Returns true if the engine Windows Service is registered in the SCM,
    /// indicating the app was formally installed.
    /// Failure mode is fail-armed: any exception other than "service not found"
    /// returns true to keep protections active.
    /// </summary>
    bool IsInstalled();
}



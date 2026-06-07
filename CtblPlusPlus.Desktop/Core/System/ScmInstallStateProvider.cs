using System;
using System.ServiceProcess;
using CtblPlusPlus.Core.Interfaces.System;

namespace CtblPlusPlus.Core.AppSystem;

/// <summary>
/// Determines install state by querying the Windows Service Control Manager.
/// The engine service "CTBL Queue Delay Engine" is registered by the installer
/// and is the canonical signal that the app is formally installed.
///
/// Failure mode is fail-armed: any exception other than InvalidOperationException
/// ("service not found") keeps protections active. Once IsInstalled() returns
/// true in a given process lifetime it is cached permanently, preventing a
/// mid-session "sc delete" bypass.
/// </summary>
public class ScmInstallStateProvider : IInstallStateProvider
{
    private const string ServiceName = "CTBL Queue Delay Engine";

    // Cached positive result. Never cached as false — a transient SCM hiccup
    // at startup should not permanently disarm protections for the session.
    private bool _cachedTrue = false;

    public bool IsInstalled()
    {
        if (_cachedTrue) return true;

        try
        {
            using var sc = new ServiceController(ServiceName);
            // Accessing Status triggers the SCM lookup.
            // Throws InvalidOperationException if the service is not registered.
            var _ = sc.Status;
            _cachedTrue = true;
            return true;
        }
        catch (InvalidOperationException)
        {
            // Service does not exist in the SCM ? not installed.
            return false;
        }
        catch
        {
            // Any other failure (access denied, SCM unavailable, etc.)
            // ? fail-armed: cannot confirm uninstalled, keep protections active.
            return true;
        }
    }
}



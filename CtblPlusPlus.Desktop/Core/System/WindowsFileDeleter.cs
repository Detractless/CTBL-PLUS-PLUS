using System;
using System.IO;
using System.Threading;
using CtblPlusPlus.Core.Interfaces.System;
using CtblPlusPlus.Core.Security.Lockdown;

namespace CtblPlusPlus.Core.AppSystem;

/// <summary>
/// Concrete implementation of <see cref="IFileDeleter"/> that performs
/// a 5-attempt deletion with linear back-off for locked files.
/// </summary>
public class WindowsFileDeleter : IFileDeleter
{
    /// <summary>
    /// Attempts to delete a file up to 5 times with increasing back-off.
    /// Uses Thread.Sleep intentionally — this method is called from
    /// FileSystemWatcher thread-pool callbacks where introducing
    /// async void would risk unobserved exceptions and re-entrancy.
    /// </summary>
    public void DeleteWithRetry(string filePath)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return;
                }
            }
            catch (IOException)
            {
                // File might be locked by the OS write buffer, back off and retry
                Thread.Sleep(100 * (i + 1));
            }
            catch (Exception ex)
            {
                LockdownLogger.Log("Lockdown.FileDeleter", $"Delete error for {filePath}: {ex.Message}");
                break;
            }
        }
    }
}



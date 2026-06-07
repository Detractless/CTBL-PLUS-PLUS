namespace CtblPlusPlus.Core.Interfaces.System;

/// <summary>
/// Abstracts file deletion with retry logic so that consumers
/// (ScorchedEarthPurgeService, FileSystemWatchdogService) can be
/// tested without touching the real filesystem.
/// </summary>
public interface IFileDeleter
{
    void DeleteWithRetry(string filePath);
}



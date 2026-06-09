using System;
using System.Collections.Generic;
using System.Threading;
using CtblPlusPlus.Core.Models;
using CtblPlusPlus.Core.Security;
using CtblPlusPlus.Core.Persistence; // Ensure DatabaseClient uses are covered if it's there

using CtblPlusPlus.Core.AppSystem;
using CtblPlusPlus.Core.Communication;

namespace CtblPlusPlus.Core.Domain.Queue;

public class QueueBatchContext
{
    private readonly DatabaseClient _DatabaseClient;
    private readonly CtblCliClient _cliService;
    
    private CtblRoot? _cachedState;
    private bool _isLoaded;

    public string LogPath { get; }
    public bool CtblStateModified { get; private set; }
    public Dictionary<string, string> BlocksToStart { get; } = new();

    public QueueBatchContext(string logPath, DatabaseClient DatabaseClient, CtblCliClient cliService)
    {
        LogPath = logPath;
        _DatabaseClient = DatabaseClient;
        _cliService = cliService;
    }

    public CtblRoot GetCtblState()
    {
        if (_isLoaded)
        {
            return _cachedState ?? throw new InvalidOperationException("CTBL DB state could not be loaded.");
        }

        Log($"[{DateTime.UtcNow:O}] Force killing CTService before DB read...\n");
        _cliService.KillService();
        Thread.Sleep(500);

        for (int i = 0; i < 5; i++)
        {
            try
            {
                _cachedState = _DatabaseClient.GetDbState();
                _isLoaded = true;
                return _cachedState;
            }
            catch (Exception ex) when (ex.Message.Contains("lock") || ex.Message.Contains("busy"))
            {
                if (i == 4) throw;
                Log($"[{DateTime.UtcNow:O}] SQLite Lock detected, retrying DB read... ({i + 1}/5)\n");
                Thread.Sleep(500);
            }
        }

        throw new InvalidOperationException("Unreachable but satisfies compiler");
    }

    public void MarkCtblModified()
    {
        CtblStateModified = true;
    }

    public void RequestBlockRestart(string blockName)
    {
        BlocksToStart[blockName] = Guid.NewGuid().ToString("N");
    }

    public void Log(string message)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                System.IO.File.AppendAllText(LogPath, message);
                return;
            }
            catch (System.IO.IOException)
            {
                Thread.Sleep(50);
            }
        }
    }
}



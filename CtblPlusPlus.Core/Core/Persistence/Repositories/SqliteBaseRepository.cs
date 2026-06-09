using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CtblPlusPlus.Core.Persistence.Repositories;

public abstract class SqliteBaseRepository
{
    protected readonly string _dbPath;

    protected SqliteBaseRepository()
    {
        string dirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblPlusPlus");
        if (!Directory.Exists(dirPath))
        {
            Directory.CreateDirectory(dirPath);
        }
        _dbPath = Path.Combine(dirPath, "ctblplusplus.db");
        
        // Auto-migrate legacy database if it exists and the new one doesn't
        string legacyDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CtblQueueDelay", "ctblqueuedelay.db");
        if (!File.Exists(_dbPath) && File.Exists(legacyDbPath))
        {
            try
            {
                File.Copy(legacyDbPath, _dbPath);
            }
            catch { /* Best effort */ }
        }

        EnsureDatabaseCreated();
    }

    protected string GetConnectionString()
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    private void EnsureDatabaseCreated()
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS DelayQueue (
                    Id TEXT PRIMARY KEY,
                    BlockName TEXT NOT NULL,
                    TargetUrl TEXT NOT NULL,
                    RequestedAt DATETIME NOT NULL,
                    UnlockAt DATETIME NOT NULL,
                    Status TEXT NOT NULL,
                    Signature TEXT DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS Settings (
                    Key TEXT PRIMARY KEY,
                    Value TEXT NOT NULL,
                    Signature TEXT DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS AuditLog (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BlockName TEXT NOT NULL,
                    TargetUrl TEXT NOT NULL,
                    Action TEXT NOT NULL,
                    Timestamp DATETIME NOT NULL
                );
                CREATE TABLE IF NOT EXISTS AppRegistry (
                    Id TEXT PRIMARY KEY,
                    ExePath TEXT NOT NULL UNIQUE,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    Publisher TEXT NOT NULL DEFAULT '',
                    Status TEXT NOT NULL DEFAULT 'Detected',
                    SignatureStatus TEXT NOT NULL DEFAULT 'Unknown',
                    FirstSeenUtc TEXT NOT NULL
                );
            ";
            command.ExecuteNonQuery();

            // Migration check
            try 
            {
                command.CommandText = "ALTER TABLE DelayQueue ADD COLUMN Signature TEXT DEFAULT '';";
                command.ExecuteNonQuery();
            } catch { }
        }
    }
}



using Microsoft.Data.Sqlite;

namespace CtblPlusPlus.Core.Persistence.Repositories;

public class AppRegistryMigrator
{
    private static bool _migrated = false;
    private static readonly object _migrationLock = new();

    public void EnsureMigrated(string connectionString)
    {
        if (_migrated) return;
        lock (_migrationLock)
        {
            if (_migrated) return;

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // 1. Drop legacy AppControlRules table
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "DROP TABLE IF EXISTS AppControlRules;";
                cmd.ExecuteNonQuery();
            }

            // 2. Ensure AppRegistry table exists
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS AppRegistry (
                        Id TEXT PRIMARY KEY,
                        ExePath TEXT UNIQUE NOT NULL,
                        DisplayName TEXT NOT NULL DEFAULT '',
                        Publisher TEXT NOT NULL DEFAULT '',
                        Status TEXT NOT NULL DEFAULT 'Detected',
                        SignatureStatus TEXT NOT NULL DEFAULT 'Unknown',
                        FirstSeenUtc TEXT NOT NULL,
                        IsColdTurkeyInjected INTEGER NOT NULL DEFAULT 0
                    );
                ";
                cmd.ExecuteNonQuery();
            }

            // 3. Add IsColdTurkeyInjected column if missing (safe ALTER for existing DBs)
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "ALTER TABLE AppRegistry ADD COLUMN IsColdTurkeyInjected INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists — expected on subsequent runs
            }

            _migrated = true;
        }
    }
}



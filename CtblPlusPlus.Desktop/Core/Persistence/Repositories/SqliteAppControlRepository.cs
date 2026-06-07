using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using CtblPlusPlus.Core.Interfaces.Data;
// AppRegistryMigrator handles schema migration; SqliteConnection is still used by CRUD methods below
using CtblPlusPlus.Models;

namespace CtblPlusPlus.Core.Persistence.Repositories;

/// <summary>
/// Unified SQLite repository operating exclusively on the AppRegistry table.
/// On first run, performs a clean migration: drops the legacy AppControlRules table,
/// clears stale AppRegistry entries, and adds the IsColdTurkeyInjected column.
/// </summary>
public class SqliteAppControlRepository : SqliteBaseRepository, IAppControlRepository
{
    public SqliteAppControlRepository()
    {
        new AppRegistryMigrator().EnsureMigrated(GetConnectionString());
    }

    // -- Core CRUD ------------------------------------------------------

    public void UpsertApp(string exePath, string displayName, string publisher)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO AppRegistry (Id, ExePath, DisplayName, Publisher, Status, SignatureStatus, FirstSeenUtc, IsColdTurkeyInjected)
            VALUES ($Id, $ExePath, $DisplayName, $Publisher, 'Detected', 'Unknown', $FirstSeenUtc, 0)
            ON CONFLICT(ExePath) DO UPDATE SET DisplayName = excluded.DisplayName, Publisher = excluded.Publisher;
        ";
        command.Parameters.AddWithValue("$Id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$ExePath", exePath);
        command.Parameters.AddWithValue("$DisplayName", displayName);
        command.Parameters.AddWithValue("$Publisher", publisher);
        command.Parameters.AddWithValue("$FirstSeenUtc", DateTime.UtcNow.ToString("o"));
        command.ExecuteNonQuery();
    }

    public void BulkUpsertApps(IEnumerable<(string ExePath, string DisplayName, string Publisher)> apps)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"
            INSERT INTO AppRegistry (Id, ExePath, DisplayName, Publisher, Status, SignatureStatus, FirstSeenUtc, IsColdTurkeyInjected)
            VALUES ($Id, $ExePath, $DisplayName, $Publisher, 'Detected', 'Unknown', $FirstSeenUtc, 0)
            ON CONFLICT(ExePath) DO UPDATE SET DisplayName = excluded.DisplayName, Publisher = excluded.Publisher;
        ";

        var idParam = command.Parameters.Add("$Id", SqliteType.Text);
        var pathParam = command.Parameters.Add("$ExePath", SqliteType.Text);
        var nameParam = command.Parameters.Add("$DisplayName", SqliteType.Text);
        var pubParam = command.Parameters.Add("$Publisher", SqliteType.Text);
        var timeParam = command.Parameters.Add("$FirstSeenUtc", SqliteType.Text);

        string now = DateTime.UtcNow.ToString("o");

        foreach (var app in apps)
        {
            idParam.Value = Guid.NewGuid().ToString();
            pathParam.Value = app.ExePath;
            nameParam.Value = app.DisplayName;
            pubParam.Value = app.Publisher;
            timeParam.Value = now;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public List<AppRegistryEntry> GetAllApps()
    {
        var apps = new List<AppRegistryEntry>();
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ExePath, DisplayName, Publisher, Status, FirstSeenUtc, IsColdTurkeyInjected FROM AppRegistry ORDER BY DisplayName;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            apps.Add(ReadEntry(reader));
        }
        return apps;
    }

    public void DeleteAppsByIds(IEnumerable<string> ids)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM AppRegistry WHERE Id = $Id;";
        var idParam = command.Parameters.Add("$Id", SqliteType.Text);

        foreach (var id in ids)
        {
            idParam.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // -- Status Management ----------------------------------------------

    public void SetAppStatus(string id, string newStatus)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppRegistry SET Status = $Status WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Status", newStatus);
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    public void BulkSetAppStatus(IEnumerable<string> ids, string newStatus)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE AppRegistry SET Status = $Status WHERE Id = $Id;";
        var statusParam = command.Parameters.Add("$Status", SqliteType.Text);
        var idParam = command.Parameters.Add("$Id", SqliteType.Text);

        statusParam.Value = newStatus;
        foreach (var id in ids)
        {
            idParam.Value = id;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void SetColdTurkeyInjected(string id, bool injected)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AppRegistry SET IsColdTurkeyInjected = $Injected WHERE Id = $Id;";
        command.Parameters.AddWithValue("$Injected", injected ? 1 : 0);
        command.Parameters.AddWithValue("$Id", id);
        command.ExecuteNonQuery();
    }

    // -- Queries --------------------------------------------------------

    public bool IsPathAllowed(string exePath)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM AppRegistry WHERE ExePath = $Path AND Status = 'Allowed' LIMIT 1;";
        command.Parameters.AddWithValue("$Path", exePath);
        return command.ExecuteScalar() != null;
    }

    public bool AppExistsByPath(string exePath)
    {
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM AppRegistry WHERE ExePath = $ExePath LIMIT 1;";
        command.Parameters.AddWithValue("$ExePath", exePath);
        return command.ExecuteScalar() != null;
    }

    public HashSet<string> GetAllAppPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ExePath FROM AppRegistry;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            paths.Add(reader.GetString(0));
        }
        return paths;
    }

    public List<AppRegistryEntry> GetAppsByStatus(string status)
    {
        var apps = new List<AppRegistryEntry>();
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ExePath, DisplayName, Publisher, Status, FirstSeenUtc, IsColdTurkeyInjected FROM AppRegistry WHERE Status = $Status ORDER BY DisplayName;";
        command.Parameters.AddWithValue("$Status", status);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            apps.Add(ReadEntry(reader));
        }
        return apps;
    }

    public List<AppRegistryEntry> GetUnjectedBlockedApps()
    {
        var apps = new List<AppRegistryEntry>();
        using var connection = new SqliteConnection(GetConnectionString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ExePath, DisplayName, Publisher, Status, FirstSeenUtc, IsColdTurkeyInjected FROM AppRegistry WHERE Status = 'Blocked' AND IsColdTurkeyInjected = 0;";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            apps.Add(ReadEntry(reader));
        }
        return apps;
    }

    // -- Helpers --------------------------------------------------------

    private static AppRegistryEntry ReadEntry(SqliteDataReader reader)
    {
        return new AppRegistryEntry
        {
            Id = reader.GetString(0),
            ExePath = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Publisher = reader.GetString(3),
            Status = reader.GetString(4),
            FirstSeenUtc = reader.GetString(5),
            IsColdTurkeyInjected = reader.GetInt32(6) == 1
        };
    }
}



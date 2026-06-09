using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Models;

namespace CtblPlusPlus.Core.Persistence.Repositories;

public class SqliteAuditRepository : SqliteBaseRepository, IAuditRepository
{
    public void LogAction(string blockName, string targetUrl, string action)
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AuditLog (BlockName, TargetUrl, Action, Timestamp) VALUES ($BlockName, $TargetUrl, $Action, $Timestamp);";
            command.Parameters.AddWithValue("$BlockName", blockName);
            command.Parameters.AddWithValue("$TargetUrl", targetUrl);
            command.Parameters.AddWithValue("$Action", action);
            command.Parameters.AddWithValue("$Timestamp", DateTime.UtcNow);
            
            command.ExecuteNonQuery();
        }
    }

    public IEnumerable<AuditLogEntry> GetAuditLogs()
    {
        var logs = new List<AuditLogEntry>();
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BlockName, TargetUrl, Action, Timestamp FROM AuditLog ORDER BY Timestamp DESC LIMIT 100;";
            
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    logs.Add(new AuditLogEntry
                    {
                        Id = reader.GetInt32(0),
                        BlockName = reader.GetString(1),
                        TargetUrl = reader.GetString(2),
                        Action = reader.GetString(3),
                        Timestamp = reader.GetDateTime(4)
                    });
                }
            }
        }
        return logs;
    }
}



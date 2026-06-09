using System;
using Microsoft.Data.Sqlite;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;

namespace CtblPlusPlus.Core.Persistence.Repositories;

public class SqliteSettingsRepository : SqliteBaseRepository, ISettingsRepository
{
    private readonly IHmacProvider _hmacProvider;

    public SqliteSettingsRepository(IHmacProvider hmacProvider)
    {
        _hmacProvider = hmacProvider;
    }

    public string GetSetting(string key, string defaultValue = "")
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Value, Signature FROM Settings WHERE Key = $Key;";
            command.Parameters.AddWithValue("$Key", key);
            
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    string value = reader.GetString(0);
                    string signature = reader.IsDBNull(1) ? "" : reader.GetString(1);

                    string expectedSignature = _hmacProvider.ComputeHmac(key + value);
                    if (signature == expectedSignature)
                    {
                        return value;
                    }
                    else
                    {
                        // Security tampering logic preserved
                        if (key == "GlobalDelayHours") return "24.0"; 
                        return defaultValue;
                    }
                }
            }
            return defaultValue;
        }
    }

    public void SetSetting(string key, string value)
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Settings (Key, Value, Signature) 
                VALUES ($Key, $Value, $Signature) 
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value, Signature = excluded.Signature;
            ";
            command.Parameters.AddWithValue("$Key", key);
            command.Parameters.AddWithValue("$Value", value);
            command.Parameters.AddWithValue("$Signature", _hmacProvider.ComputeHmac(key + value));
            
            command.ExecuteNonQuery();
        }
    }
}



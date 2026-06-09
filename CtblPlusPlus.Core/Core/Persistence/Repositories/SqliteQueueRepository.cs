using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using CtblPlusPlus.Core.Interfaces.Data;
using CtblPlusPlus.Core.Interfaces.Security;
using CtblPlusPlus.Core.Models;

namespace CtblPlusPlus.Core.Persistence.Repositories;

public class SqliteQueueRepository : SqliteBaseRepository, IQueueRepository
{
    private readonly IHmacProvider _hmacProvider;

    public SqliteQueueRepository(IHmacProvider hmacProvider)
    {
        _hmacProvider = hmacProvider;
    }

    public void AddRequest(DelayRequest request)
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO DelayQueue (Id, BlockName, TargetUrl, RequestedAt, UnlockAt, Status, Signature)
                VALUES ($Id, $BlockName, $TargetUrl, $RequestedAt, $UnlockAt, $Status, $Signature);
            ";
            
            string payloadToSign = request.Id + request.TargetUrl + request.UnlockAt.ToString("o");
            string signature = _hmacProvider.ComputeHmac(payloadToSign);

            command.Parameters.AddWithValue("$Id", request.Id);
            command.Parameters.AddWithValue("$BlockName", request.BlockName);
            command.Parameters.AddWithValue("$TargetUrl", request.TargetUrl);
            command.Parameters.AddWithValue("$RequestedAt", request.RequestedAt.ToString("o"));
            command.Parameters.AddWithValue("$UnlockAt", request.UnlockAt.ToString("o"));
            command.Parameters.AddWithValue("$Status", request.Status);
            command.Parameters.AddWithValue("$Signature", signature);
            
            command.ExecuteNonQuery();
        }
    }
    public void BulkAddRequests(IEnumerable<DelayRequest> requests)
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
                    INSERT INTO DelayQueue (Id, BlockName, TargetUrl, RequestedAt, UnlockAt, Status, Signature)
                    VALUES ($Id, $BlockName, $TargetUrl, $RequestedAt, $UnlockAt, $Status, $Signature);
                ";

                var idParam = command.Parameters.Add("$Id", SqliteType.Text);
                var blockParam = command.Parameters.Add("$BlockName", SqliteType.Text);
                var urlParam = command.Parameters.Add("$TargetUrl", SqliteType.Text);
                var reqAtParam = command.Parameters.Add("$RequestedAt", SqliteType.Text);
                var unlockAtParam = command.Parameters.Add("$UnlockAt", SqliteType.Text);
                var statusParam = command.Parameters.Add("$Status", SqliteType.Text);
                var sigParam = command.Parameters.Add("$Signature", SqliteType.Text);

                foreach (var req in requests)
                {
                    string payloadToSign = req.Id + req.TargetUrl + req.UnlockAt.ToString("o");
                    string signature = _hmacProvider.ComputeHmac(payloadToSign);

                    idParam.Value = req.Id;
                    blockParam.Value = req.BlockName;
                    urlParam.Value = req.TargetUrl;
                    reqAtParam.Value = req.RequestedAt.ToString("o");
                    unlockAtParam.Value = req.UnlockAt.ToString("o");
                    statusParam.Value = req.Status;
                    sigParam.Value = signature;

                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
        }
    }

    public List<DelayRequest> GetPendingRequests()
    {
        return GetRequestsByStatus("Pending");
    }

    public List<DelayRequest> GetInjectedRequests()
    {
        return GetRequestsByStatus("Injected");
    }

    private List<DelayRequest> GetRequestsByStatus(string status)
    {
        var requests = new List<DelayRequest>();
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, BlockName, TargetUrl, RequestedAt, UnlockAt, Status, Signature FROM DelayQueue WHERE Status = $Status ORDER BY UnlockAt ASC;";
            command.Parameters.AddWithValue("$Status", status);
            
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    requests.Add(new DelayRequest
                    {
                        Id = reader.GetString(0),
                        BlockName = reader.GetString(1),
                        TargetUrl = reader.GetString(2),
                        RequestedAt = DateTime.Parse(reader.GetString(3), null, global::System.Globalization.DateTimeStyles.RoundtripKind),
                        UnlockAt = DateTime.Parse(reader.GetString(4), null, global::System.Globalization.DateTimeStyles.RoundtripKind),
                        Status = reader.GetString(5),
                        Signature = reader.IsDBNull(6) ? "" : reader.GetString(6)
                    });
                }
            }
        }
        return requests;
    }

    public void UpdateRequestStatus(string id, string newStatus)
    {
        using (var connection = new SqliteConnection(GetConnectionString()))
        {
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE DelayQueue SET Status = $Status WHERE Id = $Id;";
            command.Parameters.AddWithValue("$Status", newStatus);
            command.Parameters.AddWithValue("$Id", id);
            command.ExecuteNonQuery();
        }
    }
}



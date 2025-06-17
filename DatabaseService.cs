using System;
using Microsoft.Data.Sqlite;
using Serilog;

namespace InternetSupportBot
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService(string connectionString)
        {
            _connectionString = connectionString;
            SetupDatabase();
        }

        private void SetupDatabase()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Users (
                    ChatId INTEGER PRIMARY KEY,
                    ContractNumber TEXT,
                    Address TEXT
                )";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Tickets (
                    TicketId TEXT PRIMARY KEY,
                    ChatId INTEGER,
                    ProblemType TEXT,
                    ProblemDetails TEXT,
                    Status TEXT,
                    Response TEXT
                )";
            command.ExecuteNonQuery();

            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ChatId INTEGER,
                    Action TEXT,
                    Timestamp TEXT
                )";
            command.ExecuteNonQuery();
        }

        public void LogAction(long chatId, string action)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Logs (ChatId, Action, Timestamp) VALUES ($chatId, $action, $timestamp)";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$action", action);
            command.Parameters.AddWithValue("$timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.ExecuteNonQuery();
            Log.Information($"ChatId: {chatId}, Action: {action}, Time: {DateTime.Now}");
        }

        public bool CheckUserExists(long chatId, string? contractNumber = null, string? address = null)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            if (contractNumber != null)
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE ContractNumber = $contractNumber";
            else
                command.CommandText = "SELECT COUNT(*) FROM Users WHERE Address = $address";
            command.Parameters.AddWithValue(contractNumber != null ? "$contractNumber" : "$address", contractNumber ?? address ?? "");
            var count = Convert.ToInt32(command.ExecuteScalar());
            return count > 0;
        }

        public void SaveUser(long chatId, string? contractNumber, string? address)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO Users (ChatId, ContractNumber, Address) VALUES ($chatId, $contractNumber, $address)";
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$contractNumber", contractNumber ?? "");
            command.Parameters.AddWithValue("$address", address ?? "");
            command.ExecuteNonQuery();
        }

        public void SaveTicket(long chatId, string ticketId, string? problemType, string? problemDetails, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Tickets (TicketId, ChatId, ProblemType, ProblemDetails, Status) VALUES ($ticketId, $chatId, $problemType, $problemDetails, $status)";
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.Parameters.AddWithValue("$chatId", chatId);
            command.Parameters.AddWithValue("$problemType", problemType ?? "");
            command.Parameters.AddWithValue("$problemDetails", problemDetails ?? "");
            command.Parameters.AddWithValue("$status", status);
            command.ExecuteNonQuery();
        }

        public void UpdateTicketStatus(string ticketId, string status)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Tickets SET Status = $status WHERE TicketId = $ticketId";
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.ExecuteNonQuery();
        }

        public void UpdateTicketResponse(string ticketId, string response)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Tickets SET Response = $response, Status = 'answered' WHERE TicketId = $ticketId";
            command.Parameters.AddWithValue("$response", response);
            command.Parameters.AddWithValue("$ticketId", ticketId);
            command.ExecuteNonQuery();
        }
    }
}
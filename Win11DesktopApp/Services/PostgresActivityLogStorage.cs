using System;
using System.Collections.Generic;
using Npgsql;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresActivityLogStorage : IActivityLogStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresActivityLogStorage(AppSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public LocalDbMigrationResult MigrateActivityLogIfNeeded(string jsonPath, IReadOnlyList<ActivityLogEntry> sourceEntries)
        {
            return new LocalDbMigrationResult
            {
                WasMigrationAttempted = false,
                IsSuccessful = true,
                RecordsFound = sourceEntries?.Count ?? 0,
                RecordsImported = 0,
                Message = "PostgreSQL activity log is populated by the SQLite to PostgreSQL migration wizard."
            };
        }

        public void InsertActivityLog(ActivityLogEntry entry)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertActivityLog(connection, transaction, entry);
            TrimActivityLog(connection, transaction, 5000);
            transaction.Commit();
        }

        public List<ActivityLogEntry> GetAllActivityLogs()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, timestamp, action_type, category, firm_name, employee_name, employee_folder,
       description, old_value, new_value, details, related_operation_id, actor_name
FROM app.activity_log
ORDER BY timestamp DESC, id DESC;";

            using var reader = command.ExecuteReader();
            var result = new List<ActivityLogEntry>();
            while (reader.Read())
            {
                result.Add(new ActivityLogEntry
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Timestamp = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    ActionType = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    Category = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    FirmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    EmployeeName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    EmployeeFolder = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Description = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    OldValue = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    NewValue = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    Details = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    RelatedOperationId = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                    ActorName = reader.IsDBNull(12) ? string.Empty : reader.GetString(12)
                });
            }

            return result;
        }

        public void RemoveActivityLogEntries(string originalFolder, string deletedFolder, string employeeName, string firmName)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM app.activity_log
WHERE (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder))
   OR (
        COALESCE(employee_folder, '') = ''
        AND lower(employee_name) = lower(@employeeName)
        AND lower(firm_name) = lower(@firmName)
      );";
            command.Parameters.AddWithValue("originalFolder", originalFolder ?? string.Empty);
            command.Parameters.AddWithValue("deletedFolder", deletedFolder ?? string.Empty);
            command.Parameters.AddWithValue("employeeName", employeeName ?? string.Empty);
            command.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
            command.ExecuteNonQuery();
        }

        public void ClearActivityLogs()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app.activity_log;";
            command.ExecuteNonQuery();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
                return;

            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE IF NOT EXISTS app.activity_log (
    id TEXT PRIMARY KEY,
    timestamp TEXT NOT NULL,
    action_type TEXT NOT NULL,
    category TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    description TEXT NOT NULL,
    old_value TEXT NOT NULL,
    new_value TEXT NOT NULL,
    details TEXT NOT NULL,
    related_operation_id TEXT NOT NULL,
    actor_name TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_pg_activity_log_timestamp ON app.activity_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_pg_activity_log_employee_folder ON app.activity_log(employee_folder);
CREATE INDEX IF NOT EXISTS idx_pg_activity_log_firm_employee ON app.activity_log(firm_name, employee_name);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private static void InsertActivityLog(NpgsqlConnection connection, NpgsqlTransaction transaction, ActivityLogEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app.activity_log (
    id, timestamp, action_type, category, firm_name, employee_name, employee_folder,
    description, old_value, new_value, details, related_operation_id, actor_name
) VALUES (
    @id, @timestamp, @actionType, @category, @firmName, @employeeName, @employeeFolder,
    @description, @oldValue, @newValue, @details, @relatedOperationId, @actorName
)
ON CONFLICT(id) DO UPDATE SET
    timestamp = EXCLUDED.timestamp,
    action_type = EXCLUDED.action_type,
    category = EXCLUDED.category,
    firm_name = EXCLUDED.firm_name,
    employee_name = EXCLUDED.employee_name,
    employee_folder = EXCLUDED.employee_folder,
    description = EXCLUDED.description,
    old_value = EXCLUDED.old_value,
    new_value = EXCLUDED.new_value,
    details = EXCLUDED.details,
    related_operation_id = EXCLUDED.related_operation_id,
    actor_name = EXCLUDED.actor_name;";
            command.Parameters.AddWithValue("id", entry.Id ?? Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("timestamp", entry.Timestamp ?? string.Empty);
            command.Parameters.AddWithValue("actionType", entry.ActionType ?? string.Empty);
            command.Parameters.AddWithValue("category", entry.Category ?? string.Empty);
            command.Parameters.AddWithValue("firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("employeeName", entry.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("description", entry.Description ?? string.Empty);
            command.Parameters.AddWithValue("oldValue", entry.OldValue ?? string.Empty);
            command.Parameters.AddWithValue("newValue", entry.NewValue ?? string.Empty);
            command.Parameters.AddWithValue("details", entry.Details ?? string.Empty);
            command.Parameters.AddWithValue("relatedOperationId", entry.RelatedOperationId ?? string.Empty);
            command.Parameters.AddWithValue("actorName", entry.ActorName ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private static void TrimActivityLog(NpgsqlConnection connection, NpgsqlTransaction transaction, int maxEntries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM app.activity_log
WHERE id IN (
    SELECT id
    FROM app.activity_log
    ORDER BY timestamp DESC, id DESC
    OFFSET @maxEntries
);";
            command.Parameters.AddWithValue("maxEntries", maxEntries);
            command.ExecuteNonQuery();
        }

        private NpgsqlConnection OpenConnection()
        {
            var settings = _settingsService.Settings;
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(settings.PostgresHost) ? "localhost" : settings.PostgresHost.Trim(),
                Port = settings.PostgresPort <= 0 ? 5432 : settings.PostgresPort,
                Database = string.IsNullOrWhiteSpace(settings.PostgresDatabase) ? "agency_db" : settings.PostgresDatabase.Trim(),
                Username = string.IsNullOrWhiteSpace(settings.PostgresUsername) ? "postgres" : settings.PostgresUsername.Trim(),
                Password = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword),
                Timeout = 10,
                CommandTimeout = 30,
                Pooling = true
            };

            var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }
    }
}

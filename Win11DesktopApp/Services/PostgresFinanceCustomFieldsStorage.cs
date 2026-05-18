using System;
using System.Collections.Generic;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresFinanceCustomFieldsStorage : IFinanceCustomFieldsStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresFinanceCustomFieldsStorage(AppSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public List<CustomSalaryField> GetCustomSalaryFields()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, name, operation, firm_name, order_index
FROM app.custom_salary_fields
ORDER BY firm_name, order_index, id;";

            using var reader = command.ExecuteReader();
            var result = new List<CustomSalaryField>();
            while (reader.Read())
            {
                result.Add(new CustomSalaryField
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Operation = reader.IsDBNull(2) ? FieldOperation.Subtract : (FieldOperation)reader.GetInt32(2),
                    FirmName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Order = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
                });
            }

            return result;
        }

        public void UpsertCustomSalaryField(CustomSalaryField field)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertCustomSalaryField(connection, transaction, field);
            transaction.Commit();
        }

        public void ReplaceCustomSalaryFields(IReadOnlyList<CustomSalaryField> fields)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM app.custom_salary_fields;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var field in fields ?? Array.Empty<CustomSalaryField>())
                UpsertCustomSalaryField(connection, transaction, field);

            transaction.Commit();
        }

        public void DeleteCustomSalaryField(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app.custom_salary_fields WHERE id = @id;";
            command.Parameters.AddWithValue("id", fieldId);
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

CREATE TABLE IF NOT EXISTS app.custom_salary_fields (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    operation INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    order_index INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_pg_custom_salary_fields_firm_order ON app.custom_salary_fields(firm_name, order_index);
CREATE INDEX IF NOT EXISTS idx_pg_custom_salary_fields_order ON app.custom_salary_fields(order_index);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private static void UpsertCustomSalaryField(NpgsqlConnection connection, NpgsqlTransaction transaction, CustomSalaryField field)
        {
            var normalizedId = string.IsNullOrWhiteSpace(field.Id) ? Guid.NewGuid().ToString() : field.Id;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app.custom_salary_fields (
    id, name, operation, firm_name, order_index
) VALUES (
    @id, @name, @operation, @firmName, @order
)
ON CONFLICT(id) DO UPDATE SET
    name = EXCLUDED.name,
    operation = EXCLUDED.operation,
    firm_name = EXCLUDED.firm_name,
    order_index = EXCLUDED.order_index;";
            command.Parameters.AddWithValue("id", normalizedId);
            command.Parameters.AddWithValue("name", field.Name ?? string.Empty);
            command.Parameters.AddWithValue("operation", (int)field.Operation);
            command.Parameters.AddWithValue("firmName", field.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("order", field.Order);
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

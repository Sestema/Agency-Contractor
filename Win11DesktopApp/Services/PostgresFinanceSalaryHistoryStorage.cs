using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresFinanceSalaryHistoryStorage : IFinanceSalaryHistoryStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresFinanceSalaryHistoryStorage(AppSettingsService settingsService, FolderService folderService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        }

        public void UpsertSalaryHistoryRecord(string employeeId, string employeeFolder, SalaryHistoryRecord record)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertSalaryHistoryRecord(connection, transaction, employeeId, employeeFolder, record);
            transaction.Commit();
        }

        public void DeleteSalaryHistoryRecord(string employeeId, string employeeFolder, int year, int month, string firmName)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM app.salary_history
WHERE year = @year
  AND month = @month
  AND lower(firm_name) = lower(@firmName)
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            command.ExecuteNonQuery();
        }

        public int DeleteSalaryHistoryForEmployee(string? employeeId, string? originalFolder, string? deletedFolder)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM app.salary_history
WHERE (@employeeId <> '' AND lower(COALESCE(employee_id, '')) = lower(@employeeId))
   OR (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder));";
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("originalFolder", ToPortablePath(originalFolder ?? string.Empty));
            command.Parameters.AddWithValue("deletedFolder", ToPortablePath(deletedFolder ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public int RemapEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            if (string.IsNullOrWhiteSpace(toFolder))
                return 0;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE app.salary_history
SET employee_folder = @toFolder,
    employee_id = CASE
        WHEN @employeeId <> '' AND (COALESCE(employee_id, '') = '' OR lower(employee_id) = lower(@employeeId))
            THEN @employeeId
        ELSE employee_id
    END
WHERE (
        (@employeeId <> '' AND lower(COALESCE(employee_id, '')) = lower(@employeeId))
        OR (@fromFolderA <> '' AND lower(employee_folder) = lower(@fromFolderA))
        OR (@fromFolderB <> '' AND lower(employee_folder) = lower(@fromFolderB))
      )
  AND lower(employee_folder) <> lower(@toFolder);";
            command.Parameters.AddWithValue("toFolder", ToPortablePath(toFolder));
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("fromFolderA", ToPortablePath(fromFolderA ?? string.Empty));
            command.Parameters.AddWithValue("fromFolderB", ToPortablePath(fromFolderB ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public List<SalaryHistoryRecord> GetSalaryHistory(string employeeId, string employeeFolder)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, paid_at, year, month, firm_name, full_name, hours_worked, hourly_rate, gross_salary,
       advance, net_salary, note, custom_values_json, custom_fields_json
FROM app.salary_history
WHERE ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY year DESC, month DESC, paid_at DESC, id DESC;";
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));

            using var reader = command.ExecuteReader();
            var result = new List<SalaryHistoryRecord>();
            while (reader.Read())
                result.Add(ReadSalaryHistoryRecord(reader));

            return result;
        }

        public int RemoveDuplicateSalaryHistoryRecords()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, paid_at, year, month, firm_name
FROM app.salary_history;";

            var rows = new List<SalaryHistoryDuplicateCleanupRow>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    rows.Add(new SalaryHistoryDuplicateCleanupRow
                    {
                        Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                        EmployeeId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        EmployeeFolder = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        PaidAt = ParseDateTime(reader.IsDBNull(3) ? string.Empty : reader.GetString(3)),
                        Year = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                        Month = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                        FirmName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6)
                    });
                }
            }

            var idsToRemove = SalaryHistoryDuplicateCleanup.GetDuplicateRecordIdsToRemove(rows);
            if (idsToRemove.Count == 0)
                return 0;

            using var transaction = connection.BeginTransaction();
            var removed = 0;
            foreach (var id in idsToRemove)
            {
                using var deleteCommand = connection.CreateCommand();
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM app.salary_history WHERE id = @id;";
                deleteCommand.Parameters.AddWithValue("id", id);
                removed += deleteCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return removed;
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

CREATE TABLE IF NOT EXISTS app.salary_history (
    id TEXT PRIMARY KEY,
    employee_id TEXT,
    employee_folder TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    full_name TEXT NOT NULL,
    paid_at TEXT NOT NULL,
    hours_worked TEXT NOT NULL DEFAULT '0',
    hourly_rate TEXT NOT NULL DEFAULT '0',
    gross_salary TEXT NOT NULL DEFAULT '0',
    advance TEXT NOT NULL DEFAULT '0',
    net_salary TEXT NOT NULL DEFAULT '0',
    note TEXT NOT NULL DEFAULT '',
    custom_values_json TEXT NOT NULL DEFAULT '{}',
    custom_fields_json TEXT NOT NULL DEFAULT '[]'
);

CREATE INDEX IF NOT EXISTS idx_pg_salary_history_employee ON app.salary_history(employee_id);
CREATE INDEX IF NOT EXISTS idx_pg_salary_history_folder ON app.salary_history(employee_folder);
CREATE INDEX IF NOT EXISTS idx_pg_salary_history_period ON app.salary_history(year, month);
CREATE INDEX IF NOT EXISTS idx_pg_salary_history_firm ON app.salary_history(firm_name);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pg_salary_history_dedup ON app.salary_history(
    COALESCE(employee_id, ''), employee_folder, year, month, firm_name
);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private void UpsertSalaryHistoryRecord(NpgsqlConnection connection, NpgsqlTransaction transaction, string employeeId, string employeeFolder, SalaryHistoryRecord record)
        {
            var recordId = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString() : record.Id;
            var portableEmployeeFolder = ToPortablePath(employeeFolder);

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = @"
DELETE FROM app.salary_history
WHERE year = @year
  AND month = @month
  AND lower(btrim(firm_name)) = lower(btrim(@firmName))
  AND ((@employeeId <> '' AND lower(COALESCE(employee_id, '')) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
  AND id <> @id;";
                deleteCommand.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
                deleteCommand.Parameters.AddWithValue("employeeFolder", portableEmployeeFolder);
                deleteCommand.Parameters.AddWithValue("year", record.Year);
                deleteCommand.Parameters.AddWithValue("month", record.Month);
                deleteCommand.Parameters.AddWithValue("firmName", record.FirmName ?? string.Empty);
                deleteCommand.Parameters.AddWithValue("id", recordId);
                deleteCommand.ExecuteNonQuery();
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app.salary_history (
    id, employee_id, employee_folder, year, month, firm_name, full_name, paid_at,
    hours_worked, hourly_rate, gross_salary, advance, net_salary, note, custom_values_json, custom_fields_json
) VALUES (
    @id, @employeeId, @employeeFolder, @year, @month, @firmName, @fullName, @paidAt,
    @hoursWorked, @hourlyRate, @grossSalary, @advance, @netSalary, @note, @customValuesJson, @customFieldsJson
)
ON CONFLICT(id) DO UPDATE SET
    employee_id = EXCLUDED.employee_id,
    employee_folder = EXCLUDED.employee_folder,
    year = EXCLUDED.year,
    month = EXCLUDED.month,
    firm_name = EXCLUDED.firm_name,
    full_name = EXCLUDED.full_name,
    paid_at = EXCLUDED.paid_at,
    hours_worked = EXCLUDED.hours_worked,
    hourly_rate = EXCLUDED.hourly_rate,
    gross_salary = EXCLUDED.gross_salary,
    advance = EXCLUDED.advance,
    net_salary = EXCLUDED.net_salary,
    note = EXCLUDED.note,
    custom_values_json = EXCLUDED.custom_values_json,
    custom_fields_json = EXCLUDED.custom_fields_json;";
            command.Parameters.AddWithValue("id", recordId);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", portableEmployeeFolder);
            command.Parameters.AddWithValue("year", record.Year);
            command.Parameters.AddWithValue("month", record.Month);
            command.Parameters.AddWithValue("firmName", record.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("fullName", record.FullName ?? string.Empty);
            command.Parameters.AddWithValue("paidAt", record.PaidAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("hoursWorked", ToInvariant(record.HoursWorked));
            command.Parameters.AddWithValue("hourlyRate", ToInvariant(record.HourlyRate));
            command.Parameters.AddWithValue("grossSalary", ToInvariant(record.GrossSalary));
            command.Parameters.AddWithValue("advance", ToInvariant(record.Advance));
            command.Parameters.AddWithValue("netSalary", ToInvariant(record.NetSalary));
            command.Parameters.AddWithValue("note", record.Note ?? string.Empty);
            command.Parameters.AddWithValue("customValuesJson", JsonSerializer.Serialize(record.CustomValues ?? new Dictionary<string, decimal>()));
            command.Parameters.AddWithValue("customFieldsJson", JsonSerializer.Serialize(record.CustomFields ?? new List<CustomFieldSnapshot>()));
            command.ExecuteNonQuery();
        }

        private static SalaryHistoryRecord ReadSalaryHistoryRecord(NpgsqlDataReader reader)
        {
            var customValuesJson = reader.IsDBNull(12) ? "{}" : reader.GetString(12);
            var customFieldsJson = reader.IsDBNull(13) ? "[]" : reader.GetString(13);
            var customValues = JsonSerializer.Deserialize<Dictionary<string, decimal>>(customValuesJson)
                               ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var customFields = JsonSerializer.Deserialize<List<CustomFieldSnapshot>>(customFieldsJson)
                               ?? new List<CustomFieldSnapshot>();

            return new SalaryHistoryRecord
            {
                Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                PaidAt = ParseDateTime(reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
                Year = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Month = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                FirmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                FullName = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                HoursWorked = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                HourlyRate = reader.IsDBNull(7) ? 0m : ParseDecimal(reader.GetString(7)),
                GrossSalary = reader.IsDBNull(8) ? 0m : ParseDecimal(reader.GetString(8)),
                Advance = reader.IsDBNull(9) ? 0m : ParseDecimal(reader.GetString(9)),
                NetSalary = reader.IsDBNull(10) ? 0m : ParseDecimal(reader.GetString(10)),
                Note = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                CustomValues = customValues,
                CustomFields = customFields
            };
        }

        private NpgsqlConnection OpenConnection()
            => PostgresConnectionFactory.OpenConnection(_settingsService);

        private string ToPortablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalizedPath = NormalizeFullPath(path);
            var rootPath = NormalizeFullPath(_folderService.RootPath);
            if (string.IsNullOrWhiteSpace(rootPath))
                return normalizedPath;

            if (!Path.IsPathRooted(normalizedPath))
                return normalizedPath;

            return IsPathUnderRoot(normalizedPath, rootPath)
                ? Path.GetRelativePath(rootPath, normalizedPath)
                : normalizedPath;
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            try
            {
                var normalizedPath = EnsureTrailingSeparator(NormalizeFullPath(path));
                var normalizedRoot = EnsureTrailingSeparator(NormalizeFullPath(rootPath));
                return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.EndsWith(Path.DirectorySeparatorChar)
                || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string ToInvariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static DateTime ParseDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DateTime.Now;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.Now;
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            LoggingService.LogWarning("PostgresFinanceSalaryHistoryStorage.ParseDecimal", $"Failed to parse decimal value '{value}'. Using 0.");
            return 0m;
        }
    }
}

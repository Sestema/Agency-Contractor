using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class SalaryDbService
    {
        private const int CurrentSchemaVersion = 1;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private readonly HashSet<string> _initializedDatabases = new(StringComparer.OrdinalIgnoreCase);

        public SalaryDbService(FolderService folderService)
        {
            _folderService = folderService;
        }

        public string SalaryDbFolder => _folderService.GetSalaryDbFolder();

        public string GetMonthDbPath(int year, int month)
        {
            var folder = SalaryDbFolder;
            return string.IsNullOrWhiteSpace(folder)
                ? string.Empty
                : Path.Combine(folder, $"salary_{year:D4}_{month:D2}.db");
        }

        public bool MonthDbExists(int year, int month)
        {
            var path = GetMonthDbPath(year, month);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        public IEnumerable<(int year, int month, string path)> EnumerateMonthDatabases()
        {
            var folder = SalaryDbFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                yield break;

            foreach (var path in Directory.GetFiles(folder, "salary_????_??.db").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var parts = fileName.Split('_');
                if (parts.Length != 3)
                    continue;

                if (!int.TryParse(parts[1], out var year) || !int.TryParse(parts[2], out var month))
                    continue;

                yield return (year, month, path);
            }
        }

        public SqliteConnection OpenMonthConnection(int year, int month)
        {
            var dbPath = GetMonthDbPath(year, month);
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("Salary SQLite path is not available.");

            EnsureMonthSchema(dbPath);

            var connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        public void ReplaceMonthData(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            using var connection = OpenMonthConnection(year, month);
            using var transaction = connection.BeginTransaction();

            using (var deleteEntries = connection.CreateCommand())
            {
                deleteEntries.Transaction = transaction;
                deleteEntries.CommandText = "DELETE FROM salary_entries;";
                deleteEntries.ExecuteNonQuery();
            }

            using (var deleteExpenses = connection.CreateCommand())
            {
                deleteExpenses.Transaction = transaction;
                deleteExpenses.CommandText = "DELETE FROM salary_expenses;";
                deleteExpenses.ExecuteNonQuery();
            }

            foreach (var entry in entries)
                InsertSalaryEntry(connection, transaction, year, month, entry);

            foreach (var expense in expenses)
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
        }

        public (int EntryCount, int ExpenseCount, decimal SavedNetSalaryTotal, Dictionary<string, int> StatusCounts) GetMonthValidationSnapshot(int year, int month)
        {
            using var connection = OpenMonthConnection(year, month);

            using var entryCountCommand = connection.CreateCommand();
            entryCountCommand.CommandText = "SELECT COUNT(1) FROM salary_entries;";
            var entryCount = Convert.ToInt32(entryCountCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

            using var expenseCountCommand = connection.CreateCommand();
            expenseCountCommand.CommandText = "SELECT COUNT(1) FROM salary_expenses;";
            var expenseCount = Convert.ToInt32(expenseCountCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

            using var netCommand = connection.CreateCommand();
            netCommand.CommandText = "SELECT ifnull(SUM(CAST(saved_net_salary AS REAL)), 0) FROM salary_entries;";
            var totalNet = Convert.ToDecimal(netCommand.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);

            using var statusCommand = connection.CreateCommand();
            statusCommand.CommandText = @"
SELECT status, COUNT(1)
FROM salary_entries
GROUP BY status;";
            using var reader = statusCommand.ExecuteReader();
            var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read())
            {
                var status = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var count = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                statusCounts[status] = count;
            }

            return (entryCount, expenseCount, totalNet, statusCounts);
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadMonthPayments(int year, int month)
        {
            if (!MonthDbExists(year, month))
                return (new List<SalaryEntry>(), new List<FirmExpense>());

            using var connection = OpenMonthConnection(year, month);
            var entries = new List<SalaryEntry>();
            var expenses = new List<FirmExpense>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT employee_id, employee_folder, full_name, firm_name, hours_worked, hourly_rate, advance,
       saved_net_salary, status, note, color_tag, custom_values
FROM salary_entries
ORDER BY firm_name, full_name;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    entries.Add(ReadSalaryEntry(reader));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, firm_name, year, month, name, amount
FROM salary_expenses
ORDER BY firm_name, name;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    expenses.Add(ReadFirmExpense(reader));
            }

            return (entries, expenses);
        }

        public Dictionary<string, (decimal netSalary, bool paid)> GetSavedPaymentsForEmployee(
            string employeeFolder,
            string? employeeId,
            string? firmName,
            string beforeMonthKey)
        {
            var result = new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase);
            var normalizedEmployeeFolder = NormalizeEmployeePath(employeeFolder);

            foreach (var monthDb in EnumerateMonthDatabases())
            {
                var monthKey = $"{monthDb.year:D4}-{monthDb.month:D2}";
                if (string.Compare(monthKey, beforeMonthKey, StringComparison.Ordinal) >= 0)
                    continue;

                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT saved_net_salary, status
FROM salary_entries
WHERE (@firmName = '' OR lower(firm_name) = lower(@firmName))
  AND (
        (@employeeId <> '' AND ifnull(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR ((@employeeId = '' OR ifnull(employee_id, '') = '') AND lower(employee_folder) = lower(@employeeFolder))
      )
LIMIT 1;";
                command.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
                command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
                command.Parameters.AddWithValue("@employeeFolder", normalizedEmployeeFolder);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    continue;

                var netSalary = reader.IsDBNull(0) ? 0m : ParseDecimal(reader.GetString(0));
                var status = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                result.TryAdd(monthKey, (netSalary, string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase)));
            }

            return result;
        }

        public void UpdateHourlyRateForward(
            string? employeeId,
            string employeeFolder,
            string firmName,
            decimal newRate,
            string fromMonthKey,
            CancellationToken cancellationToken = default)
        {
            var normalizedEmployeeFolder = NormalizeEmployeePath(employeeFolder);
            foreach (var monthDb in EnumerateMonthDatabases())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var monthKey = $"{monthDb.year:D4}-{monthDb.month:D2}";
                if (string.Compare(monthKey, fromMonthKey, StringComparison.Ordinal) <= 0)
                    continue;

                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE salary_entries
SET hourly_rate = @hourlyRate,
    updated_at = @updatedAt
WHERE lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND ifnull(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR ((@employeeId = '' OR ifnull(employee_id, '') = '') AND lower(employee_folder) = lower(@employeeFolder))
      );";
                command.Parameters.AddWithValue("@hourlyRate", ToInvariant(newRate));
                command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
                command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
                command.Parameters.AddWithValue("@employeeFolder", normalizedEmployeeFolder);
                command.ExecuteNonQuery();
            }
        }

        public void SaveMonthPayments(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            ReplaceMonthData(year, month, entries, expenses);
        }

        private void EnsureMonthSchema(string dbPath)
        {
            lock (_initLock)
            {
                if (_initializedDatabases.Contains(dbPath))
                    return;

                var folder = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                using var connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS _meta (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS salary_entries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    firm_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    employee_id TEXT,
    employee_folder TEXT,
    full_name TEXT,
    hours_worked TEXT NOT NULL DEFAULT '0',
    hourly_rate TEXT NOT NULL DEFAULT '0',
    advance TEXT NOT NULL DEFAULT '0',
    saved_net_salary TEXT NOT NULL DEFAULT '0',
    status TEXT NOT NULL DEFAULT 'pending',
    note TEXT DEFAULT '',
    color_tag TEXT DEFAULT '',
    custom_values TEXT DEFAULT '{}',
    updated_at TEXT
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_se_unique ON salary_entries(firm_name, employee_folder);
CREATE INDEX IF NOT EXISTS idx_se_firm ON salary_entries(firm_name);
CREATE INDEX IF NOT EXISTS idx_se_employee_id ON salary_entries(employee_id);

CREATE TABLE IF NOT EXISTS salary_expenses (
    id TEXT PRIMARY KEY,
    firm_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    name TEXT DEFAULT '',
    amount TEXT NOT NULL DEFAULT '0'
);

CREATE INDEX IF NOT EXISTS idx_sexp_firm ON salary_expenses(firm_name);";
                command.ExecuteNonQuery();

                using var countCommand = connection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(1) FROM _meta;";
                var hasVersion = Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
                if (!hasVersion)
                {
                    using var insertVersion = connection.CreateCommand();
                    insertVersion.CommandText = "INSERT INTO _meta(version) VALUES (@version);";
                    insertVersion.Parameters.AddWithValue("@version", CurrentSchemaVersion);
                    insertVersion.ExecuteNonQuery();
                }

                _initializedDatabases.Add(dbPath);
            }
        }

        private static void InsertSalaryEntry(SqliteConnection connection, SqliteTransaction transaction, int year, int month, SalaryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary_entries (
    firm_name, year, month, employee_id, employee_folder, full_name,
    hours_worked, hourly_rate, advance, saved_net_salary, status, note, color_tag, custom_values, updated_at
) VALUES (
    @firmName, @year, @month, @employeeId, @employeeFolder, @fullName,
    @hoursWorked, @hourlyRate, @advance, @savedNetSalary, @status, @note, @colorTag, @customValues, @updatedAt
)
ON CONFLICT(firm_name, employee_folder) DO UPDATE SET
    employee_id = excluded.employee_id,
    full_name = excluded.full_name,
    hours_worked = excluded.hours_worked,
    hourly_rate = excluded.hourly_rate,
    advance = excluded.advance,
    saved_net_salary = excluded.saved_net_salary,
    status = excluded.status,
    note = excluded.note,
    color_tag = excluded.color_tag,
    custom_values = excluded.custom_values,
    updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("@firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);
            command.Parameters.AddWithValue("@employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("@fullName", entry.FullName ?? string.Empty);
            command.Parameters.AddWithValue("@hoursWorked", ToInvariant(entry.HoursWorked));
            command.Parameters.AddWithValue("@hourlyRate", ToInvariant(entry.HourlyRate));
            command.Parameters.AddWithValue("@advance", ToInvariant(entry.Advance));
            command.Parameters.AddWithValue("@savedNetSalary", ToInvariant(entry.SavedNetSalary));
            command.Parameters.AddWithValue("@status", entry.Status ?? string.Empty);
            command.Parameters.AddWithValue("@note", entry.Note ?? string.Empty);
            command.Parameters.AddWithValue("@colorTag", entry.ColorTag ?? string.Empty);
            command.Parameters.AddWithValue("@customValues", JsonSerializer.Serialize(entry.CustomValues ?? new Dictionary<string, decimal>()));
            command.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static void InsertSalaryExpense(SqliteConnection connection, SqliteTransaction transaction, int year, int month, FirmExpense expense)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary_expenses (
    id, firm_name, year, month, name, amount
) VALUES (
    @id, @firmName, @year, @month, @name, @amount
)
ON CONFLICT(id) DO UPDATE SET
    firm_name = excluded.firm_name,
    year = excluded.year,
    month = excluded.month,
    name = excluded.name,
    amount = excluded.amount;";

            command.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(expense.Id) ? Guid.NewGuid().ToString() : expense.Id);
            command.Parameters.AddWithValue("@firmName", expense.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);
            command.Parameters.AddWithValue("@name", expense.Name ?? string.Empty);
            command.Parameters.AddWithValue("@amount", ToInvariant(expense.Amount));
            command.ExecuteNonQuery();
        }

        private static SalaryEntry ReadSalaryEntry(SqliteDataReader reader)
        {
            var customValuesJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11);
            var customValues = JsonSerializer.Deserialize<Dictionary<string, decimal>>(customValuesJson)
                               ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            return new SalaryEntry
            {
                EmployeeId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                EmployeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                FullName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                FirmName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                HoursWorked = reader.IsDBNull(4) ? 0m : ParseDecimal(reader.GetString(4)),
                HourlyRate = reader.IsDBNull(5) ? 0m : ParseDecimal(reader.GetString(5)),
                Advance = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                SavedNetSalary = reader.IsDBNull(7) ? 0m : ParseDecimal(reader.GetString(7)),
                Status = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                Note = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                ColorTag = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                CustomValues = customValues
            };
        }

        private static FirmExpense ReadFirmExpense(SqliteDataReader reader)
        {
            return new FirmExpense
            {
                Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                FirmName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Year = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Month = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Name = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Amount = reader.IsDBNull(5) ? 0m : ParseDecimal(reader.GetString(5))
            };
        }

        private static decimal ParseDecimal(string value)
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0m;
        }

        private static string NormalizeEmployeePath(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        private static string ToInvariant(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

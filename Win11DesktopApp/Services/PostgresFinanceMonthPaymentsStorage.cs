using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresFinanceMonthPaymentsStorage : IFinanceMonthPaymentsStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresFinanceMonthPaymentsStorage(AppSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public bool MonthDbExists(int year, int month)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT EXISTS (
    SELECT 1 FROM salary.salary_entries WHERE source_year = @year AND source_month = @month
    UNION ALL
    SELECT 1 FROM salary.salary_expenses WHERE source_year = @year AND source_month = @month
);";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            return command.ExecuteScalar() is bool exists && exists;
        }

        public IEnumerable<(int year, int month, string path)> EnumerateMonthDatabases()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT source_year, source_month
FROM (
    SELECT source_year, source_month FROM salary.salary_entries
    UNION
    SELECT source_year, source_month FROM salary.salary_expenses
) months
ORDER BY source_year, source_month;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var year = reader.GetInt32(0);
                var month = reader.GetInt32(1);
                yield return (year, month, $"postgres://salary/{year:D4}-{month:D2}");
            }
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadMonthPayments(int year, int month)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            var entries = new List<SalaryEntry>();
            var expenses = new List<FirmExpense>();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT employee_id, employee_folder, full_name, firm_name, hours_worked, hourly_rate, advance,
       saved_net_salary, status, note, color_tag, custom_values
FROM salary.salary_entries
WHERE source_year = @year AND source_month = @month
ORDER BY lower(firm_name), COALESCE(updated_at, '') DESC, id DESC, lower(full_name);";
                command.Parameters.AddWithValue("year", year);
                command.Parameters.AddWithValue("month", month);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    entries.Add(ReadSalaryEntry(reader));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, firm_name, year, month, name, amount
FROM salary.salary_expenses
WHERE source_year = @year AND source_month = @month
ORDER BY firm_name, name;";
                command.Parameters.AddWithValue("year", year);
                command.Parameters.AddWithValue("month", month);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    expenses.Add(ReadFirmExpense(reader));
            }

            return (entries, expenses);
        }

        public void SaveMonthPayments(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            DeleteMonthRows(connection, transaction, year, month);

            var rowId = 1;
            foreach (var entry in entries ?? Array.Empty<SalaryEntry>())
                InsertSalaryEntry(connection, transaction, year, month, rowId++, entry);

            foreach (var expense in expenses ?? Array.Empty<FirmExpense>())
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
        }

        public void ReplaceFirmPaymentsForFirm(int year, int month, string firmName, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            DeleteFirmRows(connection, transaction, year, month, firmName);

            var rowId = GetNextRowId(connection, transaction, year, month);
            foreach (var entry in entries ?? Array.Empty<SalaryEntry>())
                InsertSalaryEntry(connection, transaction, year, month, rowId++, entry);

            foreach (var expense in expenses ?? Array.Empty<FirmExpense>())
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
        }

        public void UpsertFirmExpense(int year, int month, FirmExpense expense)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertSalaryExpense(connection, transaction, year, month, expense);
            transaction.Commit();
        }

        public bool DeleteFirmExpense(int year, int month, string expenseId)
        {
            if (string.IsNullOrWhiteSpace(expenseId))
                return false;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM salary.salary_expenses
WHERE source_year = @year AND source_month = @month AND id = @id;";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("id", expenseId);
            return command.ExecuteNonQuery() > 0;
        }

        public void ReplaceFirmExpensesForFirm(int year, int month, string firmName, IReadOnlyList<FirmExpense> expenses)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = @"
DELETE FROM salary.salary_expenses
WHERE source_year = @year
  AND source_month = @month
  AND lower(firm_name) = lower(@firmName);";
                deleteCommand.Parameters.AddWithValue("year", year);
                deleteCommand.Parameters.AddWithValue("month", month);
                deleteCommand.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var expense in expenses ?? Array.Empty<FirmExpense>())
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
        }

        public void UpdateHourlyRateForward(
            string? employeeId,
            string employeeFolder,
            string firmName,
            decimal newRate,
            string fromMonthKey,
            CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            using var connection = OpenConnection();

            foreach (var monthDb in EnumerateMonthDatabases().ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var monthKey = $"{monthDb.year:D4}-{monthDb.month:D2}";
                if (string.Compare(monthKey, fromMonthKey, StringComparison.Ordinal) <= 0)
                    continue;

                using var command = connection.CreateCommand();
                command.CommandText = @"
UPDATE salary.salary_entries
SET hourly_rate = @hourlyRate,
    updated_at = @updatedAt
WHERE source_year = @sourceYear
  AND source_month = @sourceMonth
  AND lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND COALESCE(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR ((@employeeId = '' OR COALESCE(employee_id, '') = '') AND lower(COALESCE(employee_folder, '')) = lower(@employeeFolder))
      );";
                command.Parameters.AddWithValue("hourlyRate", ToInvariant(newRate));
                command.Parameters.AddWithValue("updatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("sourceYear", monthDb.year);
                command.Parameters.AddWithValue("sourceMonth", monthDb.month);
                command.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
                command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
                command.Parameters.AddWithValue("employeeFolder", employeeFolder ?? string.Empty);
                command.ExecuteNonQuery();
            }
        }

        public Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForAllRequests(
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests)
        {
            var result = requests.ToDictionary(
                request => request.requestKey,
                _ => new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            if (requests.Count == 0)
                return result;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT source_year, source_month, employee_id, employee_folder, firm_name, saved_net_salary, status
FROM salary.salary_entries
WHERE (source_year::text || '-' || lpad(source_month::text, 2, '0')) < @beforeMonthKey;";
            command.Parameters.AddWithValue("beforeMonthKey", beforeMonthKey);

            var requestIndexes = BuildSavedPaymentRequestIndexes(requests);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var monthKey = $"{reader.GetInt32(0):D4}-{reader.GetInt32(1):D2}";
                var employeeId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var employeeFolder = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                var firmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                var netSalary = reader.IsDBNull(5) ? 0m : ParseDecimal(reader.GetString(5));
                var status = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
                var paid = IsPaidStatus(status);

                foreach (var requestKey in MatchSavedPaymentRequests(requestIndexes, employeeId, employeeFolder, firmName))
                    result[requestKey][monthKey] = (netSalary, paid);
            }

            return result;
        }

        public Dictionary<string, (decimal netSalary, bool paid)> GetSavedPaymentsForEmployee(
            string employeeFolder,
            string? employeeId,
            string firmName,
            string beforeMonthKey)
        {
            var requestKey = "single";
            var result = GetSavedPaymentsForAllRequests(
                beforeMonthKey,
                new[] { (requestKey, firmName, employeeFolder, employeeId) });

            return result.TryGetValue(requestKey, out var payments)
                ? payments
                : new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase);
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
CREATE SCHEMA IF NOT EXISTS salary;

CREATE TABLE IF NOT EXISTS salary.salary_entries (
    source_year INTEGER NOT NULL,
    source_month INTEGER NOT NULL,
    source_db_path TEXT NOT NULL,
    id INTEGER NOT NULL,
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
    updated_at TEXT,
    PRIMARY KEY (source_year, source_month, id)
);

CREATE TABLE IF NOT EXISTS salary.salary_expenses (
    source_year INTEGER NOT NULL,
    source_month INTEGER NOT NULL,
    source_db_path TEXT NOT NULL,
    id TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    name TEXT DEFAULT '',
    amount TEXT NOT NULL DEFAULT '0',
    PRIMARY KEY (source_year, source_month, id)
);

CREATE INDEX IF NOT EXISTS idx_pg_salary_entries_firm ON salary.salary_entries(source_year, source_month, firm_name);
CREATE INDEX IF NOT EXISTS idx_pg_salary_entries_employee_id ON salary.salary_entries(employee_id);
CREATE INDEX IF NOT EXISTS idx_pg_salary_expenses_firm ON salary.salary_expenses(source_year, source_month, firm_name);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private static void DeleteMonthRows(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month)
        {
            using (var deleteEntries = connection.CreateCommand())
            {
                deleteEntries.Transaction = transaction;
                deleteEntries.CommandText = "DELETE FROM salary.salary_entries WHERE source_year = @year AND source_month = @month;";
                deleteEntries.Parameters.AddWithValue("year", year);
                deleteEntries.Parameters.AddWithValue("month", month);
                deleteEntries.ExecuteNonQuery();
            }

            using (var deleteExpenses = connection.CreateCommand())
            {
                deleteExpenses.Transaction = transaction;
                deleteExpenses.CommandText = "DELETE FROM salary.salary_expenses WHERE source_year = @year AND source_month = @month;";
                deleteExpenses.Parameters.AddWithValue("year", year);
                deleteExpenses.Parameters.AddWithValue("month", month);
                deleteExpenses.ExecuteNonQuery();
            }
        }

        private static void DeleteFirmRows(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month, string firmName)
        {
            using (var deleteEntries = connection.CreateCommand())
            {
                deleteEntries.Transaction = transaction;
                deleteEntries.CommandText = @"
DELETE FROM salary.salary_entries
WHERE source_year = @year
  AND source_month = @month
  AND lower(firm_name) = lower(@firmName);";
                deleteEntries.Parameters.AddWithValue("year", year);
                deleteEntries.Parameters.AddWithValue("month", month);
                deleteEntries.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
                deleteEntries.ExecuteNonQuery();
            }

            using (var deleteExpenses = connection.CreateCommand())
            {
                deleteExpenses.Transaction = transaction;
                deleteExpenses.CommandText = @"
DELETE FROM salary.salary_expenses
WHERE source_year = @year
  AND source_month = @month
  AND lower(firm_name) = lower(@firmName);";
                deleteExpenses.Parameters.AddWithValue("year", year);
                deleteExpenses.Parameters.AddWithValue("month", month);
                deleteExpenses.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
                deleteExpenses.ExecuteNonQuery();
            }
        }

        private static int GetNextRowId(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT COALESCE(MAX(id), 0) + 1
FROM salary.salary_entries
WHERE source_year = @year AND source_month = @month;";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private static void InsertSalaryEntry(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month, int rowId, SalaryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary.salary_entries (
    source_year, source_month, source_db_path, id, firm_name, year, month, employee_id, employee_folder, full_name,
    hours_worked, hourly_rate, advance, saved_net_salary, status, note, color_tag, custom_values, updated_at
) VALUES (
    @sourceYear, @sourceMonth, @sourceDbPath, @id, @firmName, @year, @month, @employeeId, @employeeFolder, @fullName,
    @hoursWorked, @hourlyRate, @advance, @savedNetSalary, @status, @note, @colorTag, @customValues, @updatedAt
);";
            command.Parameters.AddWithValue("sourceYear", year);
            command.Parameters.AddWithValue("sourceMonth", month);
            command.Parameters.AddWithValue("sourceDbPath", BuildSourcePath(year, month));
            command.Parameters.AddWithValue("id", rowId);
            command.Parameters.AddWithValue("firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("fullName", entry.FullName ?? string.Empty);
            command.Parameters.AddWithValue("hoursWorked", ToInvariant(entry.HoursWorked));
            command.Parameters.AddWithValue("hourlyRate", ToInvariant(entry.HourlyRate));
            command.Parameters.AddWithValue("advance", ToInvariant(entry.Advance));
            command.Parameters.AddWithValue("savedNetSalary", ToInvariant(entry.SavedNetSalary));
            command.Parameters.AddWithValue("status", entry.Status ?? string.Empty);
            command.Parameters.AddWithValue("note", entry.Note ?? string.Empty);
            command.Parameters.AddWithValue("colorTag", entry.ColorTag ?? string.Empty);
            command.Parameters.AddWithValue("customValues", JsonSerializer.Serialize(entry.CustomValues ?? new Dictionary<string, decimal>()));
            command.Parameters.AddWithValue("updatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static void InsertSalaryExpense(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month, FirmExpense expense)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary.salary_expenses (
    source_year, source_month, source_db_path, id, firm_name, year, month, name, amount
) VALUES (
    @sourceYear, @sourceMonth, @sourceDbPath, @id, @firmName, @year, @month, @name, @amount
)
ON CONFLICT(source_year, source_month, id) DO UPDATE SET
    firm_name = EXCLUDED.firm_name,
    year = EXCLUDED.year,
    month = EXCLUDED.month,
    name = EXCLUDED.name,
    amount = EXCLUDED.amount;";
            command.Parameters.AddWithValue("sourceYear", year);
            command.Parameters.AddWithValue("sourceMonth", month);
            command.Parameters.AddWithValue("sourceDbPath", BuildSourcePath(year, month));
            command.Parameters.AddWithValue("id", string.IsNullOrWhiteSpace(expense.Id) ? Guid.NewGuid().ToString() : expense.Id);
            command.Parameters.AddWithValue("firmName", expense.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("name", expense.Name ?? string.Empty);
            command.Parameters.AddWithValue("amount", ToInvariant(expense.Amount));
            command.ExecuteNonQuery();
        }

        private static SalaryEntry ReadSalaryEntry(NpgsqlDataReader reader)
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

        private static FirmExpense ReadFirmExpense(NpgsqlDataReader reader)
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

        private static string BuildSourcePath(int year, int month) => $"postgres://salary/{year:D4}-{month:D2}";

        private static string ToInvariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private sealed class SavedPaymentRequestIndexes
        {
            public Dictionary<string, List<string>> ByEmployeeId { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, List<string>> ByEmployeeFolder { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> FirmByRequest { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static SavedPaymentRequestIndexes BuildSavedPaymentRequestIndexes(
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests)
        {
            var indexes = new SavedPaymentRequestIndexes();
            foreach (var request in requests)
            {
                indexes.FirmByRequest[request.requestKey] = request.firmName ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(request.employeeId))
                    AddIndex(indexes.ByEmployeeId, request.employeeId, request.requestKey);

                AddIndex(indexes.ByEmployeeFolder, request.employeeFolder, request.requestKey);
            }

            return indexes;
        }

        private static IEnumerable<string> MatchSavedPaymentRequests(
            SavedPaymentRequestIndexes indexes,
            string employeeId,
            string employeeFolder,
            string firmName)
        {
            var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(employeeId)
                && indexes.ByEmployeeId.TryGetValue(employeeId, out var byId))
            {
                foreach (var requestKey in byId)
                    matched.Add(requestKey);
            }

            if (indexes.ByEmployeeFolder.TryGetValue(employeeFolder ?? string.Empty, out var byFolder))
            {
                foreach (var requestKey in byFolder)
                    matched.Add(requestKey);
            }

            foreach (var requestKey in matched)
            {
                if (indexes.FirmByRequest.TryGetValue(requestKey, out var requestFirm)
                    && string.Equals(requestFirm, firmName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return requestKey;
                }
            }
        }

        private static void AddIndex(Dictionary<string, List<string>> index, string? key, string requestKey)
        {
            key ??= string.Empty;
            if (!index.TryGetValue(key, out var list))
            {
                list = new List<string>();
                index[key] = list;
            }

            list.Add(requestKey);
        }

        private static bool IsPaidStatus(string status)
            => string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase)
               || string.Equals(status, "оплачено", StringComparison.OrdinalIgnoreCase);

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            LoggingService.LogWarning("PostgresFinanceMonthPaymentsStorage.ParseDecimal", $"Failed to parse decimal value '{value}'. Using 0.");
            return 0m;
        }
    }
}

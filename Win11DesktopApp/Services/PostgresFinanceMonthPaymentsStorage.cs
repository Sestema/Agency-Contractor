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
       saved_net_salary, status, note, color_tag, custom_values, updated_at
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

        public void UpsertSalaryEntries(int year, int month, IReadOnlyList<SalaryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            var rowId = GetNextRowId(connection, transaction, year, month);
            foreach (var entry in entries)
            {
                EnsureSalaryEntryNotStale(connection, transaction, year, month, entry);
                DeleteSalaryEntry(connection, transaction, year, month, entry);
                InsertSalaryEntry(connection, transaction, year, month, rowId++, entry);
            }

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

        public FirmFinanceRenameResult RenameFirmReferences(
            string oldName,
            string newName,
            int effectiveYear,
            int effectiveMonth,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            if (string.IsNullOrWhiteSpace(oldName)
                || string.IsNullOrWhiteSpace(newName)
                || effectiveYear <= 0
                || effectiveMonth is < 1 or > 12)
            {
                return new FirmFinanceRenameResult();
            }

            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var conflictCommand = connection.CreateCommand())
            {
                conflictCommand.Transaction = transaction;
                conflictCommand.CommandText = @"
SELECT old_row.full_name, old_row.source_year, old_row.source_month
FROM salary.salary_entries old_row
JOIN salary.salary_entries new_row
  ON new_row.source_year = old_row.source_year
 AND new_row.source_month = old_row.source_month
 AND lower(new_row.firm_name) = lower(@newName)
 AND (
      (COALESCE(old_row.employee_id, '') <> ''
       AND lower(COALESCE(new_row.employee_id, '')) = lower(old_row.employee_id))
      OR lower(COALESCE(new_row.employee_folder, '')) =
         lower(CASE
           WHEN lower(COALESCE(old_row.employee_folder, '')) = lower(@oldFolder)
             OR (
                  length(COALESCE(old_row.employee_folder, '')) > length(@oldFolder)
                  AND lower(substr(COALESCE(old_row.employee_folder, ''), 1, length(@oldFolder))) = lower(@oldFolder)
                  AND substr(COALESCE(old_row.employee_folder, ''), length(@oldFolder) + 1, 1) = '\'
                )
           THEN @newFolder || substr(old_row.employee_folder, length(@oldFolder) + 1)
           ELSE old_row.employee_folder
         END)
     )
WHERE lower(old_row.firm_name) = lower(@oldName)
  AND (old_row.source_year > @effectiveYear
       OR (old_row.source_year = @effectiveYear AND old_row.source_month >= @effectiveMonth))
  AND lower(COALESCE(old_row.status, '')) <> 'paid'
LIMIT 1;";
                AddFirmRenameParameters(
                    conflictCommand,
                    oldName,
                    newName,
                    effectiveYear,
                    effectiveMonth,
                    oldCompanyFolder,
                    newCompanyFolder);
                using var reader = conflictCommand.ExecuteReader();
                if (reader.Read())
                {
                    throw new InvalidOperationException(
                        $"Перейменування зупинено: для {reader.GetString(0)} вже існує рядок під новою назвою за {reader.GetInt32(2):D2}.{reader.GetInt32(1):D4}.");
                }
            }

            int entriesRenamed;
            int pathsUpdated;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.Transaction = transaction;
                countCommand.CommandText = @"
SELECT
  (SELECT COUNT(*)
   FROM salary.salary_entries
   WHERE lower(firm_name) = lower(@oldName)
     AND (source_year > @effectiveYear
          OR (source_year = @effectiveYear AND source_month >= @effectiveMonth))
     AND lower(COALESCE(status, '')) <> 'paid'),
  (SELECT COUNT(*)
   FROM salary.salary_entries
   WHERE lower(COALESCE(employee_folder, '')) = lower(@oldFolder)
      OR (
           length(COALESCE(employee_folder, '')) > length(@oldFolder)
           AND lower(substr(COALESCE(employee_folder, ''), 1, length(@oldFolder))) = lower(@oldFolder)
           AND substr(COALESCE(employee_folder, ''), length(@oldFolder) + 1, 1) = '\'
         ));";
                AddFirmRenameParameters(
                    countCommand,
                    oldName,
                    newName,
                    effectiveYear,
                    effectiveMonth,
                    oldCompanyFolder,
                    newCompanyFolder);
                using var reader = countCommand.ExecuteReader();
                reader.Read();
                entriesRenamed = Convert.ToInt32(reader.GetInt64(0), CultureInfo.InvariantCulture);
                pathsUpdated = Convert.ToInt32(reader.GetInt64(1), CultureInfo.InvariantCulture);
            }

            using (var updateEntries = connection.CreateCommand())
            {
                updateEntries.Transaction = transaction;
                updateEntries.CommandText = @"
UPDATE salary.salary_entries
SET firm_name = CASE
      WHEN (source_year > @effectiveYear
            OR (source_year = @effectiveYear AND source_month >= @effectiveMonth))
       AND lower(COALESCE(status, '')) <> 'paid'
      THEN @newName
      ELSE firm_name
    END
WHERE lower(firm_name) = lower(@oldName);";
                AddFirmRenameParameters(
                    updateEntries,
                    oldName,
                    newName,
                    effectiveYear,
                    effectiveMonth,
                    oldCompanyFolder,
                    newCompanyFolder);
                updateEntries.ExecuteNonQuery();
            }

            using (var updatePaths = connection.CreateCommand())
            {
                updatePaths.Transaction = transaction;
                // See the comment on RepairEmployeeFolderPrefixes below: this is an exact
                // prefix + path-separator-boundary match, not a LIKE wildcard match. Folder
                // names routinely contain '_' (space -> underscore) and Postgres additionally
                // treats '\' as its LIKE escape character by default, so a naive
                // "LIKE @oldFolder || '%'" both over-matches unrelated folders (via '_'
                // wildcards) and under-matches real Windows paths (via '\' escaping).
                updatePaths.CommandText = @"
UPDATE salary.salary_entries
SET employee_folder = @newFolder || substr(employee_folder, length(@oldFolder) + 1)
WHERE lower(COALESCE(employee_folder, '')) = lower(@oldFolder)
   OR (
        length(COALESCE(employee_folder, '')) > length(@oldFolder)
        AND lower(substr(COALESCE(employee_folder, ''), 1, length(@oldFolder))) = lower(@oldFolder)
        AND substr(COALESCE(employee_folder, ''), length(@oldFolder) + 1, 1) = '\'
      );";
                AddFirmRenameParameters(
                    updatePaths,
                    oldName,
                    newName,
                    effectiveYear,
                    effectiveMonth,
                    oldCompanyFolder,
                    newCompanyFolder);
                updatePaths.ExecuteNonQuery();
            }

            int expensesRenamed;
            using (var updateExpenses = connection.CreateCommand())
            {
                updateExpenses.Transaction = transaction;
                updateExpenses.CommandText = @"
UPDATE salary.salary_expenses
SET firm_name = @newName
WHERE lower(firm_name) = lower(@oldName)
  AND (source_year > @effectiveYear
       OR (source_year = @effectiveYear AND source_month >= @effectiveMonth));";
                AddFirmRenameParameters(
                    updateExpenses,
                    oldName,
                    newName,
                    effectiveYear,
                    effectiveMonth,
                    oldCompanyFolder,
                    newCompanyFolder);
                expensesRenamed = updateExpenses.ExecuteNonQuery();
            }

            transaction.Commit();
            return new FirmFinanceRenameResult
            {
                EntriesRenamed = entriesRenamed,
                EntryPathsUpdated = pathsUpdated,
                ExpensesRenamed = expensesRenamed
            };
        }

        public IReadOnlyList<string> DiscoverFirmNamesForCompanyFolder(string companyFolder)
        {
            if (string.IsNullOrWhiteSpace(companyFolder))
                return Array.Empty<string>();

            return DiscoverFirmNamesForCompanyFolderPrefixes(new[] { companyFolder });
        }

        public IReadOnlyList<string> DiscoverFirmNamesForCompanyFolderPrefixes(IReadOnlyCollection<string> companyFolderPrefixes)
        {
            if (companyFolderPrefixes == null || companyFolderPrefixes.Count == 0)
                return Array.Empty<string>();

            var normalizedPrefixes = companyFolderPrefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(prefix => prefix.TrimEnd('\\', '/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedPrefixes.Count == 0)
                return Array.Empty<string>();

            EnsureInitialized();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT firm_name, employee_folder
FROM salary.salary_entries;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var firmName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var employeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(firmName))
                    continue;

                if (PostgresEmployeeFolderBelongsToCompanyPrefixes(employeeFolder, normalizedPrefixes))
                    names.Add(firmName);
            }

            return names.ToList();
        }

        private static bool PostgresEmployeeFolderBelongsToCompanyPrefixes(
            string employeeFolder,
            IReadOnlyCollection<string> normalizedCompanyFolderPrefixes)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder) || normalizedCompanyFolderPrefixes.Count == 0)
                return false;

            var normalizedEmployeeFolder = employeeFolder.Replace('/', '\\').TrimEnd('\\');
            foreach (var prefix in normalizedCompanyFolderPrefixes)
            {
                if (normalizedEmployeeFolder.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalizedEmployeeFolder, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var companyFolder = SalaryDbService.TryExtractCompanyFolderFromEmployeePath(employeeFolder);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return false;

            var normalizedCompanyFolder = companyFolder.Replace('/', '\\').TrimEnd('\\');
            return normalizedCompanyFolderPrefixes.Any(prefix =>
                string.Equals(normalizedCompanyFolder, prefix, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> DiscoverAllDistinctFirmNames()
        {
            EnsureInitialized();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT firm_name FROM salary.salary_entries;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }

            return names.ToList();
        }

        public int RepairEmployeeFolderPrefixes(string oldCompanyFolder, string newCompanyFolder)
        {
            if (string.IsNullOrWhiteSpace(oldCompanyFolder)
                || string.IsNullOrWhiteSpace(newCompanyFolder)
                || string.Equals(oldCompanyFolder, newCompanyFolder, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Exact prefix + path-separator-boundary match (not LIKE) - see the comment in
            // RenameFirmReferences above for why a LIKE-based match here is unsafe.
            command.CommandText = @"
UPDATE salary.salary_entries
SET employee_folder = @newFolder || substr(employee_folder, length(@oldFolder) + 1)
WHERE lower(COALESCE(employee_folder, '')) = lower(@oldFolder)
   OR (
        length(COALESCE(employee_folder, '')) > length(@oldFolder)
        AND lower(substr(COALESCE(employee_folder, ''), 1, length(@oldFolder))) = lower(@oldFolder)
        AND substr(COALESCE(employee_folder, ''), length(@oldFolder) + 1, 1) = '\'
      );";
            command.Parameters.AddWithValue("oldFolder", oldCompanyFolder.TrimEnd('\\', '/'));
            command.Parameters.AddWithValue("newFolder", newCompanyFolder.TrimEnd('\\', '/'));
            var updated = command.ExecuteNonQuery();
            transaction.Commit();
            return updated;
        }

        private static void AddFirmRenameParameters(
            NpgsqlCommand command,
            string oldName,
            string newName,
            int effectiveYear,
            int effectiveMonth,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            command.Parameters.AddWithValue("oldName", oldName);
            command.Parameters.AddWithValue("newName", newName);
            command.Parameters.AddWithValue("effectiveYear", effectiveYear);
            command.Parameters.AddWithValue("effectiveMonth", effectiveMonth);
            command.Parameters.AddWithValue("oldFolder", oldCompanyFolder ?? string.Empty);
            command.Parameters.AddWithValue("newFolder", newCompanyFolder ?? string.Empty);
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

        private static void DeleteSalaryEntry(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month, SalaryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM salary.salary_entries
WHERE source_year = @year
  AND source_month = @month
  AND lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND COALESCE(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR lower(COALESCE(employee_folder, '')) = lower(@employeeFolder)
      );";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.ExecuteNonQuery();
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
            var updatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
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
            command.Parameters.AddWithValue("updatedAt", updatedAt);
            command.ExecuteNonQuery();
            entry.UpdatedAt = updatedAt;
        }

        private static void EnsureSalaryEntryNotStale(NpgsqlConnection connection, NpgsqlTransaction transaction, int year, int month, SalaryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT updated_at
FROM salary.salary_entries
WHERE source_year = @year
  AND source_month = @month
  AND lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND COALESCE(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR lower(COALESCE(employee_folder, '')) = lower(@employeeFolder)
      )
ORDER BY COALESCE(updated_at, '') DESC, id DESC
LIMIT 1;";
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);
            command.Parameters.AddWithValue("firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", entry.EmployeeFolder ?? string.Empty);
            var currentUpdatedAt = command.ExecuteScalar() as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentUpdatedAt)
                || string.Equals(currentUpdatedAt, entry.UpdatedAt ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Зарплату працівника {entry.FullName} вже змінено на іншому ПК. Оновіть рядок перед збереженням.");
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
                CustomValues = customValues,
                UpdatedAt = reader.IsDBNull(12) ? string.Empty : reader.GetString(12)
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
            => PostgresConnectionFactory.OpenConnection(_settingsService);

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

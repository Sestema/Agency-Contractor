using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class SalaryDbService
    {
        private const int CurrentSchemaVersion = 1;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private readonly object _monthDbIndexLock = new();
        private readonly HashSet<string> _initializedDatabases = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<(int year, int month), List<string>> _monthDbIndex = new();
        private string _monthDbIndexFolder = string.Empty;
        private DateTime _monthDbIndexFolderLastWriteUtc = DateTime.MinValue;
        private bool _monthDbIndexDirty = true;

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

        public string ResolveMonthDbPath(int year, int month)
        {
            var folder = SalaryDbFolder;
            if (string.IsNullOrWhiteSpace(folder))
                return string.Empty;

            Directory.CreateDirectory(folder);
            var canonicalPath = GetMonthDbPath(year, month);
            var candidates = GetMonthDbCandidates(year, month);

            if (candidates.Count == 1)
                return candidates[0];

            if (candidates.Count > 1)
            {
                var details = string.Join("; ", candidates.Select(Path.GetFileName));
                throw new InvalidOperationException(
                    $"Multiple salary DB files found for {year:D4}-{month:D2}: {details}");
            }

            return canonicalPath;
        }

        public bool MonthDbExists(int year, int month)
        {
            try
            {
                var path = ResolveMonthDbPath(year, month);
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            }
            catch (InvalidOperationException ex)
            {
                LoggingService.LogWarning("SalaryDbService.MonthDbExists", ex.Message);
                return true;
            }
        }

        public IEnumerable<(int year, int month, string path)> EnumerateMonthDatabases()
        {
            var folder = SalaryDbFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                yield break;

            foreach (var pair in GetMonthDbIndexSnapshot())
            {
                var year = pair.Key.year;
                var month = pair.Key.month;
                var candidates = pair.Value;
                if (candidates.Count == 0)
                    continue;

                if (candidates.Count > 1)
                {
                    var details = string.Join("; ", candidates.Select(Path.GetFileName));
                    LoggingService.LogWarning("SalaryDbService.EnumerateMonthDatabases",
                        $"Multiple salary DB files found for {year:D4}-{month:D2}: {details}");
                    continue;
                }

                yield return (year, month, candidates[0]);
            }
        }

        public SqliteConnection OpenMonthConnection(int year, int month)
        {
            var dbPath = ResolveMonthDbPath(year, month);
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new InvalidOperationException("Salary SQLite path is not available.");

            EnsureMonthSchema(dbPath);

            var connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared;Pooling=False");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
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
            MarkMonthDbIndexDirty();
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
       saved_net_salary, status, note, color_tag, custom_values, updated_at
FROM salary_entries
ORDER BY lower(firm_name), ifnull(updated_at, '') DESC, id DESC, lower(full_name);";
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

        public List<FirmExpense> LoadFirmExpensesOnly(int year, int month)
        {
            if (!MonthDbExists(year, month))
                return new List<FirmExpense>();

            using var connection = OpenMonthConnection(year, month);
            var expenses = new List<FirmExpense>();

            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, firm_name, year, month, name, amount
FROM salary_expenses
ORDER BY firm_name, name;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                expenses.Add(ReadFirmExpense(reader));

            return expenses;
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
ORDER BY ifnull(updated_at, '') DESC, id DESC
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

        public Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForEmployees(
            string firmName,
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string employeeFolder, string? employeeId)> requests)
        {
            var result = new Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(firmName) || requests.Count == 0)
                return result;

            var requestsByEmployeeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var requestsByFolder = new Dictionary<string, List<(string requestKey, bool hasEmployeeId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                result[request.requestKey] = new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase);

                var normalizedFolder = NormalizeEmployeePath(request.employeeFolder);
                if (!requestsByFolder.TryGetValue(normalizedFolder, out var folderRequests))
                {
                    folderRequests = new List<(string requestKey, bool hasEmployeeId)>();
                    requestsByFolder[normalizedFolder] = folderRequests;
                }

                var hasEmployeeId = !string.IsNullOrWhiteSpace(request.employeeId);
                folderRequests.Add((request.requestKey, hasEmployeeId));

                if (!hasEmployeeId)
                    continue;

                var employeeId = request.employeeId ?? string.Empty;
                if (!requestsByEmployeeId.TryGetValue(employeeId, out var employeeRequests))
                {
                    employeeRequests = new List<string>();
                    requestsByEmployeeId[employeeId] = employeeRequests;
                }

                employeeRequests.Add(request.requestKey);
            }

            foreach (var monthDb in EnumerateMonthDatabases())
            {
                var monthKey = $"{monthDb.year:D4}-{monthDb.month:D2}";
                if (string.Compare(monthKey, beforeMonthKey, StringComparison.Ordinal) >= 0)
                    continue;

                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT employee_id, employee_folder, saved_net_salary, status
FROM salary_entries
WHERE lower(firm_name) = lower(@firmName)
ORDER BY ifnull(updated_at, '') DESC, id DESC;";
                command.Parameters.AddWithValue("@firmName", firmName);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var rowEmployeeId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var rowEmployeeFolder = reader.IsDBNull(1) ? string.Empty : NormalizeEmployeePath(reader.GetString(1));
                    var netSalary = reader.IsDBNull(2) ? 0m : ParseDecimal(reader.GetString(2));
                    var status = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                    var matchedRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(rowEmployeeId)
                        && requestsByEmployeeId.TryGetValue(rowEmployeeId, out var employeeMatches))
                    {
                        foreach (var requestKey in employeeMatches)
                            matchedRequestKeys.Add(requestKey);
                    }

                    if (requestsByFolder.TryGetValue(rowEmployeeFolder, out var folderMatches))
                    {
                        foreach (var folderMatch in folderMatches)
                        {
                            if (string.IsNullOrWhiteSpace(rowEmployeeId) || !folderMatch.hasEmployeeId)
                                matchedRequestKeys.Add(folderMatch.requestKey);
                        }
                    }

                    foreach (var requestKey in matchedRequestKeys)
                    {
                        if (!result.TryGetValue(requestKey, out var requestResult))
                            continue;

                        requestResult.TryAdd(monthKey, (netSalary, string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }

            return result;
        }

        public Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForAllRequests(
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests)
        {
            var result = new Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>>(StringComparer.OrdinalIgnoreCase);
            if (requests.Count == 0)
                return result;

            var requestsByFirmAndEmployeeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var requestsByFirmAndFolder = new Dictionary<string, List<(string requestKey, bool hasEmployeeId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                result[request.requestKey] = new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase);

                var normalizedFirmName = request.firmName ?? string.Empty;
                var normalizedFolder = NormalizeEmployeePath(request.employeeFolder);
                var folderKey = BuildRequestLookupKey(normalizedFirmName, normalizedFolder);
                if (!requestsByFirmAndFolder.TryGetValue(folderKey, out var folderRequests))
                {
                    folderRequests = new List<(string requestKey, bool hasEmployeeId)>();
                    requestsByFirmAndFolder[folderKey] = folderRequests;
                }

                var hasEmployeeId = !string.IsNullOrWhiteSpace(request.employeeId);
                folderRequests.Add((request.requestKey, hasEmployeeId));

                if (!hasEmployeeId)
                    continue;

                var employeeIdKey = BuildRequestLookupKey(normalizedFirmName, request.employeeId ?? string.Empty);
                if (!requestsByFirmAndEmployeeId.TryGetValue(employeeIdKey, out var employeeRequests))
                {
                    employeeRequests = new List<string>();
                    requestsByFirmAndEmployeeId[employeeIdKey] = employeeRequests;
                }

                employeeRequests.Add(request.requestKey);
            }

            foreach (var monthDb in EnumerateMonthDatabases())
            {
                var monthKey = $"{monthDb.year:D4}-{monthDb.month:D2}";
                if (string.Compare(monthKey, beforeMonthKey, StringComparison.Ordinal) >= 0)
                    continue;

                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT firm_name, employee_id, employee_folder, saved_net_salary, status
FROM salary_entries
ORDER BY ifnull(updated_at, '') DESC, id DESC;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var rowFirmName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var rowEmployeeId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    var rowEmployeeFolder = reader.IsDBNull(2) ? string.Empty : NormalizeEmployeePath(reader.GetString(2));
                    var netSalary = reader.IsDBNull(3) ? 0m : ParseDecimal(reader.GetString(3));
                    var status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                    var matchedRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    if (!string.IsNullOrWhiteSpace(rowEmployeeId))
                    {
                        var employeeIdKey = BuildRequestLookupKey(rowFirmName, rowEmployeeId);
                        if (requestsByFirmAndEmployeeId.TryGetValue(employeeIdKey, out var employeeMatches))
                        {
                            foreach (var requestKey in employeeMatches)
                                matchedRequestKeys.Add(requestKey);
                        }
                    }

                    var folderKey = BuildRequestLookupKey(rowFirmName, rowEmployeeFolder);
                    if (requestsByFirmAndFolder.TryGetValue(folderKey, out var folderMatches))
                    {
                        foreach (var folderMatch in folderMatches)
                        {
                            if (string.IsNullOrWhiteSpace(rowEmployeeId) || !folderMatch.hasEmployeeId)
                                matchedRequestKeys.Add(folderMatch.requestKey);
                        }
                    }

                    foreach (var requestKey in matchedRequestKeys)
                    {
                        if (!result.TryGetValue(requestKey, out var requestResult))
                            continue;

                        requestResult.TryAdd(monthKey, (netSalary, string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase)));
                    }
                }
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

        private static string BuildRequestLookupKey(string firmName, string employeeKey)
        {
            return $"{firmName ?? string.Empty}\n{employeeKey ?? string.Empty}";
        }

        public void SaveMonthPayments(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            using var connection = OpenMonthConnection(year, month);
            using var transaction = connection.BeginTransaction();

            foreach (var entry in entries)
                InsertSalaryEntry(connection, transaction, year, month, entry);

            foreach (var expense in expenses)
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
            MarkMonthDbIndexDirty();
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

            var monthDatabases = EnumerateMonthDatabases().ToList();
            if (monthDatabases.Count == 0)
                return new FirmFinanceRenameResult();

            var backupFolder = Path.Combine(
                _folderService.GetBackupsFolder(),
                "FirmRenames",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{SanitizeBackupName(oldName)}-to-{SanitizeBackupName(newName)}");
            var backups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var databasesUpdated = 0;
            var entriesRenamed = 0;
            var entryPathsUpdated = 0;
            var expensesRenamed = 0;
            var emptyDuplicatesRemoved = 0;

            try
            {
                foreach (var monthDb in monthDatabases)
                {
                    using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                    if (!MonthContainsFirmReferences(connection, oldName, oldCompanyFolder, newCompanyFolder))
                        continue;

                    Directory.CreateDirectory(backupFolder);
                    var backupPath = Path.Combine(backupFolder, Path.GetFileName(monthDb.path));
                    using (var backupConnection = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
                    {
                        backupConnection.Open();
                        connection.BackupDatabase(backupConnection);
                    }
                    backups[monthDb.path] = backupPath;

                    var before = CaptureRenameValidationSnapshot(connection);
                    var removedInCurrentDatabase = 0;
                    using var transaction = connection.BeginTransaction();
                    var rows = LoadFirmRenameRows(
                        connection,
                        transaction,
                        oldName,
                        newName,
                        oldCompanyFolder,
                        newCompanyFolder);

                    foreach (var row in rows)
                    {
                        var targetFolder = RemapCompanyFolder(row.EmployeeFolder, oldCompanyFolder, newCompanyFolder);
                        var shouldUseNewName = string.Equals(row.FirmName, oldName, StringComparison.OrdinalIgnoreCase)
                                               && CompareYearMonth(monthDb.year, monthDb.month, effectiveYear, effectiveMonth) >= 0
                                               && !string.Equals(row.Status, "paid", StringComparison.OrdinalIgnoreCase);
                        var targetFirmName = shouldUseNewName ? newName : row.FirmName;
                        var collision = FindRenameCollision(
                            connection,
                            transaction,
                            row.Id,
                            targetFirmName,
                            row.EmployeeId,
                            targetFolder);

                        if (collision != null)
                        {
                            if (row.HasMeaningfulData && collision.HasMeaningfulData)
                            {
                                throw new InvalidOperationException(
                                    $"Перейменування зупинено: для {row.FullName} знайдено два непорожні зарплатні рядки за {monthDb.month:D2}.{monthDb.year:D4}.");
                            }

                            if (!row.HasMeaningfulData && collision.HasMeaningfulData)
                            {
                                DeleteSalaryEntryById(connection, transaction, row.Id);
                                emptyDuplicatesRemoved++;
                                removedInCurrentDatabase++;
                                continue;
                            }

                            DeleteSalaryEntryById(connection, transaction, collision.Id);
                            emptyDuplicatesRemoved++;
                            removedInCurrentDatabase++;
                        }

                        UpdateFirmRenameRow(
                            connection,
                            transaction,
                            row.Id,
                            targetFirmName,
                            targetFolder);

                        if (!string.Equals(row.EmployeeFolder, targetFolder, StringComparison.Ordinal))
                            entryPathsUpdated++;
                        if (!string.Equals(row.FirmName, targetFirmName, StringComparison.Ordinal))
                            entriesRenamed++;
                    }

                    if (CompareYearMonth(monthDb.year, monthDb.month, effectiveYear, effectiveMonth) >= 0)
                    {
                        using var expenseCommand = connection.CreateCommand();
                        expenseCommand.Transaction = transaction;
                        expenseCommand.CommandText = @"
UPDATE salary_expenses
SET firm_name = @newName
WHERE lower(firm_name) = lower(@oldName);";
                        expenseCommand.Parameters.AddWithValue("@oldName", oldName);
                        expenseCommand.Parameters.AddWithValue("@newName", newName);
                        expensesRenamed += expenseCommand.ExecuteNonQuery();
                    }

                    transaction.Commit();

                    var after = CaptureRenameValidationSnapshot(connection);
                    if (after.EntryCount != before.EntryCount - removedInCurrentDatabase
                        || after.ExpenseCount != before.ExpenseCount
                        || after.HoursTotal != before.HoursTotal
                        || after.NetTotal != before.NetTotal
                        || after.ExpenseTotal != before.ExpenseTotal)
                    {
                        throw new InvalidOperationException(
                            $"Перевірка зарплат після перейменування не пройшла для {monthDb.month:D2}.{monthDb.year:D4}.");
                    }

                    databasesUpdated++;
                }
            }
            catch
            {
                RestoreFirmRenameBackups(backups);
                throw;
            }

            MarkMonthDbIndexDirty();
            return new FirmFinanceRenameResult
            {
                DatabasesUpdated = databasesUpdated,
                EntriesRenamed = entriesRenamed,
                EntryPathsUpdated = entryPathsUpdated,
                ExpensesRenamed = expensesRenamed,
                EmptyDuplicatesRemoved = emptyDuplicatesRemoved,
                BackupFolderPath = backups.Count > 0 ? backupFolder : string.Empty
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
                .Select(NormalizeFolderPrefix)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedPrefixes.Count == 0)
                return Array.Empty<string>();

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var monthDb in EnumerateMonthDatabases())
            {
                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT firm_name, employee_folder
FROM salary_entries;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var firmName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                    var employeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(firmName))
                        continue;

                    if (EmployeeFolderBelongsToCompanyPrefixes(employeeFolder, normalizedPrefixes))
                        names.Add(firmName);
                }
            }

            return names.ToList();
        }

        internal static string? TryExtractCompanyFolderFromEmployeePath(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            var normalized = employeeFolder.Replace('/', '\\');
            foreach (var employeesFolderName in FolderNames.AllEmployeesFolderNames)
            {
                var marker = "\\" + employeesFolderName + "\\";
                var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index > 0)
                    return normalized[..index];
            }

            return null;
        }

        private static bool EmployeeFolderBelongsToCompanyPrefixes(
            string employeeFolder,
            IReadOnlyCollection<string> normalizedCompanyFolderPrefixes)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder) || normalizedCompanyFolderPrefixes.Count == 0)
                return false;

            var normalizedEmployeeFolder = NormalizeFolderPrefix(employeeFolder);
            foreach (var prefix in normalizedCompanyFolderPrefixes)
            {
                if (normalizedEmployeeFolder.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalizedEmployeeFolder, prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var companyFolder = TryExtractCompanyFolderFromEmployeePath(employeeFolder);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return false;

            var normalizedCompanyFolder = NormalizeFolderPrefix(companyFolder);
            return normalizedCompanyFolderPrefixes.Any(prefix =>
                string.Equals(normalizedCompanyFolder, prefix, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<string> DiscoverAllDistinctFirmNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var monthDb in EnumerateMonthDatabases())
            {
                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT DISTINCT firm_name FROM salary_entries;";
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

            var updated = 0;
            foreach (var monthDb in EnumerateMonthDatabases())
            {
                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                // Note: this deliberately uses an exact prefix comparison (=/substr) instead of
                // LIKE '<oldFolder>%'. Company folder names commonly contain '_' (spaces are
                // replaced with underscores), and SQL LIKE treats a bare '_' as a "match any one
                // character" wildcard - so a LIKE-based prefix match can silently match a
                // completely unrelated folder whose path merely has the same length/shape. It
                // also requires a real path-separator boundary after the prefix, so e.g. "...\Foo"
                // no longer matches "...\FooBar\...".
                command.CommandText = @"
UPDATE salary_entries
SET employee_folder = @newFolder || substr(replace(ifnull(employee_folder, ''), '/', '\'), length(@oldFolder) + 1)
WHERE lower(replace(ifnull(employee_folder, ''), '/', '\')) = lower(@oldFolder)
   OR (
        length(replace(ifnull(employee_folder, ''), '/', '\')) > length(@oldFolder)
        AND lower(substr(replace(ifnull(employee_folder, ''), '/', '\'), 1, length(@oldFolder))) = lower(@oldFolder)
        AND substr(replace(ifnull(employee_folder, ''), '/', '\'), length(@oldFolder) + 1, 1) = '\'
      );";
                command.Parameters.AddWithValue("@oldFolder", NormalizeFolderPrefix(oldCompanyFolder));
                command.Parameters.AddWithValue("@newFolder", NormalizeFolderPrefix(newCompanyFolder).TrimEnd('\\'));
                updated += command.ExecuteNonQuery();
                transaction.Commit();
            }

            if (updated > 0)
                MarkMonthDbIndexDirty();

            return updated;
        }

        private static string NormalizeFolderPrefix(string folder)
            => folder.Replace('/', '\\').TrimEnd('\\');

        private static bool RowBelongsToCompanyFolder(
            string employeeFolder,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            var companyFolder = TryExtractCompanyFolderFromEmployeePath(employeeFolder);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return false;

            var normalizedCompanyFolder = NormalizeFolderPrefix(companyFolder);
            return string.Equals(normalizedCompanyFolder, NormalizeFolderPrefix(oldCompanyFolder), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(normalizedCompanyFolder, NormalizeFolderPrefix(newCompanyFolder), StringComparison.OrdinalIgnoreCase);
        }

        public void UpsertFirmExpense(int year, int month, FirmExpense expense)
        {
            using var connection = OpenMonthConnection(year, month);
            using var transaction = connection.BeginTransaction();
            InsertSalaryExpense(connection, transaction, year, month, expense);
            transaction.Commit();
            MarkMonthDbIndexDirty();
        }

        public bool DeleteFirmExpense(int year, int month, string expenseId)
        {
            if (string.IsNullOrWhiteSpace(expenseId))
                return false;

            using var connection = OpenMonthConnection(year, month);
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM salary_expenses WHERE id = @id;";
            command.Parameters.AddWithValue("@id", expenseId);
            var deleted = command.ExecuteNonQuery() > 0;
            if (deleted)
                MarkMonthDbIndexDirty();

            return deleted;
        }

        public void ReplaceFirmExpensesForFirm(int year, int month, string firmName, IReadOnlyList<FirmExpense> expenses)
        {
            using var connection = OpenMonthConnection(year, month);
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM salary_expenses WHERE lower(firm_name) = lower(@firmName);";
                deleteCommand.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var expense in expenses ?? Array.Empty<FirmExpense>())
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
            MarkMonthDbIndexDirty();
        }

        /// <summary>
        /// Replaces every expense row for the month. Does not read or write salary_entries,
        /// so multi-PC salary edits cannot be overwritten by an expenses-only save.
        /// </summary>
        public void ReplaceAllFirmExpenses(int year, int month, IReadOnlyList<FirmExpense> expenses)
        {
            using var connection = OpenMonthConnection(year, month);
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM salary_expenses;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var expense in expenses ?? Array.Empty<FirmExpense>())
                InsertSalaryExpense(connection, transaction, year, month, expense);

            transaction.Commit();
            MarkMonthDbIndexDirty();
        }

        private void MarkMonthDbIndexDirty()
        {
            lock (_monthDbIndexLock)
                _monthDbIndexDirty = true;
        }

        private List<string> GetMonthDbCandidates(int year, int month)
        {
            var index = GetMonthDbIndexSnapshot();
            return index.TryGetValue((year, month), out var candidates)
                ? candidates
                : new List<string>();
        }

        private Dictionary<(int year, int month), List<string>> GetMonthDbIndexSnapshot()
        {
            var folder = SalaryDbFolder;
            if (string.IsNullOrWhiteSpace(folder))
                return new Dictionary<(int year, int month), List<string>>();

            Directory.CreateDirectory(folder);

            var folderLastWriteUtc = Directory.GetLastWriteTimeUtc(folder);
            lock (_monthDbIndexLock)
            {
                if (_monthDbIndexDirty
                    || !string.Equals(_monthDbIndexFolder, folder, StringComparison.OrdinalIgnoreCase)
                    || _monthDbIndexFolderLastWriteUtc != folderLastWriteUtc)
                {
                    _monthDbIndex = BuildMonthDbIndex(folder);
                    _monthDbIndexFolder = folder;
                    _monthDbIndexFolderLastWriteUtc = folderLastWriteUtc;
                    _monthDbIndexDirty = false;
                }

                return _monthDbIndex.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.ToList());
            }
        }

        private static Dictionary<(int year, int month), List<string>> BuildMonthDbIndex(string folder)
        {
            var result = new Dictionary<(int year, int month), List<string>>();
            foreach (var path in Directory.EnumerateFiles(folder, "salary_*_*.db"))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                var parts = fileName.Split('_');
                if (parts.Length < 3)
                    continue;

                if (!int.TryParse(parts[1], out var year) || !int.TryParse(parts[2], out var month))
                    continue;

                var key = (year, month);
                if (!result.TryGetValue(key, out var candidates))
                {
                    candidates = new List<string>();
                    result[key] = candidates;
                }

                candidates.Add(path);
            }

            foreach (var candidates in result.Values)
                candidates.Sort(StringComparer.OrdinalIgnoreCase);

            return result;
        }

        public int RemapCustomFieldIdAcrossMonths(string oldFieldId, string newFieldId)
        {
            if (string.IsNullOrWhiteSpace(oldFieldId)
                || string.IsNullOrWhiteSpace(newFieldId)
                || string.Equals(oldFieldId, newFieldId, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            var updatedRows = 0;
            foreach (var monthDb in EnumerateMonthDatabases())
            {
                using var connection = OpenMonthConnection(monthDb.year, monthDb.month);
                using var transaction = connection.BeginTransaction();
                var entriesToUpdate = new List<(long rowId, string customValuesJson)>();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT id, custom_values FROM salary_entries;";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var rowId = reader.GetInt64(0);
                        var customValuesJson = reader.IsDBNull(1) ? "{}" : reader.GetString(1);
                        var customValues = JsonSerializer.Deserialize<Dictionary<string, decimal>>(customValuesJson)
                                           ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

                        if (!TryMoveCustomValueKey(customValues, oldFieldId, newFieldId))
                            continue;

                        entriesToUpdate.Add((rowId, JsonSerializer.Serialize(customValues)));
                    }
                }

                foreach (var entry in entriesToUpdate)
                {
                    using var updateCommand = connection.CreateCommand();
                    updateCommand.Transaction = transaction;
                    updateCommand.CommandText = @"
UPDATE salary_entries
SET custom_values = @customValues,
    updated_at = @updatedAt
WHERE id = @id;";
                    updateCommand.Parameters.AddWithValue("@customValues", entry.customValuesJson);
                    updateCommand.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    updateCommand.Parameters.AddWithValue("@id", entry.rowId);
                    updatedRows += updateCommand.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            return updatedRows;
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

                using var connection = new SqliteConnection($"Data Source={dbPath};Cache=Shared;Pooling=False");
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
            EnsureSalaryEntryNotStale(connection, transaction, entry);
            DeleteDuplicateEmployeeRows(connection, transaction, entry);
            var updatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

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
            command.Parameters.AddWithValue("@customValues", JsonSerializer.Serialize(entry.GetPersistedCustomValues()));
            command.Parameters.AddWithValue("@updatedAt", updatedAt);
            command.ExecuteNonQuery();
            entry.UpdatedAt = updatedAt;
        }

        private static void EnsureSalaryEntryNotStale(SqliteConnection connection, SqliteTransaction transaction, SalaryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT updated_at
FROM salary_entries
WHERE lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND ifnull(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR lower(ifnull(employee_folder, '')) = lower(@employeeFolder)
      )
ORDER BY ifnull(updated_at, '') DESC, id DESC
LIMIT 1;";
            command.Parameters.AddWithValue("@firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", entry.EmployeeFolder ?? string.Empty);
            var currentUpdatedAt = command.ExecuteScalar() as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(currentUpdatedAt)
                || string.Equals(currentUpdatedAt, entry.UpdatedAt ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Зарплату працівника {entry.FullName} вже змінено на іншому ПК. Оновіть рядок перед збереженням.");
        }

        private static void DeleteDuplicateEmployeeRows(SqliteConnection connection, SqliteTransaction transaction, SalaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.EmployeeId))
                return;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM salary_entries
WHERE lower(firm_name) = lower(@firmName)
  AND ifnull(employee_id, '') <> ''
  AND lower(employee_id) = lower(@employeeId)
  AND lower(ifnull(employee_folder, '')) <> lower(@employeeFolder);";
            command.Parameters.AddWithValue("@firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", entry.EmployeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", entry.EmployeeFolder ?? string.Empty);
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

        private static int CompareYearMonth(int yearA, int monthA, int yearB, int monthB)
            => yearA != yearB ? yearA.CompareTo(yearB) : monthA.CompareTo(monthB);

        private bool MonthContainsFirmReferences(
            SqliteConnection connection,
            string oldName,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT employee_folder, firm_name
FROM salary_entries
UNION ALL
SELECT '', firm_name
FROM salary_expenses;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var employeeFolder = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var firmName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (string.Equals(firmName, oldName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        employeeFolder,
                        RemapCompanyFolder(employeeFolder, oldCompanyFolder, newCompanyFolder),
                        StringComparison.Ordinal)
                    || RowBelongsToCompanyFolder(employeeFolder, oldCompanyFolder, newCompanyFolder))
                {
                    return true;
                }
            }

            return false;
        }

        private List<FirmRenameSalaryRow> LoadFirmRenameRows(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string oldName,
            string newName,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            var rows = new List<FirmRenameSalaryRow>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, employee_id, employee_folder, full_name, firm_name,
       hours_worked, hourly_rate, advance, saved_net_salary, status, note, custom_values
FROM salary_entries
ORDER BY id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = ReadFirmRenameSalaryRow(reader);
                if (string.Equals(row.FirmName, oldName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        row.EmployeeFolder,
                        RemapCompanyFolder(row.EmployeeFolder, oldCompanyFolder, newCompanyFolder),
                        StringComparison.Ordinal)
                    || (RowBelongsToCompanyFolder(row.EmployeeFolder, oldCompanyFolder, newCompanyFolder)
                        && !string.Equals(row.FirmName, newName, StringComparison.OrdinalIgnoreCase)))
                {
                    rows.Add(row);
                }
            }
            return rows;
        }

        private static FirmRenameSalaryRow? FindRenameCollision(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long sourceId,
            string targetFirmName,
            string employeeId,
            string employeeFolder)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
SELECT id, employee_id, employee_folder, full_name, firm_name,
       hours_worked, hourly_rate, advance, saved_net_salary, status, note, custom_values
FROM salary_entries
WHERE id <> @sourceId
  AND lower(firm_name) = lower(@firmName)
  AND (
        (@employeeId <> '' AND ifnull(employee_id, '') <> '' AND lower(employee_id) = lower(@employeeId))
        OR lower(ifnull(employee_folder, '')) = lower(@employeeFolder)
      )
ORDER BY ifnull(updated_at, '') DESC, id DESC
LIMIT 1;";
            command.Parameters.AddWithValue("@sourceId", sourceId);
            command.Parameters.AddWithValue("@firmName", targetFirmName);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", employeeFolder ?? string.Empty);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadFirmRenameSalaryRow(reader) : null;
        }

        private static FirmRenameSalaryRow ReadFirmRenameSalaryRow(SqliteDataReader reader)
        {
            return new FirmRenameSalaryRow
            {
                Id = reader.GetInt64(0),
                EmployeeId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                EmployeeFolder = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                FullName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                FirmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                HoursWorked = reader.IsDBNull(5) ? 0m : ParseDecimal(reader.GetString(5)),
                HourlyRate = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                Advance = reader.IsDBNull(7) ? 0m : ParseDecimal(reader.GetString(7)),
                SavedNetSalary = reader.IsDBNull(8) ? 0m : ParseDecimal(reader.GetString(8)),
                Status = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                Note = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                CustomValuesJson = reader.IsDBNull(11) ? "{}" : reader.GetString(11)
            };
        }

        private static void DeleteSalaryEntryById(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM salary_entries WHERE id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        private static void UpdateFirmRenameRow(
            SqliteConnection connection,
            SqliteTransaction transaction,
            long id,
            string firmName,
            string employeeFolder)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE salary_entries
SET firm_name = @firmName,
    employee_folder = @employeeFolder
WHERE id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@firmName", firmName);
            command.Parameters.AddWithValue("@employeeFolder", employeeFolder ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private string RemapCompanyFolder(string value, string oldCompanyFolder, string newCompanyFolder)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.IsNullOrWhiteSpace(oldCompanyFolder)
                || string.IsNullOrWhiteSpace(newCompanyFolder))
            {
                return value ?? string.Empty;
            }

            var candidates = new List<(string oldPrefix, string newPrefix)>
            {
                (oldCompanyFolder, newCompanyFolder)
            };

            if (!string.IsNullOrWhiteSpace(_folderService.RootPath))
            {
                candidates.Add((
                    Path.GetRelativePath(_folderService.RootPath, oldCompanyFolder),
                    Path.GetRelativePath(_folderService.RootPath, newCompanyFolder)));
            }

            foreach (var candidate in candidates)
            {
                if (TryReplacePathPrefix(value, candidate.oldPrefix, candidate.newPrefix, out var remapped))
                    return remapped;
            }

            return value;
        }

        private static bool TryReplacePathPrefix(string value, string oldPrefix, string newPrefix, out string remapped)
        {
            remapped = value;
            var normalizedValue = value.Replace('/', '\\').TrimEnd('\\');
            var normalizedOld = oldPrefix.Replace('/', '\\').TrimEnd('\\');
            var normalizedNew = newPrefix.Replace('/', '\\').TrimEnd('\\');
            if (!normalizedValue.Equals(normalizedOld, StringComparison.OrdinalIgnoreCase)
                && !normalizedValue.StartsWith(normalizedOld + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            remapped = normalizedNew + normalizedValue[normalizedOld.Length..];
            return true;
        }

        private static FirmRenameValidationSnapshot CaptureRenameValidationSnapshot(SqliteConnection connection)
        {
            var snapshot = new FirmRenameValidationSnapshot();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT hours_worked, saved_net_salary FROM salary_entries;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    snapshot.EntryCount++;
                    snapshot.HoursTotal += reader.IsDBNull(0) ? 0m : ParseDecimal(reader.GetString(0));
                    snapshot.NetTotal += reader.IsDBNull(1) ? 0m : ParseDecimal(reader.GetString(1));
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT amount FROM salary_expenses;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    snapshot.ExpenseCount++;
                    snapshot.ExpenseTotal += reader.IsDBNull(0) ? 0m : ParseDecimal(reader.GetString(0));
                }
            }

            return snapshot;
        }

        private static void RestoreFirmRenameBackups(IReadOnlyDictionary<string, string> backups)
        {
            foreach (var pair in backups)
            {
                try
                {
                    TryDeleteFile(pair.Key + "-wal");
                    TryDeleteFile(pair.Key + "-shm");
                    File.Copy(pair.Value, pair.Key, overwrite: true);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("SalaryDbService.RestoreFirmRenameBackup", ex);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // The main restore will report a useful error if a sidecar still blocks replacement.
            }
        }

        private static string SanitizeBackupName(string value)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return string.IsNullOrWhiteSpace(value) ? "firm" : value.Trim();
        }

        private sealed class FirmRenameSalaryRow
        {
            public long Id { get; init; }
            public string EmployeeId { get; init; } = string.Empty;
            public string EmployeeFolder { get; init; } = string.Empty;
            public string FullName { get; init; } = string.Empty;
            public string FirmName { get; init; } = string.Empty;
            public decimal HoursWorked { get; init; }
            public decimal HourlyRate { get; init; }
            public decimal Advance { get; init; }
            public decimal SavedNetSalary { get; init; }
            public string Status { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
            public string CustomValuesJson { get; init; } = "{}";

            public bool HasMeaningfulData =>
                HoursWorked != 0m
                || Advance != 0m
                || SavedNetSalary != 0m
                || string.Equals(Status, "paid", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(Note)
                || (!string.IsNullOrWhiteSpace(CustomValuesJson)
                    && !string.Equals(CustomValuesJson.Trim(), "{}", StringComparison.Ordinal));
        }

        private sealed class FirmRenameValidationSnapshot
        {
            public int EntryCount { get; set; }
            public int ExpenseCount { get; set; }
            public decimal HoursTotal { get; set; }
            public decimal NetTotal { get; set; }
            public decimal ExpenseTotal { get; set; }
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
                CustomValues = customValues,
                UpdatedAt = reader.IsDBNull(12) ? string.Empty : reader.GetString(12)
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

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            LoggingService.LogWarning("SalaryDbService.ParseDecimal", $"Failed to parse decimal value '{value}'. Using 0.");
            return 0m;
        }

        private static bool TryMoveCustomValueKey(Dictionary<string, decimal> customValues, string oldFieldId, string newFieldId)
        {
            if (!customValues.TryGetValue(oldFieldId, out var oldValue))
                return false;

            if (!customValues.ContainsKey(newFieldId))
                customValues[newFieldId] = oldValue;

            customValues.Remove(oldFieldId);
            return true;
        }

        private static string NormalizeEmployeePath(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        private static string ToInvariant(decimal value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

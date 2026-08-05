using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class LocalDbMigrationResult
    {
        public bool WasMigrationAttempted { get; init; }
        public bool IsSuccessful { get; init; }
        public int RecordsFound { get; init; }
        public int RecordsImported { get; init; }
        public int FoldersScanned { get; init; }
        public int FoldersSkipped { get; init; }
        public string Message { get; init; } = string.Empty;
        public string BackupPath { get; init; } = string.Empty;
    }

    public sealed class SalaryDbMigrationResult
    {
        public bool WasMigrationAttempted { get; init; }
        public bool IsSuccessful { get; init; }
        public int FilesScanned { get; init; }
        public int FilesSkipped { get; init; }
        public int RecordsFound { get; init; }
        public int RecordsImported { get; init; }
        public int ExpensesFound { get; init; }
        public int ExpensesImported { get; init; }
        public int DatabasesCreated { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class LocalDbService
    {
        private const int CurrentSchemaVersion = 1;
        private readonly FolderService _folderService;
        private readonly SalaryDbService _salaryDbService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public LocalDbService(FolderService folderService, SalaryDbService salaryDbService)
        {
            _folderService = folderService;
            _salaryDbService = salaryDbService;
        }

        public string DatabasePath => _folderService.LocalDbPath;
        public bool IsAvailable => !string.IsNullOrWhiteSpace(DatabasePath);

        public void EnsureInitialized()
        {
            if (_isInitialized)
                return;

            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                if (!IsAvailable)
                    return;

                var sqliteFolder = _folderService.GetSqliteFolder();
                if (string.IsNullOrWhiteSpace(sqliteFolder))
                    return;

                Directory.CreateDirectory(sqliteFolder);

                using var connection = OpenConnection();
                CreateSchema(connection);
                _isInitialized = true;
            }
        }

        public SqliteConnection OpenConnection()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Local SQLite path is not available.");

            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared;Pooling=False");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        public int RenameCurrentFirmReferences(
            string oldName,
            string newName,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;

            EnsureInitialized();
            if (!IsAvailable || !File.Exists(DatabasePath))
                return 0;

            var backupFolder = Path.Combine(
                _folderService.GetBackupsFolder(),
                "FirmRenames",
                $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-local-db");
            Directory.CreateDirectory(backupFolder);
            var backupPath = Path.Combine(backupFolder, Path.GetFileName(DatabasePath));

            using var connection = OpenConnection();
            using (var backupConnection = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
            {
                backupConnection.Open();
                connection.BackupDatabase(backupConnection);
            }

            using var transaction = connection.BeginTransaction();
            var updated = 0;
            updated += RenameTextColumn(
                connection,
                transaction,
                "custom_salary_fields",
                "firm_name",
                oldName,
                newName);
            updated += RenameTextColumn(
                connection,
                transaction,
                "advances",
                "company_id",
                oldName,
                newName);
            updated += RenameTextColumn(
                connection,
                transaction,
                "accommodations",
                "company_id",
                oldName,
                newName);

            var oldRelative = Path.GetRelativePath(_folderService.RootPath, oldCompanyFolder);
            var newRelative = Path.GetRelativePath(_folderService.RootPath, newCompanyFolder);
            foreach (var table in new[]
                     {
                         "salary_history",
                         "advances",
                         "activity_log",
                         "archive_log",
                         "employee_history",
                         "accommodations"
                     })
            {
                updated += RenamePathColumn(
                    connection,
                    transaction,
                    table,
                    "employee_folder",
                    oldCompanyFolder,
                    newCompanyFolder,
                    oldRelative,
                    newRelative);
            }

            transaction.Commit();
            return updated;
        }

        private static int RenameTextColumn(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string table,
            string column,
            string oldValue,
            string newValue)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE {table}
SET {column} = @newValue
WHERE lower({column}) = lower(@oldValue);";
            command.Parameters.AddWithValue("@oldValue", oldValue);
            command.Parameters.AddWithValue("@newValue", newValue);
            return command.ExecuteNonQuery();
        }

        private static int RenamePathColumn(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string table,
            string column,
            string oldAbsolute,
            string newAbsolute,
            string oldRelative,
            string newRelative)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Exact prefix + path-separator-boundary match instead of LIKE '<prefix>%'.
            // Company folder names commonly contain '_' (spaces become underscores), and SQL
            // LIKE treats a bare '_' as a "match any single character" wildcard, so a
            // LIKE-based prefix match can silently match a completely unrelated folder. It also
            // requires a real '\' boundary right after the prefix, so "...\Foo" no longer
            // matches "...\FooBar\...".
            command.CommandText = $@"
UPDATE {table}
SET {column} = CASE
    WHEN lower(replace({column}, '/', '\')) = lower(@oldAbsolute)
      OR (
           length(replace({column}, '/', '\')) > length(@oldAbsolute)
           AND lower(substr(replace({column}, '/', '\'), 1, length(@oldAbsolute))) = lower(@oldAbsolute)
           AND substr(replace({column}, '/', '\'), length(@oldAbsolute) + 1, 1) = '\'
         )
    THEN @newAbsolute || substr(replace({column}, '/', '\'), length(@oldAbsolute) + 1)
    WHEN lower(replace({column}, '/', '\')) = lower(@oldRelative)
      OR (
           length(replace({column}, '/', '\')) > length(@oldRelative)
           AND lower(substr(replace({column}, '/', '\'), 1, length(@oldRelative))) = lower(@oldRelative)
           AND substr(replace({column}, '/', '\'), length(@oldRelative) + 1, 1) = '\'
         )
    THEN @newRelative || substr(replace({column}, '/', '\'), length(@oldRelative) + 1)
    ELSE {column}
END
WHERE lower(replace({column}, '/', '\')) = lower(@oldAbsolute)
   OR (
        length(replace({column}, '/', '\')) > length(@oldAbsolute)
        AND lower(substr(replace({column}, '/', '\'), 1, length(@oldAbsolute))) = lower(@oldAbsolute)
        AND substr(replace({column}, '/', '\'), length(@oldAbsolute) + 1, 1) = '\'
      )
   OR lower(replace({column}, '/', '\')) = lower(@oldRelative)
   OR (
        length(replace({column}, '/', '\')) > length(@oldRelative)
        AND lower(substr(replace({column}, '/', '\'), 1, length(@oldRelative))) = lower(@oldRelative)
        AND substr(replace({column}, '/', '\'), length(@oldRelative) + 1, 1) = '\'
      );";
            command.Parameters.AddWithValue("@oldAbsolute", oldAbsolute.Replace('/', '\\').TrimEnd('\\'));
            command.Parameters.AddWithValue("@newAbsolute", newAbsolute.Replace('/', '\\').TrimEnd('\\'));
            command.Parameters.AddWithValue("@oldRelative", oldRelative.Replace('/', '\\').TrimEnd('\\'));
            command.Parameters.AddWithValue("@newRelative", newRelative.Replace('/', '\\').TrimEnd('\\'));
            return command.ExecuteNonQuery();
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

        public void InsertEmployeeHistory(string employeeId, string employeeFolder, string firmName, EmployeeHistoryEntry entry)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertEmployeeHistory(connection, transaction, employeeId, employeeFolder, firmName, entry);
            TrimEmployeeHistory(connection, transaction, employeeId, 3000);
            transaction.Commit();
        }

        public List<EmployeeHistoryEntry> GetEmployeeHistory(string employeeId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeId))
                return new List<EmployeeHistoryEntry>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, timestamp, event_type, action, field, old_value, new_value, description, actor_name
FROM employee_history
WHERE employee_id = @employeeId
ORDER BY datetime(timestamp) DESC, id DESC;";
            command.Parameters.AddWithValue("@employeeId", employeeId);

            using var reader = command.ExecuteReader();
            var result = new List<EmployeeHistoryEntry>();
            while (reader.Read())
            {
                result.Add(new EmployeeHistoryEntry
                {
                    Id = reader.GetInt64(0),
                    Timestamp = DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                        ? parsed
                        : DateTime.Now,
                    EventType = reader.GetString(2),
                    Action = reader.GetString(3),
                    Field = reader.GetString(4),
                    OldValue = reader.GetString(5),
                    NewValue = reader.GetString(6),
                    Description = reader.GetString(7),
                    ActorName = reader.GetString(8)
                });
            }

            return result;
        }

        public void DeleteEmployeeHistoryEntry(string employeeId, long historyEntryId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeId) || historyEntryId <= 0)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM employee_history
WHERE id = @id
  AND employee_id = @employeeId;";
            command.Parameters.AddWithValue("@id", historyEntryId);
            command.Parameters.AddWithValue("@employeeId", employeeId);
            command.ExecuteNonQuery();
        }

        public void DeleteEmployeeHistory(string employeeId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeId))
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM employee_history WHERE employee_id = @employeeId;";
            command.Parameters.AddWithValue("@employeeId", employeeId);
            command.ExecuteNonQuery();
        }

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<AdvancePayment>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM advances
WHERE lower(company_id) = lower(@companyId)
  AND month = @monthKey
ORDER BY datetime(date), id;";
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);
            return ReadAdvances(command);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeId, string employeeFolder, string monthKey)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<AdvancePayment>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM advances
WHERE month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY datetime(date), id;";
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeId, string employeeFolder, string firmName, string globalKey, string monthKey)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<AdvancePayment>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM advances
WHERE month = @monthKey
  AND (lower(company_id) = lower(@firmName) OR company_id = @globalKey)
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY datetime(date), id;";
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
            command.Parameters.AddWithValue("@globalKey", globalKey ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string companyId, string monthKey)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0m;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ifnull(SUM(CAST(amount AS REAL)), 0)
FROM advances
WHERE lower(company_id) = lower(@companyId)
  AND month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string monthKey)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0m;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ifnull(SUM(CAST(amount AS REAL)), 0)
FROM advances
WHERE month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey,
            string globalKey)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var initMs = 0L;
            var openConnectionMs = 0L;
            var buildMapsMs = 0L;
            var executeReaderMs = 0L;
            var rowMatchingMs = 0L;
            var rowsRead = 0;
            var matchedRows = 0;
            EnsureInitialized();
            initMs = totalSw.ElapsedMilliseconds;
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (!IsAvailable || requests.Count == 0 || string.IsNullOrWhiteSpace(monthKey))
                return result;

            var buildMapsSw = System.Diagnostics.Stopwatch.StartNew();
            var requestsByEmployeeId = new Dictionary<string, List<(string requestKey, string firmName)>>(StringComparer.OrdinalIgnoreCase);
            var requestsByFolder = new Dictionary<string, List<(string requestKey, string firmName, bool hasEmployeeId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                result[request.requestKey] = 0m;

                var folderKey = ToPortablePath(request.employeeFolder);
                if (!requestsByFolder.TryGetValue(folderKey, out var folderRequests))
                {
                    folderRequests = new List<(string requestKey, string firmName, bool hasEmployeeId)>();
                    requestsByFolder[folderKey] = folderRequests;
                }

                var hasEmployeeId = !string.IsNullOrWhiteSpace(request.employeeId);
                folderRequests.Add((request.requestKey, request.firmName, hasEmployeeId));

                if (!hasEmployeeId)
                    continue;

                if (!requestsByEmployeeId.TryGetValue(request.employeeId, out var employeeRequests))
                {
                    employeeRequests = new List<(string requestKey, string firmName)>();
                    requestsByEmployeeId[request.employeeId] = employeeRequests;
                }

                employeeRequests.Add((request.requestKey, request.firmName));
            }
            buildMapsMs = buildMapsSw.ElapsedMilliseconds;

            var openConnectionSw = System.Diagnostics.Stopwatch.StartNew();
            using var connection = OpenConnection();
            openConnectionMs = openConnectionSw.ElapsedMilliseconds;
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT employee_id, employee_folder, company_id, amount
FROM advances
WHERE month = @monthKey;";
            command.Parameters.AddWithValue("@monthKey", monthKey ?? string.Empty);

            var executeReaderSw = System.Diagnostics.Stopwatch.StartNew();
            using var reader = command.ExecuteReader();
            executeReaderMs = executeReaderSw.ElapsedMilliseconds;
            var rowMatchingSw = System.Diagnostics.Stopwatch.StartNew();
            while (reader.Read())
            {
                rowsRead++;
                var rowEmployeeId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var rowEmployeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var rowCompanyId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var rowAmount = reader.IsDBNull(3) ? 0m : ParseDecimal(reader.GetString(3));
                var matchedRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(rowEmployeeId)
                    && requestsByEmployeeId.TryGetValue(rowEmployeeId, out var employeeMatches))
                {
                    foreach (var employeeMatch in employeeMatches)
                    {
                        if (rowCompanyId == globalKey || string.Equals(rowCompanyId, employeeMatch.firmName, StringComparison.Ordinal))
                            matchedRequestKeys.Add(employeeMatch.requestKey);
                    }
                }

                if (requestsByFolder.TryGetValue(rowEmployeeFolder, out var folderMatches))
                {
                    foreach (var folderMatch in folderMatches)
                    {
                        if ((string.IsNullOrWhiteSpace(rowEmployeeId) || !folderMatch.hasEmployeeId)
                            && (rowCompanyId == globalKey || string.Equals(rowCompanyId, folderMatch.firmName, StringComparison.Ordinal)))
                        {
                            matchedRequestKeys.Add(folderMatch.requestKey);
                        }
                    }
                }

                if (matchedRequestKeys.Count > 0)
                    matchedRows++;

                foreach (var requestKey in matchedRequestKeys)
                    result[requestKey] += rowAmount;
            }
            rowMatchingMs = rowMatchingSw.ElapsedMilliseconds;
            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.LocalDb.GetTotalAdvancesForEmployeeFirms",
                $"GetTotalAdvancesForEmployeeFirms month={monthKey} total={totalSw.ElapsedMilliseconds}ms | " +
                $"init={initMs}ms | buildMaps={buildMapsMs}ms | openConnection={openConnectionMs}ms | " +
                $"executeReader={executeReaderMs}ms | rowMatching={rowMatchingMs}ms | " +
                $"requests={requests.Count} | rowsRead={rowsRead} | matchedRows={matchedRows}");

            return result;
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeId, string employeeFolder)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<AdvancePayment>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM advances
WHERE ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY datetime(date) DESC, id DESC;";
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
        }

        public void InsertAdvance(string employeeId, string employeeFolder, AdvancePayment advance)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertAdvance(connection, transaction, employeeId, employeeFolder, advance);
            transaction.Commit();
        }

        public void DeleteAdvance(string advanceId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(advanceId))
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM advances WHERE id = @id;";
            command.Parameters.AddWithValue("@id", advanceId);
            command.ExecuteNonQuery();
        }

        public void DeleteAdvancesForEmployee(string employeeId, string originalFolder, string deletedFolder)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM advances
WHERE (@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
   OR (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder));";
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@originalFolder", ToPortablePath(originalFolder));
            command.Parameters.AddWithValue("@deletedFolder", ToPortablePath(deletedFolder));
            command.ExecuteNonQuery();
        }

        public int RemapAdvanceEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(toFolder))
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE advances
SET employee_folder = @toFolder,
    employee_id = CASE
        WHEN @employeeId <> '' AND (ifnull(employee_id, '') = '' OR lower(employee_id) = lower(@employeeId))
            THEN @employeeId
        ELSE employee_id
    END
WHERE (
        (@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
        OR (@fromFolderA <> '' AND lower(employee_folder) = lower(@fromFolderA))
        OR (@fromFolderB <> '' AND lower(employee_folder) = lower(@fromFolderB))
      )
  AND lower(employee_folder) <> lower(@toFolder);";
            command.Parameters.AddWithValue("@toFolder", ToPortablePath(toFolder));
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@fromFolderA", ToPortablePath(fromFolderA ?? string.Empty));
            command.Parameters.AddWithValue("@fromFolderB", ToPortablePath(fromFolderB ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public MonthlySalaryReport? GetSalaryReport(string companyId, int year, int month)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return null;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM salary_reports
WHERE lower(company_id) = lower(@companyId)
  AND year = @year
  AND month = @month
LIMIT 1;";
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadSalaryReport(reader) : null;
        }

        public List<MonthlySalaryReport> GetSalaryReportsForCompany(string companyId)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<MonthlySalaryReport>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM salary_reports
WHERE lower(company_id) = lower(@companyId)
ORDER BY year DESC, month DESC, datetime(updated_at) DESC, id DESC;";
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);

            using var reader = command.ExecuteReader();
            var result = new List<MonthlySalaryReport>();
            while (reader.Read())
                result.Add(ReadSalaryReport(reader));

            return result;
        }

        public List<string> GetAvailableReportMonths(string companyId)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<string>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT DISTINCT year, month
FROM salary_reports
WHERE lower(company_id) = lower(@companyId)
ORDER BY year DESC, month DESC;";
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);

            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
            {
                var resultYear = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var resultMonth = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                result.Add($"{resultYear:D4}-{resultMonth:D2}");
            }

            return result;
        }

        public void UpsertSalaryReport(MonthlySalaryReport report)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertSalaryReport(connection, transaction, report);
            transaction.Commit();
        }

        public void RemoveCustomFieldReferencesFromReports(string fieldId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(fieldId))
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var reports = LoadAllSalaryReports(connection);
            var updatedAt = DateTime.Now;

            foreach (var report in reports)
            {
                var changed = false;
                foreach (var entry in report.Entries)
                {
                    if (entry.CustomValues.Remove(fieldId))
                        changed = true;
                }

                if (!changed)
                    continue;

                report.UpdatedAt = updatedAt;
                UpsertSalaryReport(connection, transaction, report);
            }

            transaction.Commit();
        }

        public void RemoveEmployeeEntriesFromReports(string employeeId, string originalFolder, string deletedFolder)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var reports = LoadAllSalaryReports(connection);
            var updatedAt = DateTime.Now;

            foreach (var report in reports)
            {
                var removed = report.Entries.RemoveAll(entry => MatchesReportEntry(entry, employeeId, originalFolder, deletedFolder));
                if (removed <= 0)
                    continue;

                report.UpdatedAt = updatedAt;
                UpsertSalaryReport(connection, transaction, report);
            }

            transaction.Commit();
        }

        public List<CustomSalaryField> GetCustomSalaryFields()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<CustomSalaryField>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, name, operation, firm_name, order_index
FROM custom_salary_fields
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
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO custom_salary_fields (
    id, name, operation, firm_name, order_index
) VALUES (
    @id, @name, @operation, @firmName, @order
)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    operation = excluded.operation,
    firm_name = excluded.firm_name,
    order_index = excluded.order_index;";
            command.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(field.Id) ? Guid.NewGuid().ToString() : field.Id);
            command.Parameters.AddWithValue("@name", field.Name ?? string.Empty);
            command.Parameters.AddWithValue("@operation", (int)field.Operation);
            command.Parameters.AddWithValue("@firmName", field.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@order", field.Order);
            command.ExecuteNonQuery();
        }

        public void DeleteCustomSalaryField(string fieldId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(fieldId))
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM custom_salary_fields WHERE id = @id;";
            command.Parameters.AddWithValue("@id", fieldId);
            command.ExecuteNonQuery();
        }

        public void ReplaceCustomSalaryFields(IReadOnlyList<CustomSalaryField> fields)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM custom_salary_fields;";
                deleteCommand.ExecuteNonQuery();
            }

            foreach (var field in fields ?? Array.Empty<CustomSalaryField>())
                UpsertCustomSalaryField(connection, transaction, field);

            transaction.Commit();
        }

        public void UpsertAccommodation(AccommodationRecord record)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO accommodations (
    id, employee_folder, employee_name, company_id, year, month, amount, address
) VALUES (
    @id, @employeeFolder, @employeeName, @companyId, @year, @month, @amount, @address
)
ON CONFLICT(id) DO UPDATE SET
    employee_folder = excluded.employee_folder,
    employee_name = excluded.employee_name,
    company_id = excluded.company_id,
    year = excluded.year,
    month = excluded.month,
    amount = excluded.amount,
    address = excluded.address;";
            command.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString() : record.Id);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(record.EmployeeFolder));
            command.Parameters.AddWithValue("@employeeName", record.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("@companyId", record.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@year", record.Year);
            command.Parameters.AddWithValue("@month", record.Month);
            command.Parameters.AddWithValue("@amount", ToInvariant(record.Amount));
            command.Parameters.AddWithValue("@address", record.Address ?? string.Empty);
            command.ExecuteNonQuery();
        }

        public decimal GetAccommodationSum(string employeeFolder, string companyId, int year, int month)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0m;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COALESCE(SUM(CAST(amount AS REAL)), 0)
FROM accommodations
WHERE lower(employee_folder) = lower(@employeeFolder)
  AND lower(company_id) = lower(@companyId)
  AND year = @year
  AND month = @month;";
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            command.Parameters.AddWithValue("@companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);

            var result = command.ExecuteScalar();
            return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
        }

        public decimal GetAccommodationSum(string employeeFolder, int year, int month)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0m;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COALESCE(SUM(CAST(amount AS REAL)), 0)
FROM accommodations
WHERE lower(employee_folder) = lower(@employeeFolder)
  AND year = @year
  AND month = @month;";
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);

            var result = command.ExecuteScalar();
            return Convert.ToDecimal(result, CultureInfo.InvariantCulture);
        }

        public int RemoveAccommodationsForEmployee(string employeeFolder)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeFolder))
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM accommodations WHERE lower(employee_folder) = lower(@employeeFolder);";
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            return command.ExecuteNonQuery();
        }

        public int RemapAccommodationEmployeeFolder(string fromFolder, string toFolder)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(fromFolder) || string.IsNullOrWhiteSpace(toFolder))
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE accommodations
SET employee_folder = @toFolder
WHERE lower(employee_folder) = lower(@fromFolder)
  AND lower(employee_folder) <> lower(@toFolder);";
            command.Parameters.AddWithValue("@fromFolder", ToPortablePath(fromFolder));
            command.Parameters.AddWithValue("@toFolder", ToPortablePath(toFolder));
            return command.ExecuteNonQuery();
        }

        public List<AccommodationRecord> GetAllAccommodations()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<AccommodationRecord>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_folder, employee_name, company_id, year, month, amount, address
FROM accommodations
ORDER BY year DESC, month DESC, employee_name, id;";

            using var reader = command.ExecuteReader();
            var result = new List<AccommodationRecord>();
            while (reader.Read())
            {
                result.Add(new AccommodationRecord
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    EmployeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    EmployeeName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    CompanyId = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Year = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    Month = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    Amount = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                    Address = reader.IsDBNull(7) ? string.Empty : reader.GetString(7)
                });
            }

            return result;
        }

        public List<SalaryHistoryRecord> GetSalaryHistory(string employeeId, string employeeFolder)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<SalaryHistoryRecord>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, paid_at, year, month, firm_name, full_name, hours_worked, hourly_rate, gross_salary,
       advance, net_salary, note, custom_values_json, custom_fields_json
FROM salary_history
WHERE ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY year DESC, month DESC, datetime(paid_at) DESC, id DESC;";
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));

            using var reader = command.ExecuteReader();
            var result = new List<SalaryHistoryRecord>();
            while (reader.Read())
            {
                var customValuesJson = reader.IsDBNull(12) ? "{}" : reader.GetString(12);
                var customFieldsJson = reader.IsDBNull(13) ? "[]" : reader.GetString(13);
                var customValues = JsonSerializer.Deserialize<Dictionary<string, decimal>>(customValuesJson)
                                   ?? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                var customFields = JsonSerializer.Deserialize<List<CustomFieldSnapshot>>(customFieldsJson)
                                   ?? new List<CustomFieldSnapshot>();

                result.Add(new SalaryHistoryRecord
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    PaidAt = reader.IsDBNull(1)
                        ? DateTime.Now
                        : (DateTime.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var paidAt)
                            ? paidAt
                            : DateTime.Now),
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
                });
            }

            return result;
        }

        public IReadOnlyList<string> DiscoverDistinctFirmNamesForCompanyFolderPrefixes(
            IReadOnlyCollection<string> companyFolderPrefixes)
        {
            EnsureInitialized();
            if (!IsAvailable || companyFolderPrefixes == null || companyFolderPrefixes.Count == 0)
                return Array.Empty<string>();

            var normalizedPrefixes = companyFolderPrefixes
                .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                .Select(prefix => prefix.Replace('/', '\\').TrimEnd('\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedPrefixes.Count == 0)
                return Array.Empty<string>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT firm_name, employee_folder
FROM salary_history;";

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var firmName = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var employeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(firmName))
                    continue;

                if (SalaryHistoryFolderBelongsToCompanyPrefixes(employeeFolder, normalizedPrefixes))
                    names.Add(firmName);
            }

            return names.ToList();
        }

        private static bool SalaryHistoryFolderBelongsToCompanyPrefixes(
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

        public int RemoveDuplicateSalaryHistoryRecords()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, paid_at, year, month, firm_name
FROM salary_history;";

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
                        PaidAt = reader.IsDBNull(3)
                            ? DateTime.MinValue
                            : (DateTime.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var paidAt)
                                ? paidAt
                                : DateTime.MinValue),
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
                deleteCommand.CommandText = "DELETE FROM salary_history WHERE id = @id;";
                deleteCommand.Parameters.AddWithValue("@id", id);
                removed += deleteCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return removed;
        }

        public void UpsertSalaryHistoryRecord(string employeeId, string employeeFolder, SalaryHistoryRecord record)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertSalaryHistoryRecord(connection, transaction, employeeId, employeeFolder, record);
            transaction.Commit();
        }

        public void DeleteSalaryHistoryRecord(string employeeId, string employeeFolder, int year, int month, string firmName)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM salary_history
WHERE year = @year
  AND month = @month
  AND lower(firm_name) = lower(@firmName)
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("@year", year);
            command.Parameters.AddWithValue("@month", month);
            command.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            command.ExecuteNonQuery();
        }

        public int DeleteSalaryHistoryForEmployee(string? employeeId, string? originalFolder, string? deletedFolder)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM salary_history
WHERE (@employeeId <> '' AND lower(COALESCE(employee_id, '')) = lower(@employeeId))
   OR (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder));";
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@originalFolder", ToPortablePath(originalFolder ?? string.Empty));
            command.Parameters.AddWithValue("@deletedFolder", ToPortablePath(deletedFolder ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public int RemapSalaryHistoryEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(toFolder))
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE salary_history
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
            command.Parameters.AddWithValue("@toFolder", ToPortablePath(toFolder));
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@fromFolderA", ToPortablePath(fromFolderA ?? string.Empty));
            command.Parameters.AddWithValue("@fromFolderB", ToPortablePath(fromFolderB ?? string.Empty));
            return command.ExecuteNonQuery();
        }

        public List<ActivityLogEntry> GetAllActivityLogs()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<ActivityLogEntry>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, timestamp, action_type, category, firm_name, employee_name, employee_folder,
       description, old_value, new_value, details, related_operation_id, actor_name,
       tenant_id, actor_user_id, session_id, machine_id, entity_type, entity_id,
       old_values_json, new_values_json
FROM activity_log
ORDER BY datetime(timestamp) DESC, rowid DESC;";

            using var reader = command.ExecuteReader();
            var result = new List<ActivityLogEntry>();
            while (reader.Read())
            {
                result.Add(new ActivityLogEntry
                {
                    Id = reader.GetString(0),
                    Timestamp = reader.GetString(1),
                    ActionType = reader.GetString(2),
                    Category = reader.GetString(3),
                    FirmName = reader.GetString(4),
                    EmployeeName = reader.GetString(5),
                    EmployeeFolder = reader.GetString(6),
                    Description = reader.GetString(7),
                    OldValue = reader.GetString(8),
                    NewValue = reader.GetString(9),
                    Details = reader.GetString(10),
                    RelatedOperationId = reader.GetString(11),
                    ActorName = reader.GetString(12),
                    TenantId = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                    ActorUserId = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    SessionId = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    MachineId = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                    EntityType = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                    EntityId = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    OldValuesJson = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                    NewValuesJson = reader.IsDBNull(20) ? string.Empty : reader.GetString(20)
                });
            }

            return result;
        }

        public List<ArchiveLogEntry> GetAllArchiveLogs()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new List<ArchiveLogEntry>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT operation_id, employee_name, firm_name, employee_folder, action, date, timestamp,
       is_reverted, reverted_at, reverted_by_operation_id, id
FROM archive_log
ORDER BY datetime(timestamp) DESC, id DESC;";

            using var reader = command.ExecuteReader();
            var result = new List<ArchiveLogEntry>();
            while (reader.Read())
            {
                result.Add(new ArchiveLogEntry
                {
                    OperationId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    EmployeeName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    FirmName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    EmployeeFolder = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    Action = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Date = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                    Timestamp = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    IsReverted = !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                    RevertedAt = reader.IsDBNull(8) ? null : reader.GetString(8),
                    RevertedByOperationId = reader.IsDBNull(9) ? null : reader.GetString(9)
                });
            }

            return result;
        }

        public ArchiveLogEntry? GetActiveArchiveLogEntry(string operationId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(operationId))
                return null;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT operation_id, employee_name, firm_name, employee_folder, action, date, timestamp,
       is_reverted, reverted_at, reverted_by_operation_id
FROM archive_log
WHERE operation_id = @operationId
  AND lower(action) = 'archived'
  AND is_reverted = 0
LIMIT 1;";
            command.Parameters.AddWithValue("@operationId", operationId);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return new ArchiveLogEntry
            {
                OperationId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                EmployeeName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                FirmName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                EmployeeFolder = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Action = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Date = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                Timestamp = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                IsReverted = !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                RevertedAt = reader.IsDBNull(8) ? null : reader.GetString(8),
                RevertedByOperationId = reader.IsDBNull(9) ? null : reader.GetString(9)
            };
        }

        public void InsertArchiveLog(ArchiveLogEntry entry)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertArchiveLog(connection, transaction, entry);
            transaction.Commit();
        }

        public bool MarkArchiveLogReverted(string operationId, string undoOperationId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(operationId))
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE archive_log
SET is_reverted = 1,
    reverted_at = @revertedAt,
    reverted_by_operation_id = @undoOperationId
WHERE operation_id = @operationId
  AND is_reverted = 0;";
            command.Parameters.AddWithValue("@revertedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@undoOperationId", undoOperationId ?? string.Empty);
            command.Parameters.AddWithValue("@operationId", operationId);
            return command.ExecuteNonQuery() > 0;
        }

        public void RemoveActivityLogEntries(string originalFolder, string deletedFolder, string employeeName, string firmName)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM activity_log
WHERE (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder))
   OR (
        ifnull(employee_folder, '') = ''
        AND lower(employee_name) = lower(@employeeName)
        AND lower(firm_name) = lower(@firmName)
      );";

            command.Parameters.AddWithValue("@originalFolder", originalFolder ?? string.Empty);
            command.Parameters.AddWithValue("@deletedFolder", deletedFolder ?? string.Empty);
            command.Parameters.AddWithValue("@employeeName", employeeName ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", firmName ?? string.Empty);
            command.ExecuteNonQuery();
        }

        public void ClearActivityLogs()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM activity_log;";
            command.ExecuteNonQuery();
        }

        public void RecordMigrationJournal(string stage, string status, int recordsFound, int recordsImported, string? errorMessage, int foldersScanned = 0, int foldersSkipped = 0)
        {
            InsertMigrationJournal(stage, status, recordsFound, recordsImported, errorMessage, foldersScanned, foldersSkipped);
        }

        private void InsertActivityLog(SqliteConnection connection, SqliteTransaction transaction, ActivityLogEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO activity_log (
    id, timestamp, action_type, category, firm_name, employee_name, employee_folder,
    description, old_value, new_value, details, related_operation_id, actor_name,
    tenant_id, actor_user_id, session_id, machine_id, entity_type, entity_id,
    old_values_json, new_values_json
) VALUES (
    @id, @timestamp, @actionType, @category, @firmName, @employeeName, @employeeFolder,
    @description, @oldValue, @newValue, @details, @relatedOperationId, @actorName,
    @tenantId, @actorUserId, @sessionId, @machineId, @entityType, @entityId,
    @oldValuesJson, @newValuesJson
)
ON CONFLICT(id) DO UPDATE SET
    timestamp = excluded.timestamp,
    action_type = excluded.action_type,
    category = excluded.category,
    firm_name = excluded.firm_name,
    employee_name = excluded.employee_name,
    employee_folder = excluded.employee_folder,
    description = excluded.description,
    old_value = excluded.old_value,
    new_value = excluded.new_value,
    details = excluded.details,
    related_operation_id = excluded.related_operation_id,
    actor_name = excluded.actor_name,
    tenant_id = excluded.tenant_id,
    actor_user_id = excluded.actor_user_id,
    session_id = excluded.session_id,
    machine_id = excluded.machine_id,
    entity_type = excluded.entity_type,
    entity_id = excluded.entity_id,
    old_values_json = excluded.old_values_json,
    new_values_json = excluded.new_values_json;";

            command.Parameters.AddWithValue("@id", entry.Id ?? Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("@timestamp", entry.Timestamp ?? string.Empty);
            command.Parameters.AddWithValue("@actionType", entry.ActionType ?? string.Empty);
            command.Parameters.AddWithValue("@category", entry.Category ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeName", entry.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("@description", entry.Description ?? string.Empty);
            command.Parameters.AddWithValue("@oldValue", entry.OldValue ?? string.Empty);
            command.Parameters.AddWithValue("@newValue", entry.NewValue ?? string.Empty);
            command.Parameters.AddWithValue("@details", entry.Details ?? string.Empty);
            command.Parameters.AddWithValue("@relatedOperationId", entry.RelatedOperationId ?? string.Empty);
            command.Parameters.AddWithValue("@actorName", entry.ActorName ?? string.Empty);
            command.Parameters.AddWithValue("@tenantId", entry.TenantId ?? string.Empty);
            command.Parameters.AddWithValue("@actorUserId", entry.ActorUserId ?? string.Empty);
            command.Parameters.AddWithValue("@sessionId", entry.SessionId ?? string.Empty);
            command.Parameters.AddWithValue("@machineId", entry.MachineId ?? string.Empty);
            command.Parameters.AddWithValue("@entityType", entry.EntityType ?? string.Empty);
            command.Parameters.AddWithValue("@entityId", entry.EntityId ?? string.Empty);
            command.Parameters.AddWithValue("@oldValuesJson", entry.OldValuesJson ?? string.Empty);
            command.Parameters.AddWithValue("@newValuesJson", entry.NewValuesJson ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private void InsertArchiveLog(SqliteConnection connection, SqliteTransaction transaction, ArchiveLogEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO archive_log (
    operation_id, employee_name, firm_name, employee_folder, action, date, timestamp,
    is_reverted, reverted_at, reverted_by_operation_id
) VALUES (
    @operationId, @employeeName, @firmName, @employeeFolder, @action, @date, @timestamp,
    @isReverted, @revertedAt, @revertedByOperationId
);";

            command.Parameters.AddWithValue("@operationId", entry.OperationId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeName", entry.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", entry.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", entry.EmployeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("@action", entry.Action ?? string.Empty);
            command.Parameters.AddWithValue("@date", entry.Date ?? string.Empty);
            command.Parameters.AddWithValue("@timestamp", entry.Timestamp ?? string.Empty);
            command.Parameters.AddWithValue("@isReverted", entry.IsReverted ? 1 : 0);
            command.Parameters.AddWithValue("@revertedAt", (object?)entry.RevertedAt ?? DBNull.Value);
            command.Parameters.AddWithValue("@revertedByOperationId", (object?)entry.RevertedByOperationId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static void TrimActivityLog(SqliteConnection connection, SqliteTransaction transaction, int maxEntries)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM activity_log
WHERE id IN (
    SELECT id
    FROM activity_log
    ORDER BY datetime(timestamp) DESC, rowid DESC
    LIMIT -1 OFFSET @maxEntries
);";
            command.Parameters.AddWithValue("@maxEntries", maxEntries);
            command.ExecuteNonQuery();
        }

        private void CreateSchema(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS migration_journal (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stage TEXT NOT NULL,
    status TEXT NOT NULL,
    records_found INTEGER NOT NULL DEFAULT 0,
    records_imported INTEGER NOT NULL DEFAULT 0,
    folders_scanned INTEGER NOT NULL DEFAULT 0,
    folders_skipped INTEGER NOT NULL DEFAULT 0,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    error_message TEXT
);

CREATE TABLE IF NOT EXISTS activity_log (
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
    actor_name TEXT NOT NULL,
    tenant_id TEXT,
    actor_user_id TEXT,
    session_id TEXT,
    machine_id TEXT,
    entity_type TEXT,
    entity_id TEXT,
    old_values_json TEXT,
    new_values_json TEXT
);

CREATE TABLE IF NOT EXISTS archive_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    operation_id TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    action TEXT NOT NULL,
    date TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    is_reverted INTEGER NOT NULL DEFAULT 0,
    reverted_at TEXT,
    reverted_by_operation_id TEXT
);

CREATE TABLE IF NOT EXISTS employee_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_id TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    firm_name TEXT,
    timestamp TEXT NOT NULL,
    event_type TEXT NOT NULL,
    action TEXT NOT NULL,
    field TEXT NOT NULL,
    old_value TEXT NOT NULL,
    new_value TEXT NOT NULL,
    description TEXT NOT NULL,
    actor_name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS salary_history (
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
    custom_values_json TEXT NOT NULL DEFAULT '{{}}',
    custom_fields_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS advances (
    id TEXT PRIMARY KEY,
    employee_id TEXT NOT NULL DEFAULT '',
    employee_folder TEXT NOT NULL,
    employee_name TEXT NOT NULL DEFAULT '',
    company_id TEXT NOT NULL,
    date TEXT NOT NULL,
    amount TEXT NOT NULL DEFAULT '0',
    month TEXT NOT NULL,
    note TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS salary_reports (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    company_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    entries_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS custom_salary_fields (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    operation INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    order_index INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS accommodations (
    id TEXT PRIMARY KEY,
    employee_folder TEXT NOT NULL,
    employee_name TEXT NOT NULL DEFAULT '',
    company_id TEXT NOT NULL DEFAULT '',
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    amount TEXT NOT NULL DEFAULT '0',
    address TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_activity_log_timestamp ON activity_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_activity_log_employee_folder ON activity_log(employee_folder);
CREATE INDEX IF NOT EXISTS idx_activity_log_firm_employee ON activity_log(firm_name, employee_name);
CREATE INDEX IF NOT EXISTS idx_archive_log_operation_id ON archive_log(operation_id);
CREATE INDEX IF NOT EXISTS idx_archive_log_is_reverted ON archive_log(is_reverted);
CREATE INDEX IF NOT EXISTS idx_archive_log_timestamp ON archive_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_eh_employee ON employee_history(employee_id);
CREATE INDEX IF NOT EXISTS idx_eh_type ON employee_history(employee_id, event_type);
CREATE INDEX IF NOT EXISTS idx_eh_timestamp ON employee_history(employee_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_sh_employee ON salary_history(employee_id);
CREATE INDEX IF NOT EXISTS idx_sh_folder ON salary_history(employee_folder);
CREATE INDEX IF NOT EXISTS idx_sh_period ON salary_history(year, month);
CREATE INDEX IF NOT EXISTS idx_sh_firm ON salary_history(firm_name);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sh_dedup ON salary_history(
    COALESCE(employee_id, ''), employee_folder, year, month, firm_name
);
CREATE INDEX IF NOT EXISTS idx_adv_month ON advances(month);
CREATE INDEX IF NOT EXISTS idx_adv_employee_folder ON advances(employee_folder, month);
CREATE INDEX IF NOT EXISTS idx_adv_employee_id ON advances(employee_id, month
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sr_company_period ON salary_reports(
    company_id, year, month
);
CREATE INDEX IF NOT EXISTS idx_sr_company_updated ON salary_reports(company_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_sr_period ON salary_reports(year, month
);
CREATE INDEX IF NOT EXISTS idx_csf_firm_order ON custom_salary_fields(firm_name, order_index);
CREATE INDEX IF NOT EXISTS idx_csf_order ON custom_salary_fields(order_index
);
CREATE INDEX IF NOT EXISTS idx_acc_emp_comp_ym ON accommodations(employee_folder, company_id, year, month);
CREATE INDEX IF NOT EXISTS idx_acc_emp_ym ON accommodations(employee_folder, year, month
);";

            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "migration_journal", "folders_scanned", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "migration_journal", "folders_skipped", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "activity_log", "tenant_id", "TEXT");
            EnsureColumnExists(connection, "activity_log", "actor_user_id", "TEXT");
            EnsureColumnExists(connection, "activity_log", "session_id", "TEXT");
            EnsureColumnExists(connection, "activity_log", "machine_id", "TEXT");
            EnsureColumnExists(connection, "activity_log", "entity_type", "TEXT");
            EnsureColumnExists(connection, "activity_log", "entity_id", "TEXT");
            EnsureColumnExists(connection, "activity_log", "old_values_json", "TEXT");
            EnsureColumnExists(connection, "activity_log", "new_values_json", "TEXT");

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM schema_version;";
            var hasVersion = Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            if (!hasVersion)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO schema_version(version) VALUES (@version);";
                insertCommand.Parameters.AddWithValue("@version", CurrentSchemaVersion);
                insertCommand.ExecuteNonQuery();
            }
        }

        private void InsertMigrationJournal(string stage, string status, int recordsFound, int recordsImported, string? errorMessage, int foldersScanned = 0, int foldersSkipped = 0)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            command.CommandText = @"
INSERT INTO migration_journal (
    stage, status, records_found, records_imported, folders_scanned, folders_skipped, started_at, completed_at, error_message
) VALUES (
    @stage, @status, @recordsFound, @recordsImported, @foldersScanned, @foldersSkipped, @startedAt, @completedAt, @errorMessage
);";

            command.Parameters.AddWithValue("@stage", stage);
            command.Parameters.AddWithValue("@status", status);
            command.Parameters.AddWithValue("@recordsFound", recordsFound);
            command.Parameters.AddWithValue("@recordsImported", recordsImported);
            command.Parameters.AddWithValue("@foldersScanned", foldersScanned);
            command.Parameters.AddWithValue("@foldersSkipped", foldersSkipped);
            command.Parameters.AddWithValue("@startedAt", now);
            command.Parameters.AddWithValue("@completedAt", status == "started" ? DBNull.Value : now);
            command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string columnDefinition)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"PRAGMA table_info({tableName});";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private void InsertEmployeeHistory(SqliteConnection connection, SqliteTransaction transaction, string employeeId, string employeeFolder, string firmName, EmployeeHistoryEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO employee_history (
    employee_id, employee_folder, firm_name, timestamp, event_type, action, field, old_value, new_value, description, actor_name
) VALUES (
    @employeeId, @employeeFolder, @firmName, @timestamp, @eventType, @action, @field, @oldValue, @newValue, @description, @actorName
);";

            command.Parameters.AddWithValue("@employeeId", employeeId);
            command.Parameters.AddWithValue("@employeeFolder", employeeFolder ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", (object?)firmName ?? DBNull.Value);
            command.Parameters.AddWithValue("@timestamp", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@eventType", entry.EventType ?? string.Empty);
            command.Parameters.AddWithValue("@action", entry.Action ?? string.Empty);
            command.Parameters.AddWithValue("@field", entry.Field ?? string.Empty);
            command.Parameters.AddWithValue("@oldValue", entry.OldValue ?? string.Empty);
            command.Parameters.AddWithValue("@newValue", entry.NewValue ?? string.Empty);
            command.Parameters.AddWithValue("@description", entry.Description ?? string.Empty);
            command.Parameters.AddWithValue("@actorName", entry.ActorName ?? string.Empty);
            command.ExecuteNonQuery();

            using var idCommand = connection.CreateCommand();
            idCommand.Transaction = transaction;
            idCommand.CommandText = "SELECT last_insert_rowid();";
            entry.Id = Convert.ToInt64(idCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        private void UpsertSalaryHistoryRecord(SqliteConnection connection, SqliteTransaction transaction, string employeeId, string employeeFolder, SalaryHistoryRecord record)
        {
            var recordId = string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString() : record.Id;
            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = @"
DELETE FROM salary_history
WHERE year = @year
  AND month = @month
  AND lower(trim(firm_name)) = lower(trim(@firmName))
  AND ((@employeeId <> '' AND lower(COALESCE(employee_id, '')) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
  AND id <> @id;";
                deleteCommand.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
                deleteCommand.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
                deleteCommand.Parameters.AddWithValue("@year", record.Year);
                deleteCommand.Parameters.AddWithValue("@month", record.Month);
                deleteCommand.Parameters.AddWithValue("@firmName", record.FirmName ?? string.Empty);
                deleteCommand.Parameters.AddWithValue("@id", recordId);
                deleteCommand.ExecuteNonQuery();
            }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary_history (
    id, employee_id, employee_folder, year, month, firm_name, full_name, paid_at,
    hours_worked, hourly_rate, gross_salary, advance, net_salary, note, custom_values_json, custom_fields_json
) VALUES (
    @id, @employeeId, @employeeFolder, @year, @month, @firmName, @fullName, @paidAt,
    @hoursWorked, @hourlyRate, @grossSalary, @advance, @netSalary, @note, @customValuesJson, @customFieldsJson
)
ON CONFLICT(id) DO UPDATE SET
    employee_id = excluded.employee_id,
    employee_folder = excluded.employee_folder,
    year = excluded.year,
    month = excluded.month,
    firm_name = excluded.firm_name,
    full_name = excluded.full_name,
    paid_at = excluded.paid_at,
    hours_worked = excluded.hours_worked,
    hourly_rate = excluded.hourly_rate,
    gross_salary = excluded.gross_salary,
    advance = excluded.advance,
    net_salary = excluded.net_salary,
    note = excluded.note,
    custom_values_json = excluded.custom_values_json,
    custom_fields_json = excluded.custom_fields_json;";

            command.Parameters.AddWithValue("@id", recordId);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            command.Parameters.AddWithValue("@year", record.Year);
            command.Parameters.AddWithValue("@month", record.Month);
            command.Parameters.AddWithValue("@firmName", record.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@fullName", record.FullName ?? string.Empty);
            command.Parameters.AddWithValue("@paidAt", record.PaidAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@hoursWorked", ToInvariant(record.HoursWorked));
            command.Parameters.AddWithValue("@hourlyRate", ToInvariant(record.HourlyRate));
            command.Parameters.AddWithValue("@grossSalary", ToInvariant(record.GrossSalary));
            command.Parameters.AddWithValue("@advance", ToInvariant(record.Advance));
            command.Parameters.AddWithValue("@netSalary", ToInvariant(record.NetSalary));
            command.Parameters.AddWithValue("@note", record.Note ?? string.Empty);
            command.Parameters.AddWithValue("@customValuesJson", JsonSerializer.Serialize(record.CustomValues ?? new Dictionary<string, decimal>()));
            command.Parameters.AddWithValue("@customFieldsJson", JsonSerializer.Serialize(record.CustomFields ?? new List<CustomFieldSnapshot>()));
            command.ExecuteNonQuery();
        }

        private void UpsertSalaryReport(SqliteConnection connection, SqliteTransaction transaction, MonthlySalaryReport report)
        {
            var normalized = CloneReport(report);
            if (string.IsNullOrWhiteSpace(normalized.Id))
                normalized.Id = Guid.NewGuid().ToString();
            if (normalized.CreatedAt == default)
                normalized.CreatedAt = DateTime.Now;
            if (normalized.UpdatedAt == default)
                normalized.UpdatedAt = normalized.CreatedAt;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO salary_reports (
    id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
) VALUES (
    @id, @companyId, @companyName, @year, @month, @notes, @createdAt, @updatedAt, @entriesJson
)
ON CONFLICT(company_id, year, month) DO UPDATE SET
    id = excluded.id,
    company_name = excluded.company_name,
    notes = excluded.notes,
    updated_at = excluded.updated_at,
    entries_json = excluded.entries_json;";
            command.Parameters.AddWithValue("@id", normalized.Id);
            command.Parameters.AddWithValue("@companyId", normalized.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@companyName", normalized.CompanyName ?? string.Empty);
            command.Parameters.AddWithValue("@year", normalized.Year);
            command.Parameters.AddWithValue("@month", normalized.Month);
            command.Parameters.AddWithValue("@notes", normalized.Notes ?? string.Empty);
            command.Parameters.AddWithValue("@createdAt", normalized.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@updatedAt", normalized.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@entriesJson", SerializeReportEntries(normalized.Entries));
            command.ExecuteNonQuery();
        }

        private void UpsertCustomSalaryField(SqliteConnection connection, SqliteTransaction transaction, CustomSalaryField field)
        {
            var normalizedId = string.IsNullOrWhiteSpace(field.Id) ? Guid.NewGuid().ToString() : field.Id;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO custom_salary_fields (
    id, name, operation, firm_name, order_index
) VALUES (
    @id, @name, @operation, @firmName, @order
)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    operation = excluded.operation,
    firm_name = excluded.firm_name,
    order_index = excluded.order_index;";
            command.Parameters.AddWithValue("@id", normalizedId);
            command.Parameters.AddWithValue("@name", field.Name ?? string.Empty);
            command.Parameters.AddWithValue("@operation", (int)field.Operation);
            command.Parameters.AddWithValue("@firmName", field.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@order", field.Order);
            command.ExecuteNonQuery();
        }

        private void UpsertAccommodation(SqliteConnection connection, SqliteTransaction transaction, AccommodationRecord record)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO accommodations (
    id, employee_folder, employee_name, company_id, year, month, amount, address
) VALUES (
    @id, @employeeFolder, @employeeName, @companyId, @year, @month, @amount, @address
)
ON CONFLICT(id) DO UPDATE SET
    employee_folder = excluded.employee_folder,
    employee_name = excluded.employee_name,
    company_id = excluded.company_id,
    year = excluded.year,
    month = excluded.month,
    amount = excluded.amount,
    address = excluded.address;";
            command.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(record.Id) ? Guid.NewGuid().ToString() : record.Id);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(record.EmployeeFolder));
            command.Parameters.AddWithValue("@employeeName", record.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("@companyId", record.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@year", record.Year);
            command.Parameters.AddWithValue("@month", record.Month);
            command.Parameters.AddWithValue("@amount", ToInvariant(record.Amount));
            command.Parameters.AddWithValue("@address", record.Address ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private void UpsertAdvance(SqliteConnection connection, SqliteTransaction transaction, string employeeId, string employeeFolder, AdvancePayment advance)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO advances (
    id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
) VALUES (
    @id, @employeeId, @employeeFolder, @employeeName, @companyId, @date, @amount, @month, @note
)
ON CONFLICT(id) DO UPDATE SET
    employee_id = excluded.employee_id,
    employee_folder = excluded.employee_folder,
    employee_name = excluded.employee_name,
    company_id = excluded.company_id,
    date = excluded.date,
    amount = excluded.amount,
    month = excluded.month,
    note = excluded.note;";

            command.Parameters.AddWithValue("@id", string.IsNullOrWhiteSpace(advance.Id) ? Guid.NewGuid().ToString() : advance.Id);
            command.Parameters.AddWithValue("@employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(employeeFolder));
            command.Parameters.AddWithValue("@employeeName", advance.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("@companyId", advance.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("@date", advance.Date.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("@amount", ToInvariant(advance.Amount));
            command.Parameters.AddWithValue("@month", advance.Month ?? string.Empty);
            command.Parameters.AddWithValue("@note", advance.Note ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private List<AdvancePayment> ReadAdvances(SqliteCommand command)
        {
            var advances = new List<AdvancePayment>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                advances.Add(new AdvancePayment
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    EmployeeFolder = ResolveStoredPath(reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
                    EmployeeName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    CompanyId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Date = reader.IsDBNull(5)
                        ? DateTime.Now
                        : (DateTime.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate)
                            ? parsedDate
                            : DateTime.Now),
                    Amount = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                    Month = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    Note = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                });
            }

            return advances;
        }

        private List<MonthlySalaryReport> LoadAllSalaryReports(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM salary_reports
ORDER BY year DESC, month DESC, datetime(updated_at) DESC, id DESC;";

            using var reader = command.ExecuteReader();
            var reports = new List<MonthlySalaryReport>();
            while (reader.Read())
                reports.Add(ReadSalaryReport(reader));

            return reports;
        }

        private MonthlySalaryReport ReadSalaryReport(SqliteDataReader reader)
        {
            var entriesJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8);
            return new MonthlySalaryReport
            {
                Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                CompanyId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                CompanyName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Year = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Month = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                Notes = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                CreatedAt = ParseDateTime(reader.IsDBNull(6) ? string.Empty : reader.GetString(6)),
                UpdatedAt = ParseDateTime(reader.IsDBNull(7) ? string.Empty : reader.GetString(7)),
                Entries = DeserializeReportEntries(entriesJson)
            };
        }

        private static void TrimEmployeeHistory(SqliteConnection connection, SqliteTransaction transaction, string employeeId, int maxEntriesPerEmployee)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
DELETE FROM employee_history
WHERE id IN (
    SELECT id
    FROM employee_history
    WHERE employee_id = @employeeId
    ORDER BY datetime(timestamp) DESC, id DESC
    LIMIT -1 OFFSET @maxEntries
);";
            command.Parameters.AddWithValue("@employeeId", employeeId);
            command.Parameters.AddWithValue("@maxEntries", maxEntriesPerEmployee);
            command.ExecuteNonQuery();
        }

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

        private string ResolveStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            if (Path.IsPathRooted(path))
                return NormalizeFullPath(path);

            var rootPath = NormalizeFullPath(_folderService.RootPath);
            if (string.IsNullOrWhiteSpace(rootPath))
                return path;

            return NormalizeFullPath(Path.Combine(rootPath, path));
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

        private static string ToInvariant(decimal value)
            => value.ToString(CultureInfo.InvariantCulture);

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

            LoggingService.LogWarning("LocalDbService.ParseDecimal", $"Failed to parse decimal value '{value}'. Using 0.");
            return 0m;
        }

        private static MonthlySalaryReport CloneReport(MonthlySalaryReport report)
        {
            return new MonthlySalaryReport
            {
                Id = report.Id,
                Year = report.Year,
                Month = report.Month,
                CompanyId = report.CompanyId,
                CompanyName = report.CompanyName,
                Entries = SalaryEntryCloneHelper.CloneEntries(report.Entries ?? new List<SalaryEntry>()),
                CreatedAt = report.CreatedAt,
                UpdatedAt = report.UpdatedAt,
                Notes = report.Notes
            };
        }

        private string SerializeReportEntries(IEnumerable<SalaryEntry> entries)
        {
            var clonedEntries = SalaryEntryCloneHelper.CloneEntries(entries ?? Array.Empty<SalaryEntry>());
            foreach (var entry in clonedEntries)
                entry.EmployeeFolder = ToPortablePath(entry.EmployeeFolder);

            return JsonSerializer.Serialize(clonedEntries);
        }

        private List<SalaryEntry> DeserializeReportEntries(string json)
        {
            var entries = JsonSerializer.Deserialize<List<SalaryEntry>>(json) ?? new List<SalaryEntry>();
            foreach (var entry in entries)
                entry.EmployeeFolder = ResolveStoredPath(entry.EmployeeFolder);

            return entries;
        }

        private bool MatchesReportEntry(SalaryEntry entry, string employeeId, string originalFolder, string deletedFolder)
        {
            if (!string.IsNullOrWhiteSpace(employeeId)
                && string.Equals(entry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedEntryFolder = NormalizeFullPath(entry.EmployeeFolder);
            var normalizedOriginalFolder = NormalizeFullPath(originalFolder);
            var normalizedDeletedFolder = NormalizeFullPath(deletedFolder);

            return (!string.IsNullOrWhiteSpace(normalizedOriginalFolder)
                    && string.Equals(normalizedEntryFolder, normalizedOriginalFolder, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(normalizedDeletedFolder)
                    && string.Equals(normalizedEntryFolder, normalizedDeletedFolder, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class EmployeeHistoryMigrationSource
    {
        public string EmployeeId { get; init; } = string.Empty;
        public string EmployeeFolder { get; init; } = string.Empty;
        public string FirmName { get; init; } = string.Empty;
        public string HistoryJsonPath { get; init; } = string.Empty;
        public IReadOnlyList<EmployeeHistoryEntry> Entries { get; init; } = Array.Empty<EmployeeHistoryEntry>();
    }

    public sealed class SalaryHistoryMigrationSource
    {
        public string EmployeeId { get; init; } = string.Empty;
        public string EmployeeFolder { get; init; } = string.Empty;
        public string HistoryJsonPath { get; init; } = string.Empty;
        public IReadOnlyList<SalaryHistoryRecord> Records { get; init; } = Array.Empty<SalaryHistoryRecord>();
    }

    public sealed class AdvanceMigrationSource
    {
        public string EmployeeId { get; init; } = string.Empty;
        public string EmployeeFolder { get; init; } = string.Empty;
        public AdvancePayment Advance { get; init; } = new();
        public IReadOnlyList<AdvancePayment> Advances => new[] { Advance };
    }
}

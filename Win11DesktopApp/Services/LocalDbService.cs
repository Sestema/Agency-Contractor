using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.EmployeeModels;

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
        private readonly object _initLock = new();
        private bool _isInitialized;

        public LocalDbService(FolderService folderService)
        {
            _folderService = folderService;
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

            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        public bool IsActivityLogMigrationCompleted()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM migration_journal
WHERE stage = 'activity_log'
  AND status = 'completed';";

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool HasActivityLogEntries()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM activity_log;";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool IsEmployeeHistoryMigrationCompleted()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM migration_journal
WHERE stage = 'employee_history'
  AND status = 'completed';";

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool IsArchiveLogMigrationCompleted()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COUNT(1)
FROM migration_journal
WHERE stage = 'archive_log'
  AND status = 'completed';";

            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool HasEmployeeHistoryEntries(string employeeId)
        {
            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeId))
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM employee_history WHERE employee_id = @employeeId;";
            command.Parameters.AddWithValue("@employeeId", employeeId);
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool HasArchiveLogEntries()
        {
            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM archive_log;";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
        }

        public bool CleanupMigratedActivityLogBackup()
        {
            EnsureInitialized();
            if (!IsAvailable || !IsActivityLogMigrationCompleted() || !HasActivityLogEntries())
                return false;

            var rootPath = _folderService.RootPath;
            if (string.IsNullOrWhiteSpace(rootPath))
                return false;

            return TryDeleteMigratedBackup(Path.Combine(rootPath, "activity_log.json"), "LocalDbService.CleanupMigratedActivityLogBackup");
        }

        public bool CleanupMigratedArchiveLogBackup()
        {
            EnsureInitialized();
            if (!IsAvailable || !IsArchiveLogMigrationCompleted() || !HasArchiveLogEntries())
                return false;

            var archiveFolder = _folderService.GetArchiveFolder();
            if (string.IsNullOrWhiteSpace(archiveFolder))
                return false;

            return TryDeleteMigratedBackup(Path.Combine(archiveFolder, "archive_log.json"), "LocalDbService.CleanupMigratedArchiveLogBackup");
        }

        public int CleanupMigratedEmployeeHistoryBackups(IEnumerable<EmployeeHistoryMigrationSource> sources)
        {
            EnsureInitialized();
            if (!IsAvailable || !IsEmployeeHistoryMigrationCompleted())
                return 0;

            var deletedCount = 0;
            foreach (var source in sources)
            {
                if (string.IsNullOrWhiteSpace(source.EmployeeId) || !HasEmployeeHistoryEntries(source.EmployeeId))
                    continue;

                if (TryDeleteMigratedBackup(source.HistoryJsonPath, "LocalDbService.CleanupMigratedEmployeeHistoryBackups"))
                    deletedCount++;
            }

            return deletedCount;
        }

        public LocalDbMigrationResult MigrateActivityLogIfNeeded(string jsonPath, IReadOnlyList<ActivityLogEntry> sourceEntries)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new LocalDbMigrationResult { Message = "SQLite path is unavailable." };

            if (IsActivityLogMigrationCompleted())
            {
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = false,
                    IsSuccessful = true,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = sourceEntries.Count,
                    Message = "Activity log migration already completed."
                };
            }

            if (sourceEntries.Count == 0)
            {
                if (!HasActivityLogEntries())
                    InsertMigrationJournal("activity_log", "completed", 0, 0, null);

                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    Message = "No activity log entries found for migration."
                };
            }

            InsertMigrationJournal("activity_log", "started", sourceEntries.Count, 0, null);

            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                using (var clearCommand = connection.CreateCommand())
                {
                    clearCommand.Transaction = transaction;
                    clearCommand.CommandText = "DELETE FROM activity_log;";
                    clearCommand.ExecuteNonQuery();
                }

                foreach (var entry in sourceEntries)
                    InsertActivityLog(connection, transaction, entry);

                transaction.Commit();

                var importedCount = GetActivityLogCount();
                if (importedCount != sourceEntries.Count)
                    throw new InvalidOperationException($"Activity log migration mismatch: expected {sourceEntries.Count}, imported {importedCount}.");

                var backupPath = RenameMigratedJson(jsonPath);
                InsertMigrationJournal("activity_log", "completed", sourceEntries.Count, importedCount, null);

                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = importedCount,
                    Message = $"Migrated {importedCount} activity log entries to SQLite.",
                    BackupPath = backupPath
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("LocalDbService.MigrateActivityLogIfNeeded", ex);
                InsertMigrationJournal("activity_log", "failed", sourceEntries.Count, 0, ex.Message);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = 0,
                    Message = ex.Message
                };
            }
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

        public LocalDbMigrationResult MigrateEmployeeHistoryIfNeeded(IEnumerable<EmployeeHistoryMigrationSource> sources)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new LocalDbMigrationResult { Message = "SQLite path is unavailable." };

            if (IsEmployeeHistoryMigrationCompleted())
            {
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = false,
                    IsSuccessful = true,
                    Message = "Employee history migration already completed."
                };
            }

            var sourceList = new List<EmployeeHistoryMigrationSource>(sources);
            var foldersScanned = 0;
            var foldersSkipped = 0;
            var recordsFound = 0;
            var recordsImported = 0;

            InsertMigrationJournal("employee_history", "started", 0, 0, null, 0, 0);

            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                foreach (var source in sourceList)
                {
                    foldersScanned++;

                    if (string.IsNullOrWhiteSpace(source.HistoryJsonPath)
                        || !File.Exists(source.HistoryJsonPath)
                        || File.Exists(source.HistoryJsonPath + ".migrated"))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(source.EmployeeId))
                    {
                        foldersSkipped++;
                        LoggingService.LogWarning("LocalDbService.MigrateEmployeeHistoryIfNeeded",
                            $"Skipped history migration because UniqueId is missing: {source.EmployeeFolder}");
                        continue;
                    }

                    var entries = source.Entries ?? new List<EmployeeHistoryEntry>();
                    recordsFound += entries.Count;

                    foreach (var entry in entries)
                    {
                        InsertEmployeeHistory(connection, transaction, source.EmployeeId, source.EmployeeFolder, source.FirmName, entry);
                        recordsImported++;
                    }
                }

                transaction.Commit();

                if (recordsImported != recordsFound)
                    throw new InvalidOperationException($"Employee history migration mismatch: expected {recordsFound}, imported {recordsImported}.");

                foreach (var source in sourceList)
                {
                    if (!string.IsNullOrWhiteSpace(source.EmployeeId)
                        && !string.IsNullOrWhiteSpace(source.HistoryJsonPath)
                        && File.Exists(source.HistoryJsonPath)
                        && !File.Exists(source.HistoryJsonPath + ".migrated"))
                    {
                        RenameMigratedJson(source.HistoryJsonPath);
                    }
                }

                InsertMigrationJournal("employee_history", "completed", recordsFound, recordsImported, null, foldersScanned, foldersSkipped);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = $"Migrated {recordsImported} employee history records to SQLite."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("LocalDbService.MigrateEmployeeHistoryIfNeeded", ex);
                InsertMigrationJournal("employee_history", "failed", recordsFound, recordsImported, ex.Message, foldersScanned, foldersSkipped);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = ex.Message
                };
            }
        }

        public LocalDbMigrationResult MigrateArchiveLogIfNeeded(string jsonPath, IReadOnlyList<ArchiveLogEntry> sourceEntries)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return new LocalDbMigrationResult { Message = "SQLite path is unavailable." };

            if (IsArchiveLogMigrationCompleted())
            {
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = false,
                    IsSuccessful = true,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = sourceEntries.Count,
                    Message = "Archive log migration already completed."
                };
            }

            if (sourceEntries.Count == 0)
            {
                if (!HasArchiveLogEntries())
                    InsertMigrationJournal("archive_log", "completed", 0, 0, null);

                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    Message = "No archive log entries found for migration."
                };
            }

            InsertMigrationJournal("archive_log", "started", sourceEntries.Count, 0, null);

            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                using (var clearCommand = connection.CreateCommand())
                {
                    clearCommand.Transaction = transaction;
                    clearCommand.CommandText = "DELETE FROM archive_log;";
                    clearCommand.ExecuteNonQuery();
                }

                foreach (var entry in sourceEntries)
                    InsertArchiveLog(connection, transaction, entry);

                transaction.Commit();

                var importedCount = GetArchiveLogCount();
                if (importedCount != sourceEntries.Count)
                    throw new InvalidOperationException($"Archive log migration mismatch: expected {sourceEntries.Count}, imported {importedCount}.");

                var backupPath = RenameMigratedJson(jsonPath);
                InsertMigrationJournal("archive_log", "completed", sourceEntries.Count, importedCount, null);

                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = importedCount,
                    Message = $"Migrated {importedCount} archive log entries to SQLite.",
                    BackupPath = backupPath
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("LocalDbService.MigrateArchiveLogIfNeeded", ex);
                InsertMigrationJournal("archive_log", "failed", sourceEntries.Count, 0, ex.Message);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    RecordsFound = sourceEntries.Count,
                    RecordsImported = 0,
                    Message = ex.Message
                };
            }
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
       description, old_value, new_value, details, related_operation_id, actor_name
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
                    ActorName = reader.GetString(12)
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

        private int GetActivityLogCount()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM activity_log;";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private int GetArchiveLogCount()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM archive_log;";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private void InsertActivityLog(SqliteConnection connection, SqliteTransaction transaction, ActivityLogEntry entry)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO activity_log (
    id, timestamp, action_type, category, firm_name, employee_name, employee_folder,
    description, old_value, new_value, details, related_operation_id, actor_name
) VALUES (
    @id, @timestamp, @actionType, @category, @firmName, @employeeName, @employeeFolder,
    @description, @oldValue, @newValue, @details, @relatedOperationId, @actorName
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
    actor_name = excluded.actor_name;";

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
    actor_name TEXT NOT NULL
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

CREATE INDEX IF NOT EXISTS idx_activity_log_timestamp ON activity_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_activity_log_employee_folder ON activity_log(employee_folder);
CREATE INDEX IF NOT EXISTS idx_activity_log_firm_employee ON activity_log(firm_name, employee_name);
CREATE INDEX IF NOT EXISTS idx_archive_log_operation_id ON archive_log(operation_id);
CREATE INDEX IF NOT EXISTS idx_archive_log_is_reverted ON archive_log(is_reverted);
CREATE INDEX IF NOT EXISTS idx_archive_log_timestamp ON archive_log(timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_eh_employee ON employee_history(employee_id);
CREATE INDEX IF NOT EXISTS idx_eh_type ON employee_history(employee_id, event_type);
CREATE INDEX IF NOT EXISTS idx_eh_timestamp ON employee_history(employee_id, timestamp DESC);";

            command.ExecuteNonQuery();

            EnsureColumnExists(connection, "migration_journal", "folders_scanned", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumnExists(connection, "migration_journal", "folders_skipped", "INTEGER NOT NULL DEFAULT 0");

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

        private static string RenameMigratedJson(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath) || !File.Exists(jsonPath))
                return string.Empty;

            var migratedPath = jsonPath + ".migrated";
            if (File.Exists(migratedPath))
                SafeFileService.DeleteFile(migratedPath);

            SafeFileService.MoveFile(jsonPath, migratedPath);
            return migratedPath;
        }

        private static bool CanDeleteMigratedBackup(string jsonPath)
        {
            if (string.IsNullOrWhiteSpace(jsonPath))
                return false;

            return !File.Exists(jsonPath) && File.Exists(jsonPath + ".migrated");
        }

        private static bool TryDeleteMigratedBackup(string jsonPath, string logModule)
        {
            if (!CanDeleteMigratedBackup(jsonPath))
                return false;

            var migratedPath = jsonPath + ".migrated";
            try
            {
                SafeFileService.DeleteFile(migratedPath);
                LoggingService.LogInfo(logModule, $"Deleted migrated backup: {migratedPath}");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(logModule, $"Could not delete migrated backup '{migratedPath}': {ex.Message}");
                return false;
            }
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
}

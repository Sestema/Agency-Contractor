using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public sealed class EmployeeIndexRebuildResult
    {
        public bool WasRebuildAttempted { get; init; }
        public bool IsSuccessful { get; init; }
        public int RecordsFound { get; init; }
        public int RecordsImported { get; init; }
        public int FoldersScanned { get; init; }
        public int FoldersSkipped { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public sealed class EmployeeIndexDbService
    {
        private const int CurrentSchemaVersion = 1;
        private static readonly TimeSpan IndexLockStaleAfter = TimeSpan.FromMinutes(3);
        private static readonly JsonSerializerOptions LockJsonOptions = new() { WriteIndented = false };
        private readonly FolderService _folderService;
        private readonly AppSettingsService? _settingsService;
        private readonly PostgresEmployeeIndexStorage? _postgresStorage;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public EmployeeIndexDbService(FolderService folderService, AppSettingsService? settingsService = null)
        {
            _folderService = folderService;
            _settingsService = settingsService;
            if (settingsService != null)
                _postgresStorage = new PostgresEmployeeIndexStorage(settingsService, folderService);
        }

        public string DatabasePath => _folderService.EmployeeIndexDbPath;
        public bool IsAvailable => !string.IsNullOrWhiteSpace(DatabasePath);
        public string ActiveStorageDisplay => UsePostgresStorage ? "PostgreSQL" : "SQLite";
        private bool UsePostgresStorage =>
            _postgresStorage != null
            && _settingsService != null
            && DatabaseStorageModes.PostgresRuntimeStorageEnabled
            && string.Equals(_settingsService.Settings.DatabaseStorageMode, DatabaseStorageModes.Postgres, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_settingsService.Settings.PostgresConnectionString)
            && !string.IsNullOrWhiteSpace(_settingsService.Settings.PostgresMigrationCompletedAtUtc)
            && !string.IsNullOrWhiteSpace(_settingsService.Settings.PostgresEnabledAtUtc);

        public void EnsureInitialized()
        {
            if (UsePostgresStorage)
            {
                _postgresStorage!.EnsureInitialized();
                return;
            }

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
                TryDeleteStaleIndexLockFile();

                using var connection = OpenConnection();
                CreateSchema(connection);
                _isInitialized = true;
            }
        }

        public SqliteConnection OpenConnection()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Employee index SQLite path is not available.");

            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared;Pooling=False");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        public int GetEmployeeIndexCount()
        {
            if (UsePostgresStorage)
                return _postgresStorage!.GetEmployeeIndexCount();

            EnsureInitialized();
            if (!IsAvailable)
                return 0;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM employee_index;";
            var result = command.ExecuteScalar();
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        public bool HasAnyRows()
        {
            return GetEmployeeIndexCount() > 0;
        }

        public bool HasLegacyAbsolutePaths()
        {
            if (UsePostgresStorage)
                return _postgresStorage!.HasLegacyAbsolutePaths();

            EnsureInitialized();
            if (!IsAvailable)
                return false;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT employee_folder, photo_path
FROM employee_index
WHERE ifnull(employee_folder, '') <> ''
   OR ifnull(photo_path, '') <> '';";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var employeeFolder = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var photoPath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (IsLegacyAbsolutePath(employeeFolder) || IsLegacyAbsolutePath(photoPath))
                    return true;
            }

            return false;
        }

        public void UpsertEmployeeIndex(EmployeeIndexRow row)
        {
            if (UsePostgresStorage)
            {
                _postgresStorage!.UpsertEmployeeIndex(row);
                return;
            }

            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(row?.UniqueId))
                return;

            using var indexLock = AcquireIndexWriteLock(TimeSpan.FromSeconds(10), "upsert");
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertEmployeeIndex(connection, transaction, row);
            transaction.Commit();
        }

        public void DeleteEmployeeIndex(string uniqueId)
        {
            if (UsePostgresStorage)
            {
                _postgresStorage!.DeleteEmployeeIndex(uniqueId);
                return;
            }

            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(uniqueId))
                return;

            using var indexLock = AcquireIndexWriteLock(TimeSpan.FromSeconds(10), "delete");
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM employee_index WHERE unique_id = @uniqueId;";
            command.Parameters.AddWithValue("@uniqueId", uniqueId);
            command.ExecuteNonQuery();
        }

        public List<EmployeeIndexRow> GetEmployeesForFirmRows(string firmName)
        {
            if (UsePostgresStorage)
                return _postgresStorage!.GetEmployeesForFirmRows(firmName);

            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(firmName))
                return new List<EmployeeIndexRow>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
       start_date, end_date, contract_type, position_title, position_number, phone, email,
       passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
       work_permit_name, work_permit_expiry, bank_account_number, bank_name,
       is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
FROM employee_index
WHERE lower(firm_name) = lower(@firmName)
  AND is_archived = 0
ORDER BY full_name, start_date;";
            command.Parameters.AddWithValue("@firmName", firmName);
            return ReadRows(command);
        }

        public List<EmployeeIndexRow> GetArchivedEmployeeRows()
        {
            if (UsePostgresStorage)
                return _postgresStorage!.GetArchivedEmployeeRows();

            EnsureInitialized();
            if (!IsAvailable)
                return new List<EmployeeIndexRow>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
       start_date, end_date, contract_type, position_title, position_number, phone, email,
       passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
       work_permit_name, work_permit_expiry, bank_account_number, bank_name,
       is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
FROM employee_index
WHERE is_archived = 1
ORDER BY full_name, start_date;";
            return ReadRows(command);
        }

        public EmployeeIndexRow? GetEmployeeRowByFolder(string employeeFolder)
        {
            if (UsePostgresStorage)
                return _postgresStorage!.GetEmployeeRowByFolder(employeeFolder);

            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            var portableFolder = ToPortablePath(employeeFolder);
            var absoluteFolder = NormalizeFullPath(employeeFolder);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
       start_date, end_date, contract_type, position_title, position_number, phone, email,
       passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
       work_permit_name, work_permit_expiry, bank_account_number, bank_name,
       is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
FROM employee_index
WHERE lower(employee_folder) = lower(@portableFolder)
   OR lower(employee_folder) = lower(@absoluteFolder)
LIMIT 1;";
            command.Parameters.AddWithValue("@portableFolder", portableFolder);
            command.Parameters.AddWithValue("@absoluteFolder", absoluteFolder);

            var rows = ReadRows(command);
            return rows.Count > 0 ? rows[0] : null;
        }

        public int RenameFirmReferences(string oldName, string newName)
        {
            if (UsePostgresStorage)
                return _postgresStorage!.RenameFirmReferences(oldName, newName);

            EnsureInitialized();
            if (!IsAvailable || string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;

            var oldEmployeesFolder = _folderService.GetEmployeesFolder(oldName);
            var newEmployeesFolder = _folderService.GetEmployeesFolder(newName);
            var oldPortableFolder = ToPortablePath(oldEmployeesFolder);
            var newPortableFolder = ToPortablePath(newEmployeesFolder);
            var oldAbsoluteFolder = NormalizeFullPath(oldEmployeesFolder);
            var newAbsoluteFolder = NormalizeFullPath(newEmployeesFolder);

            using var indexLock = AcquireIndexWriteLock(TimeSpan.FromSeconds(15), "rename-firm");
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE employee_index
SET firm_name = @newName,
    employee_folder = CASE
        WHEN @oldPortableFolder <> '' AND lower(employee_folder) = lower(@oldPortableFolder)
            THEN @newPortableFolder
        WHEN @oldPortableFolder <> '' AND lower(employee_folder) LIKE lower(@oldPortableFolderLike)
            THEN @newPortableFolder || substr(employee_folder, length(@oldPortableFolder) + 1)
        WHEN @oldAbsoluteFolder <> '' AND lower(employee_folder) = lower(@oldAbsoluteFolder)
            THEN @newAbsoluteFolder
        WHEN @oldAbsoluteFolder <> '' AND lower(employee_folder) LIKE lower(@oldAbsoluteFolderLike)
            THEN @newAbsoluteFolder || substr(employee_folder, length(@oldAbsoluteFolder) + 1)
        ELSE employee_folder
    END
WHERE lower(firm_name) = lower(@oldName);";
            command.Parameters.AddWithValue("@oldName", oldName);
            command.Parameters.AddWithValue("@newName", newName);
            command.Parameters.AddWithValue("@oldPortableFolder", oldPortableFolder);
            command.Parameters.AddWithValue("@newPortableFolder", newPortableFolder);
            command.Parameters.AddWithValue("@oldPortableFolderLike", BuildChildPathLike(oldPortableFolder));
            command.Parameters.AddWithValue("@oldAbsoluteFolder", oldAbsoluteFolder);
            command.Parameters.AddWithValue("@newAbsoluteFolder", newAbsoluteFolder);
            command.Parameters.AddWithValue("@oldAbsoluteFolderLike", BuildChildPathLike(oldAbsoluteFolder));
            return command.ExecuteNonQuery();
        }

        public EmployeeIndexRebuildResult RebuildEmployeeIndex(IReadOnlyList<EmployeeIndexRow> rows, LocalDbService? localDbService = null, int foldersScanned = 0, int foldersSkipped = 0)
        {
            if (UsePostgresStorage)
                return _postgresStorage!.RebuildEmployeeIndex(rows, foldersScanned, foldersSkipped);

            EnsureInitialized();
            if (!IsAvailable)
                return new EmployeeIndexRebuildResult { Message = "Employee index SQLite path is unavailable." };

            var recordsFound = rows.Count;
            var recordsImported = 0;
            localDbService?.RecordMigrationJournal("employee_index", "started", recordsFound, 0, null, foldersScanned, foldersSkipped);

            try
            {
                using var indexLock = AcquireIndexWriteLock(TimeSpan.FromSeconds(30), "rebuild");
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                using (var clearCommand = connection.CreateCommand())
                {
                    clearCommand.Transaction = transaction;
                    clearCommand.CommandText = "DELETE FROM employee_index;";
                    clearCommand.ExecuteNonQuery();
                }

                foreach (var row in rows)
                {
                    UpsertEmployeeIndex(connection, transaction, row);
                    recordsImported++;
                }

                transaction.Commit();

                var finalCount = GetEmployeeIndexCount();
                if (finalCount != recordsFound)
                    throw new InvalidOperationException($"Employee index rebuild mismatch: expected {recordsFound}, imported {finalCount}.");

                localDbService?.RecordMigrationJournal("employee_index", "completed", recordsFound, finalCount, null, foldersScanned, foldersSkipped);
                return new EmployeeIndexRebuildResult
                {
                    WasRebuildAttempted = true,
                    IsSuccessful = true,
                    RecordsFound = recordsFound,
                    RecordsImported = finalCount,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = $"Rebuilt employee index with {finalCount} rows."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeIndexDbService.RebuildEmployeeIndex", ex);
                localDbService?.RecordMigrationJournal("employee_index", "failed", recordsFound, recordsImported, ex.Message, foldersScanned, foldersSkipped);
                return new EmployeeIndexRebuildResult
                {
                    WasRebuildAttempted = true,
                    IsSuccessful = false,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = ex.Message
                };
            }
        }

        private EmployeeIndexWriteLock AcquireIndexWriteLock(TimeSpan timeout, string operation)
        {
            var lockPath = DatabasePath + ".lock";
            var folder = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            var deadline = DateTime.UtcNow.Add(timeout);
            var ownerId = Guid.NewGuid().ToString("N");
            var lockInfo = new EmployeeIndexLockInfo
            {
                OwnerId = ownerId,
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                Operation = operation,
                CreatedAtUtc = DateTime.UtcNow
            };

            while (true)
            {
                var existingLock = TryReadIndexLock(lockPath);
                if (existingLock != null && !IsIndexLockStale(existingLock))
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new IOException(
                            $"Employee index is locked by {existingLock.MachineName} pid={existingLock.ProcessId} operation={existingLock.Operation} since {existingLock.CreatedAtUtc:o}.");
                    }

                    Thread.Sleep(150);
                    continue;
                }

                if (existingLock != null)
                    TryDeleteStaleIndexLockFile(lockPath, existingLock);

                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.WriteThrough);

                    using (var writer = new StreamWriter(stream, leaveOpen: true))
                    {
                        writer.Write(JsonSerializer.Serialize(lockInfo, LockJsonOptions));
                        writer.Flush();
                    }

                    stream.Flush(true);
                    stream.Position = 0;
                    return new EmployeeIndexWriteLock(stream, lockPath, ownerId);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(150);
                }
                catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(150);
                }
            }
        }

        private void TryDeleteStaleIndexLockFile()
        {
            var lockPath = DatabasePath + ".lock";
            var lockInfo = TryReadIndexLock(lockPath);
            if (lockInfo != null && !IsIndexLockStale(lockInfo))
                return;

            TryDeleteStaleIndexLockFile(lockPath, lockInfo);
        }

        private static bool IsIndexLockStale(EmployeeIndexLockInfo lockInfo)
        {
            if (lockInfo.CreatedAtUtc == default)
                return true;

            return DateTime.UtcNow - lockInfo.CreatedAtUtc > IndexLockStaleAfter;
        }

        private static EmployeeIndexLockInfo? TryReadIndexLock(string lockPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
                    return null;

                var text = File.ReadAllText(lockPath);
                if (string.IsNullOrWhiteSpace(text))
                    return new EmployeeIndexLockInfo { CreatedAtUtc = File.GetLastWriteTimeUtc(lockPath) };

                return JsonSerializer.Deserialize<EmployeeIndexLockInfo>(text, LockJsonOptions)
                    ?? new EmployeeIndexLockInfo { CreatedAtUtc = File.GetLastWriteTimeUtc(lockPath) };
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                return null;
            }
        }

        private static void TryDeleteStaleIndexLockFile(string lockPath, EmployeeIndexLockInfo? lockInfo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
                    return;

                File.Delete(lockPath);
                if (lockInfo != null)
                {
                    LoggingService.LogWarning("EmployeeIndexDbService.Lock",
                        $"Removed stale employee index lock from {lockInfo.MachineName} pid={lockInfo.ProcessId} operation={lockInfo.Operation} created={lockInfo.CreatedAtUtc:o}.");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // If another running process owns the lock, keeping the marker is expected.
            }
        }

        private sealed class EmployeeIndexLockInfo
        {
            public string OwnerId { get; set; } = string.Empty;
            public string MachineName { get; set; } = string.Empty;
            public int ProcessId { get; set; }
            public string Operation { get; set; } = string.Empty;
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class EmployeeIndexWriteLock : IDisposable
        {
            private readonly FileStream _stream;
            private readonly string _lockPath;
            private readonly string _ownerId;
            private bool _disposed;

            public EmployeeIndexWriteLock(FileStream stream, string lockPath, string ownerId)
            {
                _stream = stream;
                _lockPath = lockPath;
                _ownerId = ownerId;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _stream.Dispose();

                try
                {
                    var info = TryReadIndexLock(_lockPath);
                    if (info == null || string.Equals(info.OwnerId, _ownerId, StringComparison.OrdinalIgnoreCase))
                        File.Delete(_lockPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Another machine may already have synchronized or removed the lock marker.
                }
            }
        }

        private void CreateSchema(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $@"
CREATE TABLE IF NOT EXISTS _meta (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS employee_index (
    unique_id TEXT PRIMARY KEY,
    full_name TEXT,
    first_name TEXT,
    last_name TEXT,
    firm_name TEXT,
    employee_folder TEXT,
    employee_type TEXT,
    status TEXT,
    start_date TEXT,
    end_date TEXT,
    contract_type TEXT,
    position_title TEXT,
    position_number TEXT,
    phone TEXT,
    email TEXT,
    passport_number TEXT,
    visa_number TEXT,
    insurance_number TEXT,
    passport_expiry TEXT,
    visa_expiry TEXT,
    insurance_expiry TEXT,
    work_permit_name TEXT,
    work_permit_expiry TEXT,
    bank_account_number TEXT,
    bank_name TEXT,
    is_archived INTEGER NOT NULL DEFAULT 0,
    archived_from_firm TEXT,
    photo_path TEXT,
    has_photo INTEGER NOT NULL DEFAULT 0,
    has_passport INTEGER NOT NULL DEFAULT 0,
    has_visa INTEGER NOT NULL DEFAULT 0,
    has_insurance INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_ei_firm ON employee_index(firm_name);
CREATE INDEX IF NOT EXISTS idx_ei_archived ON employee_index(is_archived);
CREATE INDEX IF NOT EXISTS idx_ei_folder ON employee_index(employee_folder);
CREATE INDEX IF NOT EXISTS idx_ei_full_name ON employee_index(full_name);";
            command.ExecuteNonQuery();

            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = "SELECT COUNT(1) FROM _meta;";
            var hasVersion = Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
            if (!hasVersion)
            {
                using var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO _meta(version) VALUES (@version);";
                insertCommand.Parameters.AddWithValue("@version", CurrentSchemaVersion);
                insertCommand.ExecuteNonQuery();
            }
        }

        private void UpsertEmployeeIndex(SqliteConnection connection, SqliteTransaction transaction, EmployeeIndexRow row)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO employee_index (
    unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
    start_date, end_date, contract_type, position_title, position_number, phone, email,
    passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
    work_permit_name, work_permit_expiry, bank_account_number, bank_name,
    is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
) VALUES (
    @uniqueId, @fullName, @firstName, @lastName, @firmName, @employeeFolder, @employeeType, @status,
    @startDate, @endDate, @contractType, @positionTitle, @positionNumber, @phone, @email,
    @passportNumber, @visaNumber, @insuranceNumber, @passportExpiry, @visaExpiry, @insuranceExpiry,
    @workPermitName, @workPermitExpiry, @bankAccountNumber, @bankName,
    @isArchived, @archivedFromFirm, @photoPath, @hasPhoto, @hasPassport, @hasVisa, @hasInsurance, @updatedAt
)
ON CONFLICT(unique_id) DO UPDATE SET
    full_name = excluded.full_name,
    first_name = excluded.first_name,
    last_name = excluded.last_name,
    firm_name = excluded.firm_name,
    employee_folder = excluded.employee_folder,
    employee_type = excluded.employee_type,
    status = excluded.status,
    start_date = excluded.start_date,
    end_date = excluded.end_date,
    contract_type = excluded.contract_type,
    position_title = excluded.position_title,
    position_number = excluded.position_number,
    phone = excluded.phone,
    email = excluded.email,
    passport_number = excluded.passport_number,
    visa_number = excluded.visa_number,
    insurance_number = excluded.insurance_number,
    passport_expiry = excluded.passport_expiry,
    visa_expiry = excluded.visa_expiry,
    insurance_expiry = excluded.insurance_expiry,
    work_permit_name = excluded.work_permit_name,
    work_permit_expiry = excluded.work_permit_expiry,
    bank_account_number = excluded.bank_account_number,
    bank_name = excluded.bank_name,
    is_archived = excluded.is_archived,
    archived_from_firm = excluded.archived_from_firm,
    photo_path = excluded.photo_path,
    has_photo = excluded.has_photo,
    has_passport = excluded.has_passport,
    has_visa = excluded.has_visa,
    has_insurance = excluded.has_insurance,
    updated_at = excluded.updated_at;";

            command.Parameters.AddWithValue("@uniqueId", row.UniqueId ?? string.Empty);
            command.Parameters.AddWithValue("@fullName", row.FullName ?? string.Empty);
            command.Parameters.AddWithValue("@firstName", row.FirstName ?? string.Empty);
            command.Parameters.AddWithValue("@lastName", row.LastName ?? string.Empty);
            command.Parameters.AddWithValue("@firmName", row.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("@employeeFolder", ToPortablePath(row.EmployeeFolder));
            command.Parameters.AddWithValue("@employeeType", row.EmployeeType ?? string.Empty);
            command.Parameters.AddWithValue("@status", row.Status ?? string.Empty);
            command.Parameters.AddWithValue("@startDate", row.StartDate ?? string.Empty);
            command.Parameters.AddWithValue("@endDate", row.EndDate ?? string.Empty);
            command.Parameters.AddWithValue("@contractType", row.ContractType ?? string.Empty);
            command.Parameters.AddWithValue("@positionTitle", row.PositionTitle ?? string.Empty);
            command.Parameters.AddWithValue("@positionNumber", row.PositionNumber ?? string.Empty);
            command.Parameters.AddWithValue("@phone", row.Phone ?? string.Empty);
            command.Parameters.AddWithValue("@email", row.Email ?? string.Empty);
            command.Parameters.AddWithValue("@passportNumber", row.PassportNumber ?? string.Empty);
            command.Parameters.AddWithValue("@visaNumber", row.VisaNumber ?? string.Empty);
            command.Parameters.AddWithValue("@insuranceNumber", row.InsuranceNumber ?? string.Empty);
            command.Parameters.AddWithValue("@passportExpiry", row.PassportExpiry ?? string.Empty);
            command.Parameters.AddWithValue("@visaExpiry", row.VisaExpiry ?? string.Empty);
            command.Parameters.AddWithValue("@insuranceExpiry", row.InsuranceExpiry ?? string.Empty);
            command.Parameters.AddWithValue("@workPermitName", row.WorkPermitName ?? string.Empty);
            command.Parameters.AddWithValue("@workPermitExpiry", row.WorkPermitExpiry ?? string.Empty);
            command.Parameters.AddWithValue("@bankAccountNumber", row.BankAccountNumber ?? string.Empty);
            command.Parameters.AddWithValue("@bankName", row.BankName ?? string.Empty);
            command.Parameters.AddWithValue("@isArchived", row.IsArchived ? 1 : 0);
            command.Parameters.AddWithValue("@archivedFromFirm", row.ArchivedFromFirm ?? string.Empty);
            command.Parameters.AddWithValue("@photoPath", ToPortablePath(row.PhotoPath));
            command.Parameters.AddWithValue("@hasPhoto", row.HasPhoto ? 1 : 0);
            command.Parameters.AddWithValue("@hasPassport", row.HasPassport ? 1 : 0);
            command.Parameters.AddWithValue("@hasVisa", row.HasVisa ? 1 : 0);
            command.Parameters.AddWithValue("@hasInsurance", row.HasInsurance ? 1 : 0);
            command.Parameters.AddWithValue("@updatedAt", row.UpdatedAt ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private List<EmployeeIndexRow> ReadRows(SqliteCommand command)
        {
            var rows = new List<EmployeeIndexRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EmployeeIndexRow
                {
                    UniqueId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    FullName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    FirmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    EmployeeFolder = ResolveStoredPath(reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                    EmployeeType = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Status = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    StartDate = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    EndDate = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    ContractType = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    PositionTitle = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                    PositionNumber = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                    Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                    Email = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    PassportNumber = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    VisaNumber = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                    InsuranceNumber = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                    PassportExpiry = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    VisaExpiry = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                    InsuranceExpiry = reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                    WorkPermitName = reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
                    WorkPermitExpiry = reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
                    BankAccountNumber = reader.IsDBNull(23) ? string.Empty : reader.GetString(23),
                    BankName = reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
                    IsArchived = !reader.IsDBNull(25) && reader.GetInt32(25) != 0,
                    ArchivedFromFirm = reader.IsDBNull(26) ? string.Empty : reader.GetString(26),
                    PhotoPath = ResolveStoredPath(reader.IsDBNull(27) ? string.Empty : reader.GetString(27)),
                    HasPhoto = !reader.IsDBNull(28) && reader.GetInt32(28) != 0,
                    HasPassport = !reader.IsDBNull(29) && reader.GetInt32(29) != 0,
                    HasVisa = !reader.IsDBNull(30) && reader.GetInt32(30) != 0,
                    HasInsurance = !reader.IsDBNull(31) && reader.GetInt32(31) != 0,
                    UpdatedAt = reader.IsDBNull(32) ? string.Empty : reader.GetString(32)
                });
            }

            return rows;
        }

        private bool IsLegacyAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
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

            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string BuildChildPathLike(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return EnsureTrailingSeparator(path) + "%";
        }
    }
}

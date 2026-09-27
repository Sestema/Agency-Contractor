using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Text.Json;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Companies and database settings stored in SQLite/core.db.
    /// </summary>
    public class DatabaseRoot
    {
        public string Version { get; set; } = "2.0";
        public List<EmployerCompany> Companies { get; set; } = new();
        public DatabaseSettings Settings { get; set; } = new();
    }

    public class DatabaseSettings
    {
        public string LanguageCode { get; set; } = "uk";
        public string SelectedCompanyId { get; set; } = string.Empty;
        public string AppVersion { get; set; } = "0.0.05";
    }

    internal sealed class PendingCoreDatabaseChange
    {
        public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
        public string CreatedAtUtc { get; set; } = DateTime.UtcNow.ToString("O");
        public string MachineName { get; set; } = Environment.MachineName;
        public string UserName { get; set; } = Environment.UserName;
        public bool ReplaceAll { get; set; }
        public List<EmployerCompany> UpsertCompanies { get; set; } = new();
        public List<Guid> DeletedCompanyIds { get; set; } = new();
        public DatabaseSettings Settings { get; set; } = new();
    }

    public class PersistenceService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly ICoreDatabaseStorage _coreDatabaseStorage;
        private static readonly SemaphoreSlim _saveLock = new(1, 1);
        private DatabaseRoot? _lastLoadedDatabase;

        private static readonly JsonSerializerOptions PendingChangeJsonOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions CoreWriteLockJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string Res(string key) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? key;

        private const int CoreWriteLockTimeoutMs = 30000;
        private const int CoreWriteLockRetryDelayMs = 250;
        private static readonly TimeSpan CoreWriteLockStaleAfter = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan SaveLockTimeout = TimeSpan.FromSeconds(5);

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService)
            : this(appSettingsService, folderService, new SqliteCoreDatabaseStorage(new CoreDbService(folderService)))
        {
        }

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService, CoreDbService coreDbService)
            : this(appSettingsService, folderService, new SqliteCoreDatabaseStorage(coreDbService))
        {
        }

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService, ICoreDatabaseStorage coreDatabaseStorage)
        {
            _appSettingsService = appSettingsService;
            _folderService = folderService;
            _coreDatabaseStorage = coreDatabaseStorage;
        }

        // ============ PRIMARY FORMAT: SQLite/core.db ============

        /// <summary>
        /// Save the full database (companies + settings) into SQLite/core.db.
        /// </summary>
        public async Task SaveDatabaseAsync(IEnumerable<EmployerCompany> companies)
        {
            if (!await _saveLock.WaitAsync(SaveLockTimeout).ConfigureAwait(false))
            {
                ReportSaveLockTimeout("PersistenceService.SaveDatabaseAsync");
                return;
            }

            try
            {
                var companySnapshot = companies.ToList();
                SaveDatabaseCore(companySnapshot);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PersistenceService.SaveDatabase", ex);
                Debug.WriteLine($"PersistenceService.SaveDatabase failed: {ex.Message}");
                ErrorHandler.Report("PersistenceService.SaveDatabase", ex, ErrorSeverity.Error);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public Task SaveCompaniesAsync(IEnumerable<EmployerCompany> companies)
        {
            return SaveDatabaseAsync(companies);
        }

        /// <summary>
        /// Load the full database from SQLite/core.db.
        /// </summary>
        public DatabaseRoot LoadDatabase()
        {
            var rootPath = _folderService.RootPath;
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return new DatabaseRoot();

            if (TryLoadCoreDatabase(out var coreDatabase))
            {
                ApplyPendingCoreChanges();
                if (TryLoadCoreDatabase(out var refreshedDatabase))
                    coreDatabase = refreshedDatabase;

                RememberLoadedDatabase(coreDatabase);
                return coreDatabase;
            }

            var empty = new DatabaseRoot();
            RememberLoadedDatabase(empty);
            return empty;
        }

        private bool TryLoadCoreDatabase(out DatabaseRoot database)
        {
            database = new DatabaseRoot();

            try
            {
                var db = _coreDatabaseStorage.LoadDatabase();
                if (db == null)
                    return false;

                database = db;
                Debug.WriteLine($"PersistenceService.LoadDatabase: loaded {db.Companies.Count} companies from core.db (v{db.Version})");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.LoadCoreDatabase", ex.Message);
                Debug.WriteLine($"PersistenceService.LoadCoreDatabase failed: {ex.Message}");
                return false;
            }
        }

        // ============ BACKUP / RESTORE ============

        private void CreateBackup(string sourceFilePath)
        {
            try
            {
                var backupsFolder = _folderService.GetBackupsFolder();
                Directory.CreateDirectory(backupsFolder);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var sourceExtension = Path.GetExtension(sourceFilePath);
                var backupExtension = string.Equals(sourceExtension, ".db", StringComparison.OrdinalIgnoreCase)
                    ? ".db.bak"
                    : ".json.bak";
                var backupPath = Path.Combine(backupsFolder, $"database_{timestamp}{backupExtension}");
                SafeFileService.CopyFile(sourceFilePath, backupPath);

                CleanupOldBackups(backupsFolder);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService.CreateBackup error: {ex.Message}");
            }
        }

        private void CleanupOldBackups(string backupFolder)
        {
            try
            {
                var dir = new DirectoryInfo(backupFolder);
                var files = dir.GetFiles("*.bak").OrderByDescending(f => f.CreationTime).ToList();
                if (files.Count > 10)
                {
                    for (int i = 10; i < files.Count; i++)
                    {
                        files[i].Delete();
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogWarning("PersistenceService.CleanupOldBackups", ex.Message); }
        }

        // ============ BACKWARD COMPATIBILITY ============

        /// <summary>
        /// Load companies (backward-compatible wrapper).
        /// </summary>
        public List<EmployerCompany> LoadCompanies()
        {
            var db = LoadDatabase();
            if (db.Settings != null)
            {
                if (!string.IsNullOrEmpty(db.Settings.SelectedCompanyId))
                    _appSettingsService.Settings.SelectedCompanyId = db.Settings.SelectedCompanyId;
            }
            return db.Companies ?? new List<EmployerCompany>();
        }

        public void SaveDatabase(IEnumerable<EmployerCompany> companies)
        {
            if (!_saveLock.Wait(SaveLockTimeout))
            {
                ReportSaveLockTimeout("PersistenceService.SaveDatabase");
                return;
            }

            try
            {
                var companySnapshot = companies.ToList();
                SaveDatabaseCore(companySnapshot);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PersistenceService.SaveDatabase", ex);
                Debug.WriteLine($"PersistenceService.SaveDatabase failed: {ex.Message}");
                ErrorHandler.Report("PersistenceService.SaveDatabase", ex, ErrorSeverity.Error);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private static void ReportSaveLockTimeout(string source)
        {
            var message = Res("MsgSaveError");
            if (string.IsNullOrWhiteSpace(message) || message == "MsgSaveError")
                message = "Could not save. The company list is busy.";

            LoggingService.LogWarning(source, message);
            ErrorHandler.Report(source, message, ErrorSeverity.Error);
        }

        /// <summary>
        /// Save companies (backward-compatible wrapper).
        /// </summary>
        public void SaveCompanies(IEnumerable<EmployerCompany> companies)
        {
            SaveDatabase(companies);
        }

        private void SaveDatabaseCore(List<EmployerCompany> companySnapshot)
        {
            var db = new DatabaseRoot
            {
                Version = "2.0",
                Companies = companySnapshot,
                Settings = new DatabaseSettings
                {
                    LanguageCode = _appSettingsService.Settings.LanguageCode ?? "uk",
                    SelectedCompanyId = _appSettingsService.Settings.SelectedCompanyId ?? string.Empty,
                    AppVersion = _appSettingsService.Settings.AppVersion
                }
            };

            SaveDatabaseRootCore(db);
        }

        private void SaveDatabaseRootCore(DatabaseRoot database)
        {
            var change = BuildPendingCoreChange(database);
            var pendingPath = WritePendingCoreChange(change);
            ApplyPendingCoreChanges();

            if (File.Exists(pendingPath))
            {
                LoggingService.LogWarning("PersistenceService.SaveDatabase",
                    $"Core database write was queued because core.db is busy. Pending file: {pendingPath}");
                return;
            }

            RememberLoadedDatabase(database);
        }

        private PendingCoreDatabaseChange BuildPendingCoreChange(DatabaseRoot database)
        {
            var currentCompanies = database.Companies ?? new List<EmployerCompany>();
            var previousCompanies = _lastLoadedDatabase?.Companies;
            var change = new PendingCoreDatabaseChange
            {
                OperationId = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = DateTime.UtcNow.ToString("O"),
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                Settings = database.Settings ?? new DatabaseSettings()
            };

            if (previousCompanies == null)
            {
                change.ReplaceAll = true;
                change.UpsertCompanies = currentCompanies.ToList();
                return change;
            }

            var previousById = previousCompanies.ToDictionary(company => company.Id);
            var currentById = currentCompanies.ToDictionary(company => company.Id);

            foreach (var company in currentCompanies)
            {
                if (!previousById.TryGetValue(company.Id, out var previous)
                    || !AreCompaniesEquivalent(previous, company))
                {
                    change.UpsertCompanies.Add(company);
                }
            }

            foreach (var previous in previousCompanies)
            {
                if (!currentById.ContainsKey(previous.Id))
                    change.DeletedCompanyIds.Add(previous.Id);
            }

            return change;
        }

        private string WritePendingCoreChange(PendingCoreDatabaseChange change)
        {
            var pendingFolder = GetPendingCoreChangesFolder();
            Directory.CreateDirectory(pendingFolder);

            var fileBase = $"{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{SanitizeFileNamePart(Environment.MachineName)}_{change.OperationId}";
            var tmpPath = Path.Combine(pendingFolder, fileBase + ".tmp");
            var finalPath = Path.Combine(pendingFolder, fileBase + ".json");
            var json = JsonSerializer.Serialize(change, PendingChangeJsonOptions);

            SafeFileService.WriteTextAtomic(tmpPath, json, Encoding.UTF8);
            if (File.Exists(finalPath))
                SafeFileService.DeleteFile(finalPath);
            SafeFileService.MoveFile(tmpPath, finalPath);
            return finalPath;
        }

        private void ApplyPendingCoreChanges()
        {
            var pendingFolder = GetPendingCoreChangesFolder();
            if (!Directory.Exists(pendingFolder))
                return;

            var pendingFiles = Directory.GetFiles(pendingFolder, "*.json")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pendingFiles.Count == 0)
                return;

            if (!TryAcquireCoreWriteLock("apply-pending", out var writeLock))
                return;

            using (writeLock)
            {
                try
                {
                    var database = _coreDatabaseStorage.LoadDatabase() ?? new DatabaseRoot();
                    var changed = false;
                    var appliedPendingFiles = new List<string>();

                    foreach (var pendingFile in pendingFiles)
                    {
                        PendingCoreDatabaseChange? change;
                        try
                        {
                            change = SafeFileService.ReadJson<PendingCoreDatabaseChange>(pendingFile, PendingChangeJsonOptions, Encoding.UTF8);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogWarning("PersistenceService.PendingCoreChanges",
                                $"Could not read pending core database change '{pendingFile}': {ex.Message}");
                            continue;
                        }

                        if (change == null)
                            continue;

                        ApplyPendingCoreChange(database, change);
                        changed = true;
                        appliedPendingFiles.Add(pendingFile);
                    }

                    if (changed)
                    {
                        if (_coreDatabaseStorage.Exists)
                            CreateBackup(_coreDatabaseStorage.DatabasePath);

                        _coreDatabaseStorage.SaveDatabase(database);
                        RememberLoadedDatabase(database);

                        foreach (var appliedPendingFile in appliedPendingFiles)
                            SafeFileService.DeleteFile(appliedPendingFile);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("PersistenceService.ApplyPendingCoreChanges", ex);
                    throw;
                }
            }
        }

        private static void ApplyPendingCoreChange(DatabaseRoot database, PendingCoreDatabaseChange change)
        {
            if (change.ReplaceAll)
            {
                database.Version = "2.0";
                database.Companies = change.UpsertCompanies.ToList();
                database.Settings = change.Settings ?? new DatabaseSettings();
                return;
            }

            var companiesById = (database.Companies ?? new List<EmployerCompany>())
                .ToDictionary(company => company.Id);

            foreach (var deletedId in change.DeletedCompanyIds)
                companiesById.Remove(deletedId);

            foreach (var company in change.UpsertCompanies)
                companiesById[company.Id] = company;

            database.Version = "2.0";
            database.Companies = companiesById.Values
                .OrderBy(company => company.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            database.Settings = change.Settings ?? database.Settings ?? new DatabaseSettings();
        }

        private bool TryAcquireCoreWriteLock(string operation, out CoreWriteLock? writeLock)
        {
            writeLock = null;
            var lockPath = GetCoreWriteLockPath();
            if (string.IsNullOrWhiteSpace(lockPath))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? _folderService.GetSqliteFolder());
            var deadline = DateTime.UtcNow.AddMilliseconds(CoreWriteLockTimeoutMs);
            var ownerId = Guid.NewGuid().ToString("N");
            var lockInfo = new CoreWriteLockInfo
            {
                OwnerId = ownerId,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                ProcessId = Environment.ProcessId,
                Operation = operation,
                CreatedAtUtc = DateTime.UtcNow
            };

            while (DateTime.UtcNow < deadline)
            {
                var existingLock = TryReadCoreWriteLock(lockPath);
                if (existingLock != null && !IsCoreWriteLockStale(existingLock))
                {
                    Thread.Sleep(CoreWriteLockRetryDelayMs);
                    continue;
                }

                if (existingLock != null)
                    TryDeleteStaleCoreWriteLock(lockPath, existingLock);

                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.CreateNew,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 4096,
                        FileOptions.WriteThrough);

                    using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
                    {
                        writer.Write(JsonSerializer.Serialize(lockInfo, CoreWriteLockJsonOptions));
                        writer.Flush();
                    }

                    stream.Flush(true);
                    stream.Position = 0;
                    writeLock = new CoreWriteLock(stream, lockPath, ownerId);
                    return true;
                }
                catch (IOException)
                {
                    Thread.Sleep(CoreWriteLockRetryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(CoreWriteLockRetryDelayMs);
                }
            }

            var currentLock = TryReadCoreWriteLock(lockPath);
            var ownerDetails = currentLock == null
                ? lockPath
                : $"{lockPath} owner={currentLock.MachineName}\\{currentLock.UserName} pid={currentLock.ProcessId} operation={currentLock.Operation} since={currentLock.CreatedAtUtc:o}";
            LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                $"core.db write lock is busy. Pending changes will be retried later: {ownerDetails}");
            return false;
        }

        private static bool IsCoreWriteLockStale(CoreWriteLockInfo lockInfo)
        {
            if (lockInfo.CreatedAtUtc == default)
                return true;

            return DateTime.UtcNow - lockInfo.CreatedAtUtc > CoreWriteLockStaleAfter;
        }

        private static CoreWriteLockInfo? TryReadCoreWriteLock(string lockPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
                    return null;

                var text = File.ReadAllText(lockPath);
                if (string.IsNullOrWhiteSpace(text))
                    return new CoreWriteLockInfo { CreatedAtUtc = File.GetLastWriteTimeUtc(lockPath) };

                try
                {
                    return JsonSerializer.Deserialize<CoreWriteLockInfo>(text, CoreWriteLockJsonOptions)
                        ?? new CoreWriteLockInfo { CreatedAtUtc = File.GetLastWriteTimeUtc(lockPath) };
                }
                catch (JsonException)
                {
                    // Corrupt lock content (e.g. partial write after a crash): fall back to the
                    // file timestamp so the staleness check can still retire an abandoned lock.
                    return new CoreWriteLockInfo { CreatedAtUtc = File.GetLastWriteTimeUtc(lockPath) };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static void TryDeleteStaleCoreWriteLock(string lockPath, CoreWriteLockInfo? lockInfo)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
                    return;

                if (lockInfo != null && !IsCoreWriteLockStale(lockInfo))
                    return;

                SafeFileService.DeleteFile(lockPath);
                if (lockInfo != null)
                {
                    LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                        $"Deleted stale core database write lock from {lockInfo.MachineName}\\{lockInfo.UserName} pid={lockInfo.ProcessId} operation={lockInfo.Operation} created={lockInfo.CreatedAtUtc:o}.");
                }
                else
                {
                    LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                        $"Deleted stale core database write lock: {lockPath}");
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Another running process may still own the lock.
            }
        }

        private string GetPendingCoreChangesFolder()
        {
            var sqliteFolder = _folderService.GetSqliteFolder();
            return string.IsNullOrWhiteSpace(sqliteFolder)
                ? string.Empty
                : Path.Combine(sqliteFolder, "PendingChanges");
        }

        private string GetCoreWriteLockPath()
        {
            var databasePath = _coreDatabaseStorage.DatabasePath;
            if (!string.IsNullOrWhiteSpace(databasePath))
                return databasePath + ".lock";

            var sqliteFolder = _folderService.GetSqliteFolder();
            return string.IsNullOrWhiteSpace(sqliteFolder)
                ? string.Empty
                : Path.Combine(sqliteFolder, "core.db.lock");
        }

        private sealed class CoreWriteLockInfo
        {
            public string OwnerId { get; set; } = string.Empty;
            public string MachineName { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public int ProcessId { get; set; }
            public string Operation { get; set; } = string.Empty;
            public DateTime CreatedAtUtc { get; set; }
        }

        private sealed class CoreWriteLock : IDisposable
        {
            private readonly FileStream _stream;
            private readonly string _lockPath;
            private readonly string _ownerId;
            private bool _disposed;

            public CoreWriteLock(FileStream stream, string lockPath, string ownerId)
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
                    var info = TryReadCoreWriteLock(_lockPath);
                    if (info == null || string.Equals(info.OwnerId, _ownerId, StringComparison.OrdinalIgnoreCase))
                        SafeFileService.DeleteFile(_lockPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                        $"Could not remove core database write lock '{_lockPath}': {ex.Message}");
                }
            }
        }

        private void RememberLoadedDatabase(DatabaseRoot database)
        {
            _lastLoadedDatabase = CloneDatabase(database);
        }

        private static DatabaseRoot CloneDatabase(DatabaseRoot database)
        {
            var json = JsonSerializer.Serialize(database, PendingChangeJsonOptions);
            return JsonSerializer.Deserialize<DatabaseRoot>(json) ?? new DatabaseRoot();
        }

        private static bool AreCompaniesEquivalent(EmployerCompany left, EmployerCompany right)
        {
            var leftJson = JsonSerializer.Serialize(left, PendingChangeJsonOptions);
            var rightJson = JsonSerializer.Serialize(right, PendingChangeJsonOptions);
            return string.Equals(leftJson, rightJson, StringComparison.Ordinal);
        }

        private static string SanitizeFileNamePart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
        }
    }
}

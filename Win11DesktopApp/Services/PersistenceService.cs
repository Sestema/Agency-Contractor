using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using System.Text.Json;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Data model for the unified database.json file.
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
        private readonly CoreDbService _coreDbService;
        private static readonly SemaphoreSlim _saveLock = new(1, 1);
        private DatabaseRoot? _lastLoadedDatabase;

        private static readonly JsonSerializerOptions PendingChangeJsonOptions = new()
        {
            WriteIndented = true
        };

        private static string Res(string key) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? key;

        private const string OldDataFileName = "company_data.json";
        private const string OldChecksumExtension = ".sha256";
        private static readonly byte[] DatabaseEnvelopeMagic = Encoding.ASCII.GetBytes("ACD2");
        private const byte DatabaseEnvelopeVersion = 2;
        private const int AesIvSizeBytes = 16;
        private const int HmacSizeBytes = 32;
        private const int CoreWriteLockTimeoutMs = 30000;
        private const int CoreWriteLockRetryDelayMs = 250;
        private static readonly TimeSpan CoreWriteLockStaleAfter = TimeSpan.FromMinutes(3);

        private static readonly byte[] SecureKey = new byte[32];
        private static readonly byte[] SecureIV = new byte[16];
        private static readonly byte[] HmacKey;

        static PersistenceService()
        {
            var keyBytes = Encoding.UTF8.GetBytes("AgencyContractorSecretKey2024_Secure");
            Array.Copy(keyBytes, SecureKey, Math.Min(keyBytes.Length, SecureKey.Length));
            var ivBytes = Encoding.UTF8.GetBytes("AgencyContractor");
            Array.Copy(ivBytes, SecureIV, Math.Min(ivBytes.Length, SecureIV.Length));
            HmacKey = SHA256.HashData(Encoding.UTF8.GetBytes("AgencyContractorSecretKey2024_Secure|database-json-hmac-v2"));
        }

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService)
            : this(appSettingsService, folderService, new CoreDbService(folderService))
        {
        }

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService, CoreDbService coreDbService)
        {
            _appSettingsService = appSettingsService;
            _folderService = folderService;
            _coreDbService = coreDbService;
        }

        // ============ PRIMARY FORMAT: SQLite/core.db ============

        /// <summary>
        /// Save the full database (companies + settings) into SQLite/core.db.
        /// </summary>
        public async Task SaveDatabaseAsync(IEnumerable<EmployerCompany> companies)
        {
            await _saveLock.WaitAsync().ConfigureAwait(false);
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
        /// Falls back to legacy database.json only when core.db does not exist yet.
        /// </summary>
        public DatabaseRoot LoadDatabase()
        {
            var rootPath = _folderService.RootPath;
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return new DatabaseRoot();

            var dbPath = _folderService.DatabaseFilePath;
            var hasCore = TryLoadCoreDatabase(out var coreDatabase);
            var hasJson = File.Exists(dbPath);

            // 1. Final SQLite mode: core.db is authoritative. Retire any leftover JSON snapshot.
            if (hasCore)
            {
                ApplyPendingCoreChanges();
                if (TryLoadCoreDatabase(out var refreshedDatabase))
                    coreDatabase = refreshedDatabase;

                MarkLegacyDatabaseJsonMigrated();
                RememberLoadedDatabase(coreDatabase);
                return coreDatabase;
            }

            // 2. Fallback: try legacy database.json once and migrate it to core.db.
            if (hasJson)
            {
                var jsonDb = TryLoadJsonDatabase(dbPath);
                if (jsonDb != null)
                {
                    TryMigrateJsonDatabaseToCore(jsonDb, "initial_json_migration");
                    MarkLegacyDatabaseJsonMigrated();
                    RememberLoadedDatabase(jsonDb);
                    return jsonDb;
                }

                var restored = TryRestoreFromBackup();
                RememberLoadedDatabase(restored);
                return restored;
            }

            // 3. Fallback: try old format (company_data.json) and migrate.
            var oldPath = Path.Combine(rootPath, OldDataFileName);
            if (File.Exists(oldPath))
            {
                Debug.WriteLine("PersistenceService: found old company_data.json, migrating...");
                var migrated = MigrateFromOldFormat(rootPath, oldPath);
                RememberLoadedDatabase(migrated);
                return migrated;
            }

            // 4. Clean install — no data.
            var empty = new DatabaseRoot();
            RememberLoadedDatabase(empty);
            return empty;
        }

        private bool TryLoadCoreDatabase(out DatabaseRoot database)
        {
            database = new DatabaseRoot();

            try
            {
                var db = _coreDbService.LoadDatabase();
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

        private DatabaseRoot? TryLoadJsonDatabase(string dbPath)
        {
            try
            {
                var encryptedData = SafeFileService.ReadAllBytes(dbPath);
                var currentChecksum = ComputeHash(encryptedData);

                var checksumPath = _folderService.DatabaseChecksumPath;
                if (File.Exists(checksumPath))
                {
                    var storedChecksum = SafeFileService.ReadAllText(checksumPath, Encoding.UTF8).Trim();
                    if (storedChecksum != currentChecksum)
                        Debug.WriteLine("PersistenceService: database.json checksum mismatch detected, validating current file...");
                }
                else
                {
                    SafeFileService.WriteTextAtomic(checksumPath, currentChecksum, Encoding.UTF8);
                    LoggingService.LogWarning("PersistenceService.LoadDatabase",
                        "database.json.sha256 was missing and has been recreated.");
                }

                var json = Decrypt(encryptedData);
                var db = JsonSerializer.Deserialize<DatabaseRoot>(json);
                if (db == null)
                    return null;

                if (File.Exists(checksumPath))
                {
                    var storedChecksum = SafeFileService.ReadAllText(checksumPath, Encoding.UTF8).Trim();
                    if (!string.Equals(storedChecksum, currentChecksum, StringComparison.Ordinal))
                    {
                        SafeFileService.WriteTextAtomic(checksumPath, currentChecksum, Encoding.UTF8);
                        LoggingService.LogWarning("PersistenceService.LoadDatabase",
                            "database.json checksum was stale and has been refreshed from the current valid file.");
                    }
                }

                Debug.WriteLine($"PersistenceService.LoadDatabase: loaded {db.Companies.Count} companies from database.json (v{db.Version})");
                return db;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService.LoadDatabase (database.json) failed: {ex.Message}");
                return null;
            }
        }

        private void TryMigrateJsonDatabaseToCore(DatabaseRoot database, string reason)
        {
            try
            {
                SaveDatabaseRootCore(database);
                LoggingService.LogInfo("PersistenceService.Migration", $"database.json was migrated into SQLite/core.db ({reason}).");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.Migration", $"Could not synchronize database.json into core.db ({reason}): {ex.Message}");
            }
        }

        // ============ MIGRATION ============

        /// <summary>
        /// Migrate from old company_data.json format to new database.json.
        /// Also migrates folder structure.
        /// </summary>
        private DatabaseRoot MigrateFromOldFormat(string rootPath, string oldFilePath)
        {
            try
            {
                // 1. Read old encrypted data
                var encryptedData = SafeFileService.ReadAllBytes(oldFilePath);

                // Verify old checksum if exists
                var oldChecksumPath = oldFilePath + OldChecksumExtension;
                if (File.Exists(oldChecksumPath))
                {
                    var storedChecksum = SafeFileService.ReadAllText(oldChecksumPath, Encoding.UTF8);
                    var currentChecksum = ComputeHash(encryptedData);
                    if (storedChecksum != currentChecksum)
                    {
                        Debug.WriteLine("PersistenceService: old data integrity check failed");
                        // Continue anyway — better to have potentially corrupted data than nothing
                    }
                }

                // 2. Decrypt and deserialize old format (just a list of companies)
                var json = Decrypt(encryptedData);
                var oldCompanies = JsonSerializer.Deserialize<List<EmployerCompany>>(json)
                                   ?? new List<EmployerCompany>();

                Debug.WriteLine($"PersistenceService: migrating {oldCompanies.Count} companies from old format");

                // 3. Migrate folder structure for each company
                var langCode = _folderService.FolderLanguageCode;
                foreach (var company in oldCompanies)
                {
                    MigrateCompanyFolders(rootPath, company.Name, langCode);
                }

                // 4. Migrate archive folder
                MigrateArchiveFolder(rootPath, langCode);

                // 5. Create new database
                var db = new DatabaseRoot
                {
                    Version = "2.0",
                    Companies = oldCompanies,
                    Settings = new DatabaseSettings
                    {
                        LanguageCode = _appSettingsService.Settings.LanguageCode ?? "uk",
                        SelectedCompanyId = _appSettingsService.Settings.SelectedCompanyId ?? string.Empty,
                        AppVersion = _appSettingsService.Settings.AppVersion
                    }
                };

                // 6. Save new core.db database
                SaveDatabaseRootCore(db);

                // 7. Rename old files (keep as backup, don't delete)
                try
                {
                    SafeFileService.MoveFile(oldFilePath, oldFilePath + ".migrated");
                    if (File.Exists(oldChecksumPath))
                        SafeFileService.MoveFile(oldChecksumPath, oldChecksumPath + ".migrated");
                }
                catch (Exception ex) { LoggingService.LogWarning("PersistenceService.Migration", $"Rename error: {ex.Message}"); }

                Debug.WriteLine("PersistenceService: migration completed successfully");
                return db;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService: migration failed: {ex.Message}");
                return new DatabaseRoot();
            }
        }

        /// <summary>
        /// Migrate folder structure for a company:
        /// Old: {Root}/Employers/{Name}/Employees/ → New: {Root}/{Name}/{Працівники}/
        /// Old: {Root}/{Name}/Templates/ → New: {Root}/{Name}/{Шаблони}/
        /// </summary>
        private void MigrateCompanyFolders(string rootPath, string companyName, string langCode)
        {
            try
            {
                var safeName = FolderService.NormalizeFolderName(companyName);
                var newCompanyFolder = Path.Combine(rootPath, safeName);
                Directory.CreateDirectory(newCompanyFolder);

                // --- Migrate Employees ---
                // Old path variant 1: {Root}/Employers/{Name}/Employees/
                var oldEmployersFolder = Path.Combine(rootPath, "Employers", safeName, "Employees");
                var newEmployeesFolder = Path.Combine(newCompanyFolder, FolderNames.GetEmployeesFolder(langCode));

                if (Directory.Exists(oldEmployersFolder) && !Directory.Exists(newEmployeesFolder))
                {
                    Debug.WriteLine($"Migration: moving {oldEmployersFolder} -> {newEmployeesFolder}");
                    CopyDirectoryRecursive(oldEmployersFolder, newEmployeesFolder);
                    TryDeleteDirectory(oldEmployersFolder);
                }
                // Old path variant 2: {Root}/{Name}/Employees/ (already correct location but wrong subfolder name)
                else
                {
                    var oldEmployeesInCompany = Path.Combine(newCompanyFolder, "Employees");
                    if (Directory.Exists(oldEmployeesInCompany) && !Directory.Exists(newEmployeesFolder)
                        && langCode == "uk") // Only rename if current lang is UK and folder is English
                    {
                        Debug.WriteLine($"Migration: renaming {oldEmployeesInCompany} -> {newEmployeesFolder}");
                        try { Directory.Move(oldEmployeesInCompany, newEmployeesFolder); }
                        catch (Exception ex) { LoggingService.LogWarning("PersistenceService.Migration", $"Move employees folder failed: {ex.Message}"); }
                    }
                }

                // Ensure employees folder exists
                Directory.CreateDirectory(newEmployeesFolder.Length > 0 ? newEmployeesFolder :
                    Path.Combine(newCompanyFolder, FolderNames.GetEmployeesFolder(langCode)));

                // --- Migrate Templates ---
                var oldTemplatesFolder = Path.Combine(newCompanyFolder, "Templates");
                var newTemplatesFolder = Path.Combine(newCompanyFolder, FolderNames.GetTemplatesFolder(langCode));

                if (Directory.Exists(oldTemplatesFolder) && !Directory.Exists(newTemplatesFolder)
                    && langCode == "uk")
                {
                    Debug.WriteLine($"Migration: renaming {oldTemplatesFolder} -> {newTemplatesFolder}");
                    try { Directory.Move(oldTemplatesFolder, newTemplatesFolder); }
                    catch (Exception ex) { LoggingService.LogWarning("PersistenceService.Migration", $"Move templates folder failed: {ex.Message}"); }
                }

                // Ensure templates folder exists
                if (!Directory.Exists(newTemplatesFolder) && !Directory.Exists(oldTemplatesFolder))
                {
                    Directory.CreateDirectory(Path.Combine(newCompanyFolder, FolderNames.GetTemplatesFolder(langCode)));
                }

                // Update template index.json paths if needed
                MigrateTemplateIndex(newCompanyFolder, langCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService.MigrateCompanyFolders error for {companyName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Migrate archive folder: {Root}/Archive → {Root}/{Архів}
        /// </summary>
        private void MigrateArchiveFolder(string rootPath, string langCode)
        {
            try
            {
                var oldArchive = Path.Combine(rootPath, "Archive");
                var newArchive = Path.Combine(rootPath, FolderNames.GetArchiveFolder(langCode));

                if (Directory.Exists(oldArchive) && !Directory.Exists(newArchive) && langCode == "uk")
                {
                    Debug.WriteLine($"Migration: renaming {oldArchive} -> {newArchive}");
                    try { Directory.Move(oldArchive, newArchive); }
                    catch (Exception ex) { LoggingService.LogWarning("PersistenceService.Migration", $"Move archive folder failed: {ex.Message}"); }
                }

                // Also check if Employers directory is now empty, clean up
                var employersDir = Path.Combine(rootPath, "Employers");
                if (Directory.Exists(employersDir))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(employersDir).Any())
                        {
                            Directory.Delete(employersDir);
                            Debug.WriteLine("Migration: cleaned up empty Employers directory");
                        }
                    }
                    catch (Exception ex) { LoggingService.LogWarning("PersistenceService.Migration", $"Cleanup empty dir failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PersistenceService.MigrateArchiveFolder", ex);
                Debug.WriteLine($"PersistenceService.MigrateArchiveFolder error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates template index.json to use new relative paths if folder names changed.
        /// </summary>
        private void MigrateTemplateIndex(string companyFolder, string langCode)
        {
            try
            {
                // Find the templates folder (may be old or new name)
                string? templatesFolder = null;
                foreach (var name in FolderNames.AllTemplatesFolderNames)
                {
                    var path = Path.Combine(companyFolder, name);
                    if (Directory.Exists(path))
                    {
                        templatesFolder = path;
                        break;
                    }
                }
                if (templatesFolder == null) return;

                var indexPath = Path.Combine(templatesFolder, "index.json");
                if (!File.Exists(indexPath)) return;

                var currentFolderName = Path.GetFileName(templatesFolder);
                var index = SafeFileService.ReadJsonOrDefault(indexPath, new List<TemplateIndexEntry>());
                bool changed = false;

                foreach (var entry in index)
                {
                    var parts = (entry.Path ?? string.Empty)
                        .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length == 0)
                        continue;

                    if (!FolderNames.AllTemplatesFolderNames.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                        continue;

                    parts[0] = currentFolderName;
                    var normalizedPath = Path.Combine(parts);
                    if (string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
                        continue;

                    entry.Path = normalizedPath;
                    changed = true;
                }

                if (!changed)
                    return;

                SafeFileService.WriteJsonAtomic(indexPath, index, encoding: Encoding.UTF8);
                Debug.WriteLine($"Migration: updated template index paths in {indexPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService.MigrateTemplateIndex error: {ex.Message}");
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

        private DatabaseRoot TryRestoreFromBackup()
        {
            try
            {
                var backupsFolder = _folderService.GetBackupsFolder();
                if (!Directory.Exists(backupsFolder)) return new DatabaseRoot();

                var dir = new DirectoryInfo(backupsFolder);
                var latestBackup = dir.GetFiles("*.bak").OrderByDescending(f => f.CreationTime).FirstOrDefault();
                if (latestBackup == null) return new DatabaseRoot();

                Debug.WriteLine($"PersistenceService: restoring from backup {latestBackup.Name}");
                var encryptedData = SafeFileService.ReadAllBytes(latestBackup.FullName);
                var json = Decrypt(encryptedData);
                var db = JsonSerializer.Deserialize<DatabaseRoot>(json);
                return db ?? new DatabaseRoot();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PersistenceService: backup restore failed: {ex.Message}");
                return new DatabaseRoot();
            }
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
            _saveLock.Wait();
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
            MarkLegacyDatabaseJsonMigrated();
            DeleteCoreSyncState();
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

            if (!TryAcquireCoreWriteLock(out var lockPath))
                return;

            try
            {
                var database = _coreDbService.LoadDatabase() ?? new DatabaseRoot();
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
                    if (_coreDbService.Exists)
                        CreateBackup(_coreDbService.DatabasePath);

                    _coreDbService.SaveDatabase(database);
                    RememberLoadedDatabase(database);

                    foreach (var appliedPendingFile in appliedPendingFiles)
                        SafeFileService.DeleteFile(appliedPendingFile);
                }
            }
            finally
            {
                ReleaseCoreWriteLock(lockPath);
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

        private bool TryAcquireCoreWriteLock(out string lockPath)
        {
            lockPath = GetCoreWriteLockPath();
            if (string.IsNullOrWhiteSpace(lockPath))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(lockPath) ?? _folderService.GetSqliteFolder());
            var deadline = DateTime.UtcNow.AddMilliseconds(CoreWriteLockTimeoutMs);
            var lockPayload = JsonSerializer.Serialize(new
            {
                machineName = Environment.MachineName,
                userName = Environment.UserName,
                processId = Environment.ProcessId,
                createdAtUtc = DateTime.UtcNow.ToString("O")
            }, PendingChangeJsonOptions);

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    using var writer = new StreamWriter(stream, Encoding.UTF8);
                    writer.Write(lockPayload);
                    return true;
                }
                catch (IOException)
                {
                    TryDeleteStaleCoreWriteLock(lockPath);
                    Thread.Sleep(CoreWriteLockRetryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(CoreWriteLockRetryDelayMs);
                }
            }

            LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                $"core.db write lock is busy. Pending changes will be retried later: {lockPath}");
            return false;
        }

        private static void ReleaseCoreWriteLock(string lockPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(lockPath) && File.Exists(lockPath))
                    SafeFileService.DeleteFile(lockPath);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                    $"Could not release core database write lock '{lockPath}': {ex.Message}");
            }
        }

        private static void TryDeleteStaleCoreWriteLock(string lockPath)
        {
            try
            {
                if (!File.Exists(lockPath))
                    return;

                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath);
                if (age < CoreWriteLockStaleAfter)
                    return;

                SafeFileService.DeleteFile(lockPath);
                LoggingService.LogWarning("PersistenceService.CoreWriteLock",
                    $"Deleted stale core database write lock: {lockPath}");
            }
            catch
            {
                // The active writer may still hold the lock. Keep waiting.
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
            var sqliteFolder = _folderService.GetSqliteFolder();
            return string.IsNullOrWhiteSpace(sqliteFolder)
                ? string.Empty
                : Path.Combine(sqliteFolder, "write.lock");
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

        private void MarkLegacyDatabaseJsonMigrated()
        {
            var dbPath = _folderService.DatabaseFilePath;
            if (string.IsNullOrEmpty(dbPath))
                return;

            TryMoveToMigrated(dbPath);
            TryMoveToMigrated(_folderService.DatabaseChecksumPath);
        }

        private static void TryMoveToMigrated(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            var migratedPath = path + ".migrated";
            try
            {
                if (File.Exists(migratedPath))
                    SafeFileService.DeleteFile(migratedPath);

                SafeFileService.MoveFile(path, migratedPath);
                LoggingService.LogInfo("PersistenceService.Migration", $"Marked legacy database file as migrated: {migratedPath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.Migration", $"Could not mark legacy database file as migrated '{path}': {ex.Message}");
            }
        }

        private void DeleteCoreSyncState()
        {
            var path = _folderService.CoreSyncStatePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                SafeFileService.DeleteFile(path);
                LoggingService.LogInfo("PersistenceService.Migration", $"Deleted obsolete core sync state: {path}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.Migration", $"Could not delete obsolete core sync state '{path}': {ex.Message}");
            }
        }

        // ============ ENCRYPTION ============

        private byte[] Encrypt(string plainText)
        {
            return EncryptDatabasePayload(plainText);
        }

        private string Decrypt(byte[] cipherText)
        {
            if (TryDecryptDatabasePayload(cipherText, out var plainText))
                return plainText;

            // Keep old fixed-IV payloads readable during the SQLite/core.db transition.
            return DecryptLegacyPayload(cipherText);
        }

        internal static byte[] EncryptDatabasePayload(string plainText)
        {
            var iv = new byte[AesIvSizeBytes];
            RandomNumberGenerator.Fill(iv);

            using var aes = Aes.Create();
            aes.Key = SecureKey;
            aes.IV = iv;

            byte[] cipherText;
            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            using (var ms = new MemoryStream())
            {
                using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                using (var sw = new StreamWriter(cs, Encoding.UTF8))
                {
                    sw.Write(plainText);
                }

                cipherText = ms.ToArray();
            }

            using var payload = new MemoryStream();
            payload.Write(DatabaseEnvelopeMagic, 0, DatabaseEnvelopeMagic.Length);
            payload.WriteByte(DatabaseEnvelopeVersion);
            payload.Write(iv, 0, iv.Length);
            payload.Write(cipherText, 0, cipherText.Length);

            var payloadWithoutMac = payload.ToArray();
            using var hmac = new HMACSHA256(HmacKey);
            var mac = hmac.ComputeHash(payloadWithoutMac);
            payload.Write(mac, 0, mac.Length);
            return payload.ToArray();
        }

        internal static bool TryDecryptDatabasePayload(byte[] encryptedData, out string plainText)
        {
            plainText = string.Empty;
            if (!IsV2Envelope(encryptedData))
                return false;

            var macOffset = encryptedData.Length - HmacSizeBytes;
            using var hmac = new HMACSHA256(HmacKey);
            var expectedMac = hmac.ComputeHash(encryptedData, 0, macOffset);
            var actualMac = encryptedData.AsSpan(macOffset, HmacSizeBytes);
            if (!CryptographicOperations.FixedTimeEquals(expectedMac, actualMac))
                throw new CryptographicException("database.json HMAC validation failed.");

            var ivOffset = DatabaseEnvelopeMagic.Length + 1;
            var cipherOffset = ivOffset + AesIvSizeBytes;
            var cipherLength = macOffset - cipherOffset;

            using var aes = Aes.Create();
            aes.Key = SecureKey;
            aes.IV = encryptedData.AsSpan(ivOffset, AesIvSizeBytes).ToArray();

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(encryptedData, cipherOffset, cipherLength);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            plainText = sr.ReadToEnd();
            return true;
        }

        private static bool IsV2Envelope(byte[] encryptedData)
        {
            if (encryptedData.Length < DatabaseEnvelopeMagic.Length + 1 + AesIvSizeBytes + HmacSizeBytes)
                return false;

            if (encryptedData[DatabaseEnvelopeMagic.Length] != DatabaseEnvelopeVersion)
                return false;

            return encryptedData.AsSpan(0, DatabaseEnvelopeMagic.Length)
                .SequenceEqual(DatabaseEnvelopeMagic);
        }

        private static string DecryptLegacyPayload(byte[] cipherText)
        {
            using var aes = Aes.Create();
            aes.Key = SecureKey;
            aes.IV = SecureIV;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipherText);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }

        private string ComputeHash(byte[] data)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(data);
            return Convert.ToBase64String(hash);
        }

        // ============ UTILITY ============

        private static void CopyDirectoryRecursive(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                SafeFileService.CopyFile(file, Path.Combine(destDir, Path.GetFileName(file)));
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryDeleteDirectory: {ex.Message}");
            }
        }
    }
}

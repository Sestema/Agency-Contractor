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

    public sealed class CoreSyncState
    {
        public string StorageVersion { get; set; } = "sqlite-core-v1";
        public string LastSyncedAtUtc { get; set; } = string.Empty;
        public string LastDataHash { get; set; } = string.Empty;
        public string LastCoreWriteUtc { get; set; } = string.Empty;
        public string LastJsonWriteUtc { get; set; } = string.Empty;
    }

    public class PersistenceService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly CoreDbService _coreDbService;
        private static readonly SemaphoreSlim _saveLock = new(1, 1);

        private static string Res(string key) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? key;

        private const string OldDataFileName = "company_data.json";
        private const string OldChecksumExtension = ".sha256";
        private static readonly byte[] DatabaseEnvelopeMagic = Encoding.ASCII.GetBytes("ACD2");
        private const byte DatabaseEnvelopeVersion = 2;
        private const int AesIvSizeBytes = 16;
        private const int HmacSizeBytes = 32;

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
        /// Falls back to legacy database.json and company_data.json with automatic migration.
        /// </summary>
        public DatabaseRoot LoadDatabase()
        {
            var rootPath = _folderService.RootPath;
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return new DatabaseRoot();

            var dbPath = _folderService.DatabaseFilePath;
            var hasCore = TryLoadCoreDatabase(out var coreDatabase);
            var hasJson = File.Exists(dbPath);

            // 1. Transition mode: inspect both storages when possible.
            if (hasCore && hasJson)
            {
                var jsonDb = TryLoadJsonDatabase(dbPath);
                if (jsonDb != null)
                    return ResolveTransitionState(coreDatabase, jsonDb, dbPath);

                return coreDatabase;
            }

            // 2. Primary SQLite storage.
            if (hasCore)
                return coreDatabase;

            // 3. Fallback: try legacy database.json and migrate it to core.db
            if (hasJson)
            {
                var jsonDb = TryLoadJsonDatabase(dbPath);
                if (jsonDb != null)
                {
                    TryMigrateJsonDatabaseToCore(jsonDb, "initial_json_migration");
                    return jsonDb;
                }

                return TryRestoreFromBackup();
            }

            // 4. Fallback: try old format (company_data.json) and migrate
            var oldPath = Path.Combine(rootPath, OldDataFileName);
            if (File.Exists(oldPath))
            {
                Debug.WriteLine("PersistenceService: found old company_data.json, migrating...");
                return MigrateFromOldFormat(rootPath, oldPath);
            }

            // 5. Clean install — no data
            return new DatabaseRoot();
        }

        private DatabaseRoot ResolveTransitionState(DatabaseRoot coreDatabase, DatabaseRoot jsonDatabase, string jsonPath)
        {
            var coreHash = ComputeDatabaseHash(coreDatabase);
            var jsonHash = ComputeDatabaseHash(jsonDatabase);
            if (string.Equals(coreHash, jsonHash, StringComparison.Ordinal))
            {
                EnsureSyncStateMatches(coreHash);
                return coreDatabase;
            }

            var syncState = LoadSyncState();
            var coreChangedSinceSync = syncState == null || !string.Equals(syncState.LastDataHash, coreHash, StringComparison.Ordinal);
            var jsonChangedSinceSync = syncState == null || !string.Equals(syncState.LastDataHash, jsonHash, StringComparison.Ordinal);

            var coreLastWrite = GetSafeLastWriteUtc(_coreDbService.DatabasePath);
            var jsonLastWrite = GetSafeLastWriteUtc(jsonPath);

            if (!coreChangedSinceSync && jsonChangedSinceSync)
            {
                LoggingService.LogWarning("PersistenceService.Sync", "database.json changed after the last SQLite sync. Importing JSON changes into core.db.");
                TryMigrateJsonDatabaseToCore(jsonDatabase, "json_changed_after_sync");
                return jsonDatabase;
            }

            if (coreChangedSinceSync && !jsonChangedSinceSync)
            {
                LoggingService.LogWarning("PersistenceService.Sync", "core.db changed after the last sync while database.json stayed on the previous snapshot. Refreshing JSON snapshot from SQLite.");
                SaveLegacyJsonSnapshot(coreDatabase);
                WriteSyncState(coreHash);
                return coreDatabase;
            }

            if (jsonLastWrite > coreLastWrite)
            {
                LoggingService.LogWarning("PersistenceService.Sync", "Mixed-version data divergence detected. database.json is newer than core.db, so JSON changes are being imported into SQLite.");
                TryMigrateJsonDatabaseToCore(jsonDatabase, "json_newer_than_core");
                return jsonDatabase;
            }

            LoggingService.LogWarning("PersistenceService.Sync", "Mixed-version data divergence detected. core.db is treated as newer than database.json, and JSON snapshot is being refreshed.");
            SaveLegacyJsonSnapshot(coreDatabase);
            WriteSyncState(coreHash);
            return coreDatabase;
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
                LoggingService.LogInfo("PersistenceService.Migration", $"database.json was synchronized into SQLite/core.db ({reason}).");
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
            if (_coreDbService.Exists)
                CreateBackup(_coreDbService.DatabasePath);

            _coreDbService.SaveDatabase(database);

            // Keep an encrypted JSON snapshot during the mixed-version transition period.
            SaveLegacyJsonSnapshot(database);
            WriteSyncState(ComputeDatabaseHash(database));
        }

        private void SaveLegacyJsonSnapshot(DatabaseRoot database)
        {
            var dbPath = _folderService.DatabaseFilePath;
            if (string.IsNullOrEmpty(dbPath))
                return;

            var json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = true });
            var encryptedData = Encrypt(json);
            SafeFileService.WriteBytesAtomic(dbPath, encryptedData);

            var checksum = ComputeHash(encryptedData);
            SafeFileService.WriteTextAtomic(_folderService.DatabaseChecksumPath, checksum, Encoding.UTF8);
        }

        private void EnsureSyncStateMatches(string dataHash)
        {
            var state = LoadSyncState();
            if (state != null && string.Equals(state.LastDataHash, dataHash, StringComparison.Ordinal))
                return;

            WriteSyncState(dataHash);
        }

        private CoreSyncState? LoadSyncState()
        {
            var path = _folderService.CoreSyncStatePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                return SafeFileService.ReadJsonOrDefault<CoreSyncState?>(path, null, encoding: Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.Sync", $"Could not read core sync state: {ex.Message}");
                return null;
            }
        }

        private void WriteSyncState(string dataHash)
        {
            var path = _folderService.CoreSyncStatePath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                var state = new CoreSyncState
                {
                    LastDataHash = dataHash,
                    LastSyncedAtUtc = DateTime.UtcNow.ToString("O"),
                    LastCoreWriteUtc = GetSafeLastWriteUtc(_coreDbService.DatabasePath).ToString("O"),
                    LastJsonWriteUtc = GetSafeLastWriteUtc(_folderService.DatabaseFilePath).ToString("O")
                };
                SafeFileService.WriteJsonAtomic(path, state, encoding: Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PersistenceService.Sync", $"Could not write core sync state: {ex.Message}");
            }
        }

        private static DateTime GetSafeLastWriteUtc(string path)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                    ? File.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
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

        private string ComputeDatabaseHash(DatabaseRoot database)
        {
            var json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = false });
            return ComputeHash(Encoding.UTF8.GetBytes(json));
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

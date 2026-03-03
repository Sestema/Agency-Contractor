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

    public class PersistenceService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private static readonly SemaphoreSlim _saveLock = new(1, 1);

        private static string Res(string key) =>
            System.Windows.Application.Current?.TryFindResource(key) as string ?? key;

        private const string OldDataFileName = "company_data.json";
        private const string OldChecksumExtension = ".sha256";

        private static readonly byte[] SecureKey = new byte[32];
        private static readonly byte[] SecureIV = new byte[16];

        static PersistenceService()
        {
            var keyBytes = Encoding.UTF8.GetBytes("AgencyContractorSecretKey2024_Secure");
            Array.Copy(keyBytes, SecureKey, Math.Min(keyBytes.Length, SecureKey.Length));
            var ivBytes = Encoding.UTF8.GetBytes("AgencyContractor");
            Array.Copy(ivBytes, SecureIV, Math.Min(ivBytes.Length, SecureIV.Length));
        }

        public PersistenceService(AppSettingsService appSettingsService, FolderService folderService)
        {
            _appSettingsService = appSettingsService;
            _folderService = folderService;
        }

        // ============ NEW FORMAT: database.json ============

        /// <summary>
        /// Save the full database (companies + settings) as encrypted database.json.
        /// </summary>
        public async Task SaveDatabaseAsync(IEnumerable<EmployerCompany> companies)
        {
            var dbPath = _folderService.DatabaseFilePath;
            if (string.IsNullOrEmpty(dbPath)) return;

            await _saveLock.WaitAsync();
            try
            {
                var db = new DatabaseRoot
                {
                    Version = "2.0",
                    Companies = companies.ToList(),
                    Settings = new DatabaseSettings
                    {
                        LanguageCode = _appSettingsService.Settings.LanguageCode ?? "uk",
                        SelectedCompanyId = _appSettingsService.Settings.SelectedCompanyId ?? string.Empty,
                        AppVersion = _appSettingsService.Settings.AppVersion
                    }
                };

                var json = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });

                if (File.Exists(dbPath))
                {
                    CreateBackup(dbPath);
                }

                var encryptedData = Encrypt(json);
                var tempPath = dbPath + ".tmp";
                RetryHelper.Execute(() =>
                {
                    File.WriteAllBytes(tempPath, encryptedData);
                    File.Move(tempPath, dbPath, true);
                });

                var checksum = ComputeHash(encryptedData);
                var checksumTemp = _folderService.DatabaseChecksumPath + ".tmp";
                RetryHelper.Execute(() =>
                {
                    File.WriteAllText(checksumTemp, checksum, Encoding.UTF8);
                    File.Move(checksumTemp, _folderService.DatabaseChecksumPath, true);
                });
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
        /// Load the full database from database.json.
        /// Falls back to old company_data.json format with automatic migration.
        /// </summary>
        public DatabaseRoot LoadDatabase()
        {
            var rootPath = _folderService.RootPath;
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
                return new DatabaseRoot();

            // 1. Try new format: database.json
            var dbPath = _folderService.DatabaseFilePath;
            if (File.Exists(dbPath))
            {
                try
                {
                    var encryptedData = File.ReadAllBytes(dbPath);

                    // Verify integrity
                    var checksumPath = _folderService.DatabaseChecksumPath;
                    if (File.Exists(checksumPath))
                    {
                        var storedChecksum = File.ReadAllText(checksumPath, Encoding.UTF8);
                        var currentChecksum = ComputeHash(encryptedData);
                        if (storedChecksum != currentChecksum)
                        {
                            Debug.WriteLine("PersistenceService: database.json integrity check failed, attempting backup restore...");
                            return TryRestoreFromBackup();
                        }
                    }

                    var json = Decrypt(encryptedData);
                    var db = JsonSerializer.Deserialize<DatabaseRoot>(json);
                    if (db != null)
                    {
                        Debug.WriteLine($"PersistenceService.LoadDatabase: loaded {db.Companies.Count} companies (v{db.Version})");
                        return db;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PersistenceService.LoadDatabase (new format) failed: {ex.Message}");
                    return TryRestoreFromBackup();
                }
            }

            // 2. Fallback: try old format (company_data.json) and migrate
            var oldPath = Path.Combine(rootPath, OldDataFileName);
            if (File.Exists(oldPath))
            {
                Debug.WriteLine("PersistenceService: found old company_data.json, migrating...");
                return MigrateFromOldFormat(rootPath, oldPath);
            }

            // 3. Clean install — no data
            return new DatabaseRoot();
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
                var encryptedData = File.ReadAllBytes(oldFilePath);

                // Verify old checksum if exists
                var oldChecksumPath = oldFilePath + OldChecksumExtension;
                if (File.Exists(oldChecksumPath))
                {
                    var storedChecksum = File.ReadAllText(oldChecksumPath, Encoding.UTF8);
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
                var langCode = _appSettingsService.Settings.LanguageCode ?? "uk";
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

                // 6. Save new database.json
                var newJson = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
                var newEncrypted = Encrypt(newJson);
                var newDbPath = Path.Combine(rootPath, "database.json");
                File.WriteAllBytes(newDbPath, newEncrypted);
                File.WriteAllText(newDbPath + ".sha256", ComputeHash(newEncrypted), Encoding.UTF8);

                // 7. Rename old files (keep as backup, don't delete)
                try
                {
                    File.Move(oldFilePath, oldFilePath + ".migrated");
                    if (File.Exists(oldChecksumPath))
                        File.Move(oldChecksumPath, oldChecksumPath + ".migrated");
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

                var json = File.ReadAllText(indexPath, Encoding.UTF8);
                var currentFolderName = Path.GetFileName(templatesFolder);

                // Replace old "Templates/" prefix with current folder name
                bool changed = false;
                if (currentFolderName != "Templates" && json.Contains("\"Templates/"))
                {
                    json = json.Replace("\"Templates/", $"\"{currentFolderName}/");
                    changed = true;
                }
                else if (currentFolderName != "Шаблони" && json.Contains("\"Шаблони/"))
                {
                    json = json.Replace("\"Шаблони/", $"\"{currentFolderName}/");
                    changed = true;
                }

                if (changed)
                {
                    File.WriteAllText(indexPath, json, Encoding.UTF8);
                    Debug.WriteLine($"Migration: updated template index paths in {indexPath}");
                }
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
                var backupPath = Path.Combine(backupsFolder, $"database_{timestamp}.json.bak");
                File.Copy(sourceFilePath, backupPath, true);

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
                var encryptedData = File.ReadAllBytes(latestBackup.FullName);
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
            SaveDatabaseAsync(companies).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Save companies (backward-compatible wrapper).
        /// </summary>
        public void SaveCompanies(IEnumerable<EmployerCompany> companies)
        {
            SaveDatabase(companies);
        }

        // ============ ENCRYPTION ============

        private byte[] Encrypt(string plainText)
        {
            using var aes = Aes.Create();
            aes.Key = SecureKey;
            aes.IV = SecureIV;

            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
            return ms.ToArray();
        }

        private string Decrypt(byte[] cipherText)
        {
            using var aes = Aes.Create();
            aes.Key = SecureKey;
            aes.IV = SecureIV;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(cipherText);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);
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
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
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

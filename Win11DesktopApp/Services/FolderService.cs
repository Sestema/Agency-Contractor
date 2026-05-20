using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Centralized service for all folder path logic.
    /// Provides unified path construction and folder name normalization.
    /// Supports localized folder names with fallback search.
    /// </summary>
    public class FolderService
    {
        private readonly AppSettingsService _appSettingsService;

        public FolderService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService;
        }

        /// <summary>
        /// Root folder path from settings.
        /// </summary>
        public string RootPath => _appSettingsService.Settings.RootFolderPath;

        /// <summary>
        /// Language code used for physical folder names on disk.
        /// Folder structure must stay aligned with the UI language only; document language
        /// may change independently and must not redirect file-system paths.
        /// </summary>
        public string FolderLanguageCode => _appSettingsService.Settings.LanguageCode ?? "uk";

        // ============ PATH CONSTRUCTION ============

        /// <summary>
        /// Get the company folder path: {Root}/{CompanyName}
        /// </summary>
        public string GetCompanyFolder(string companyName)
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            var safeName = NormalizeFolderName(companyName);
            return Path.Combine(RootPath, safeName);
        }

        /// <summary>
        /// Get the employees folder path: {Root}/{CompanyName}/{Працівники|Employees}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetEmployeesFolder(string companyName)
        {
            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrEmpty(companyFolder)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(companyFolder, FolderNames.GetEmployeesFolder(FolderLanguageCode), FolderNames.AllEmployeesFolderNames);
        }

        /// <summary>
        /// Get the templates folder path: {Root}/{CompanyName}/{Шаблони|Templates}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetTemplatesFolder(string companyName)
        {
            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrEmpty(companyFolder)) return string.Empty;
            return FindOrCreateTemplateSubfolder(companyFolder, FolderNames.GetTemplatesFolder(FolderLanguageCode));
        }

        /// <summary>
        /// Get the payment folder path: {Root}/{CompanyName}/{Виплата|Payment}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetPaymentFolder(string companyName)
        {
            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrEmpty(companyFolder)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(companyFolder, FolderNames.GetPaymentFolder(FolderLanguageCode), FolderNames.AllPaymentFolderNames);
        }

        /// <summary>
        /// Get the global archive folder path: {Root}/{Архів|Archive}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetArchiveFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(RootPath, FolderNames.GetArchiveFolder(FolderLanguageCode), FolderNames.AllArchiveFolderNames);
        }

        /// <summary>
        /// Get the backups folder path: {Root}/backups
        /// </summary>
        public string GetBackupsFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return Path.Combine(RootPath, "backups");
        }

        /// <summary>
        /// Get the SQLite system folder path: {Root}/SQLite
        /// </summary>
        public string GetSqliteFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return Path.Combine(RootPath, "SQLite");
        }

        /// <summary>
        /// Full path to the local SQLite database file: {Root}/SQLite/app.db
        /// </summary>
        public string LocalDbPath
        {
            get
            {
                var sqliteFolder = GetSqliteFolder();
                return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "app.db");
            }
        }

        /// <summary>
        /// Full path to the employee index SQLite database file: {Root}/SQLite/employee_index.db
        /// </summary>
        public string EmployeeIndexDbPath
        {
            get
            {
                var sqliteFolder = GetSqliteFolder();
                return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "employee_index.db");
            }
        }

        /// <summary>
        /// Full path to the core SQLite database file: {Root}/SQLite/core.db
        /// </summary>
        public string CoreDbPath
        {
            get
            {
                var sqliteFolder = GetSqliteFolder();
                return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "core.db");
            }
        }

        /// <summary>
        /// Full path to the core sync state file: {Root}/SQLite/core.sync.json
        /// </summary>
        public string CoreSyncStatePath
        {
            get
            {
                var sqliteFolder = GetSqliteFolder();
                return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "core.sync.json");
            }
        }

        /// <summary>
        /// Get the salary SQLite folder path: {Root}/SQLite/Vyplaty
        /// </summary>
        public string GetSalaryDbFolder()
        {
            var sqliteFolder = GetSqliteFolder();
            return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "Vyplaty");
        }

        /// <summary>
        /// Folder for lightweight cross-PC sync event files: {Root}/SQLite/SyncEvents.
        /// </summary>
        public string GetSyncEventsFolder()
        {
            var sqliteFolder = GetSqliteFolder();
            return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "SyncEvents");
        }

        public string GetSyncEventsInboxFolder()
        {
            var folder = GetSyncEventsFolder();
            return string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, "Inbox");
        }

        public string GetSyncEventsReadFolder()
        {
            var folder = GetSyncEventsFolder();
            return string.IsNullOrEmpty(folder) ? string.Empty : Path.Combine(folder, "Read");
        }

        public string GetLocksFolder()
        {
            var sqliteFolder = GetSqliteFolder();
            return string.IsNullOrEmpty(sqliteFolder) ? string.Empty : Path.Combine(sqliteFolder, "Locks");
        }

        /// <summary>
        /// Full path to the database file: {Root}/database.json
        /// </summary>
        public string DatabaseFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(RootPath)) return string.Empty;
                return Path.Combine(RootPath, "database.json");
            }
        }

        /// <summary>
        /// Full path to the database checksum: {Root}/database.json.sha256
        /// </summary>
        public string DatabaseChecksumPath
        {
            get
            {
                if (string.IsNullOrEmpty(RootPath)) return string.Empty;
                return Path.Combine(RootPath, "database.json.sha256");
            }
        }

        // ============ FOLDER STRUCTURE ============

        /// <summary>
        /// Creates the full folder structure for a company:
        /// {Root}/{CompanyName}/
        /// {Root}/{CompanyName}/{Працівники|Employees}/
        /// {Root}/{CompanyName}/{Шаблони|Templates}/
        /// Employee/payment folders follow the preferred language name. The templates folder
        /// keeps whichever localized name already exists so templates stay visible across PCs.
        /// </summary>
        public void EnsureCompanyStructure(string companyName)
        {
            if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(companyName)) return;
            try
            {
                var companyFolder = GetCompanyFolder(companyName);
                Directory.CreateDirectory(companyFolder);

                EnsureLocalizedSubfolder(companyFolder, FolderNames.GetEmployeesFolder(FolderLanguageCode), FolderNames.AllEmployeesFolderNames);
                EnsureExistingLocalizedSubfolder(companyFolder, FolderNames.GetTemplatesFolder(FolderLanguageCode), FolderNames.AllTemplatesFolderNames);
                EnsureLocalizedSubfolder(companyFolder, FolderNames.GetPaymentFolder(FolderLanguageCode), FolderNames.AllPaymentFolderNames);

                Debug.WriteLine($"FolderService.EnsureCompanyStructure: {companyFolder}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.EnsureCompanyStructure error: {ex.Message}");
            }
        }

        /// <summary>
        /// Renames a company folder when the company name changes.
        /// </summary>
        public void RenameCompanyFolder(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return;

            try
            {
                var oldFolder = Path.Combine(RootPath, NormalizeFolderName(oldName));
                var newFolder = Path.Combine(RootPath, NormalizeFolderName(newName));

                if (Directory.Exists(oldFolder) && !Directory.Exists(newFolder))
                {
                    Directory.Move(oldFolder, newFolder);
                    Debug.WriteLine($"FolderService.RenameCompanyFolder: {oldFolder} -> {newFolder}");
                }
                else if (!Directory.Exists(oldFolder))
                {
                    Debug.WriteLine($"FolderService.RenameCompanyFolder: old folder not found, creating new structure.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.RenameCompanyFolder error: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the archive folder exists.
        /// </summary>
        public string GetCandidatesFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(RootPath, FolderNames.GetCandidatesFolder(FolderLanguageCode), FolderNames.AllCandidatesFolderNames);
        }

        public string GetRecentlyDeletedFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(RootPath, FolderNames.GetRecentlyDeletedFolder(FolderLanguageCode), FolderNames.AllRecentlyDeletedFolderNames);
        }

        public void EnsureArchiveFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return;
            try
            {
                EnsureLocalizedSubfolder(RootPath, FolderNames.GetArchiveFolder(FolderLanguageCode), FolderNames.AllArchiveFolderNames);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.EnsureArchiveFolder error: {ex.Message}");
            }
        }

        public void EnsureRecentlyDeletedFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return;
            try
            {
                EnsureLocalizedSubfolder(RootPath, FolderNames.GetRecentlyDeletedFolder(FolderLanguageCode), FolderNames.AllRecentlyDeletedFolderNames);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.EnsureRecentlyDeletedFolder error: {ex.Message}");
            }
        }

        public int GetCompanyEmployeeFolderCount(string companyName)
        {
            try
            {
                var employeesFolder = GetEmployeesFolder(companyName);
                if (string.IsNullOrWhiteSpace(employeesFolder) || !Directory.Exists(employeesFolder))
                    return 0;

                return Directory.GetDirectories(employeesFolder).Length;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.GetCompanyEmployeeFolderCount error: {ex.Message}");
                return 0;
            }
        }

        public bool DeleteCompanyFolder(string companyName)
        {
            if (string.IsNullOrWhiteSpace(RootPath) || string.IsNullOrWhiteSpace(companyName))
                return true;

            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrWhiteSpace(companyFolder) || !Directory.Exists(companyFolder))
                return true;

            try
            {
                NormalizeAttributesRecursive(companyFolder);
                Directory.Delete(companyFolder, true);
                Debug.WriteLine($"FolderService.DeleteCompanyFolder: {companyFolder}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.DeleteCompanyFolder error: {ex.Message}");
                return false;
            }
        }

        // ============ NORMALIZATION ============

        /// <summary>
        /// Unified folder name normalization. Removes invalid characters, replaces spaces with underscores.
        /// </summary>
        public static string NormalizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "_unnamed_";
            var invalid = Path.GetInvalidFileNameChars();
            var safe = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
            safe = safe.Replace(" ", "_");
            safe = safe.TrimEnd('.', ' ');
            while (safe.Contains("__", StringComparison.Ordinal))
                safe = safe.Replace("__", "_", StringComparison.Ordinal);
            return string.IsNullOrWhiteSpace(safe) ? "_unnamed_" : safe;
        }

        // ============ HELPERS ============

        /// <summary>
        /// Finds an existing subfolder using any of the known names (fallback),
        /// or returns the path using the preferred name.
        /// Does NOT create the folder — callers should create it when needed.
        /// </summary>
        private string FindOrCreateLocalizedSubfolder(string parentFolder, string preferredName, string[] allNames)
        {
            var preferredPath = Path.Combine(parentFolder, preferredName);
            if (Directory.Exists(preferredPath))
                return preferredPath;

            foreach (var name in allNames)
            {
                var fallbackPath = Path.Combine(parentFolder, name);
                if (Directory.Exists(fallbackPath))
                    return fallbackPath;
            }

            return preferredPath;
        }

        private string FindOrCreateTemplateSubfolder(string parentFolder, string preferredName)
        {
            var preferredPath = Path.Combine(parentFolder, preferredName);
            var existingFolders = FolderNames.AllTemplatesFolderNames
                .Select(name => Path.Combine(parentFolder, name))
                .Where(Directory.Exists)
                .ToArray();

            var indexedFolder = existingFolders.FirstOrDefault(path => File.Exists(Path.Combine(path, "index.json")));
            if (!string.IsNullOrWhiteSpace(indexedFolder))
                return indexedFolder;

            var nonEmptyFolder = existingFolders.FirstOrDefault(DirectoryHasEntries);
            if (!string.IsNullOrWhiteSpace(nonEmptyFolder))
                return nonEmptyFolder;

            var preferredExistingFolder = existingFolders.FirstOrDefault(path =>
                string.Equals(path, preferredPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferredExistingFolder))
                return preferredExistingFolder;

            return existingFolders.FirstOrDefault() ?? preferredPath;
        }

        private static bool DirectoryHasEntries(string folderPath)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(folderPath).Any();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures a localized subfolder exists. If it already exists under a different
        /// language name, renames it to the preferred name instead of creating a duplicate.
        /// </summary>
        private void EnsureLocalizedSubfolder(string parentFolder, string preferredName, string[] allNames)
        {
            var preferredPath = Path.Combine(parentFolder, preferredName);
            if (Directory.Exists(preferredPath))
                return;

            foreach (var name in allNames)
            {
                if (name == preferredName) continue;
                var existingPath = Path.Combine(parentFolder, name);
                if (Directory.Exists(existingPath))
                {
                    try
                    {
                        Directory.Move(existingPath, preferredPath);
                        Debug.WriteLine($"FolderService: Renamed '{existingPath}' -> '{preferredPath}'");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"FolderService: Rename failed ({ex.Message}), keeping existing folder.");
                    }
                    return;
                }
            }

            Directory.CreateDirectory(preferredPath);
        }

        /// <summary>
        /// Ensures a localized subfolder exists without renaming an existing localized variant.
        /// Used for shared template storage so PCs with different UI languages keep one folder.
        /// </summary>
        private void EnsureExistingLocalizedSubfolder(string parentFolder, string preferredName, string[] allNames)
        {
            var folderPath = FindOrCreateLocalizedSubfolder(parentFolder, preferredName, allNames);
            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);
        }

        private static void NormalizeAttributesRecursive(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var filePath in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            foreach (var dirPath in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(dirPath, FileAttributes.Normal);
                }
                catch
                {
                }
            }

            try
            {
                File.SetAttributes(directory, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Check if the root folder looks like it contains our data
        /// (has database.json or company_data.json or company-looking subfolders).
        /// </summary>
        public bool IsValidDataFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return false;

            // New format
            if (File.Exists(Path.Combine(folderPath, "database.json")))
                return true;

            // Old format
            if (File.Exists(Path.Combine(folderPath, "company_data.json")))
                return true;

            return false;
        }
    }
}

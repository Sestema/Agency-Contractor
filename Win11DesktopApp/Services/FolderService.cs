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
        /// Language code for folder names: prefers DocumentLanguage, falls back to LanguageCode.
        /// </summary>
        public string LangCode => !string.IsNullOrEmpty(_appSettingsService.Settings.DocumentLanguage)
            ? _appSettingsService.Settings.DocumentLanguage
            : _appSettingsService.Settings.LanguageCode ?? "uk";

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
            return FindOrCreateLocalizedSubfolder(companyFolder, FolderNames.GetEmployeesFolder(LangCode), FolderNames.AllEmployeesFolderNames);
        }

        /// <summary>
        /// Get the templates folder path: {Root}/{CompanyName}/{Шаблони|Templates}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetTemplatesFolder(string companyName)
        {
            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrEmpty(companyFolder)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(companyFolder, FolderNames.GetTemplatesFolder(LangCode), FolderNames.AllTemplatesFolderNames);
        }

        /// <summary>
        /// Get the payment folder path: {Root}/{CompanyName}/{Виплата|Payment}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetPaymentFolder(string companyName)
        {
            var companyFolder = GetCompanyFolder(companyName);
            if (string.IsNullOrEmpty(companyFolder)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(companyFolder, FolderNames.GetPaymentFolder(LangCode), FolderNames.AllPaymentFolderNames);
        }

        /// <summary>
        /// Get the global archive folder path: {Root}/{Архів|Archive}
        /// Uses fallback search if the preferred locale folder doesn't exist.
        /// </summary>
        public string GetArchiveFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return string.Empty;
            return FindOrCreateLocalizedSubfolder(RootPath, FolderNames.GetArchiveFolder(LangCode), FolderNames.AllArchiveFolderNames);
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
        /// If subfolders already exist under a different language name, they are renamed.
        /// </summary>
        public void EnsureCompanyStructure(string companyName)
        {
            if (string.IsNullOrEmpty(RootPath) || string.IsNullOrEmpty(companyName)) return;
            try
            {
                var companyFolder = GetCompanyFolder(companyName);
                Directory.CreateDirectory(companyFolder);

                EnsureLocalizedSubfolder(companyFolder, FolderNames.GetEmployeesFolder(LangCode), FolderNames.AllEmployeesFolderNames);
                EnsureLocalizedSubfolder(companyFolder, FolderNames.GetTemplatesFolder(LangCode), FolderNames.AllTemplatesFolderNames);
                EnsureLocalizedSubfolder(companyFolder, FolderNames.GetPaymentFolder(LangCode), FolderNames.AllPaymentFolderNames);

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
            return FindOrCreateLocalizedSubfolder(RootPath, FolderNames.GetCandidatesFolder(LangCode), FolderNames.AllCandidatesFolderNames);
        }

        public void EnsureArchiveFolder()
        {
            if (string.IsNullOrEmpty(RootPath)) return;
            try
            {
                EnsureLocalizedSubfolder(RootPath, FolderNames.GetArchiveFolder(LangCode), FolderNames.AllArchiveFolderNames);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FolderService.EnsureArchiveFolder error: {ex.Message}");
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

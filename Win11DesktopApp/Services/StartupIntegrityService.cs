using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class StartupIntegrityService
    {
        private const string LegacyDatabaseFileName = "company_data.json";
        private const string TemplateIndexFileName = "index.json";

        private readonly FolderService _folderService;
        private readonly PersistenceService _persistenceService;
        private int _recoveryCount;
        private int _warningCount;

        public StartupIntegrityService(FolderService folderService, PersistenceService persistenceService)
        {
            _folderService = folderService;
            _persistenceService = persistenceService;
        }

        public void IncludeSettingsStartupState(AppSettingsService appSettingsService)
        {
            if (appSettingsService.WasRecoveredFromBackupOnLoad)
                RegisterRecovery();

            if (appSettingsService.WasResetToDefaultsOnLoad)
                RegisterWarning();
        }

        public void IncludeFinanceStartupState(FinanceService financeService)
        {
            if (financeService.WasRecoveredFromBackupOnLoad)
                RegisterRecovery();

            if (financeService.WasResetToDefaultsOnLoad)
                RegisterWarning();
        }

        public void RunQuickCheck()
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrWhiteSpace(_folderService.RootPath))
                {
                    LoggingService.LogWarning("StartupIntegrityService.QuickCheck",
                        "RootFolderPath is empty. Startup data checks skipped.");
                    return;
                }

                EnsureRootFolders();
                EnsureDatabaseChecksumOrRestore();
                EnsureArchiveLogIsReadableOrRepair();
                EnsureActivityLogIsReadableOrRepair();

                LoggingService.LogInfo("StartupIntegrityService.QuickCheck",
                    $"Completed in {stopwatch.ElapsedMilliseconds} ms.");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("StartupIntegrityService.QuickCheck", ex);
            }
        }

        public void RunBackgroundCheck(IEnumerable<EmployerCompany> companies)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrWhiteSpace(_folderService.RootPath) || !Directory.Exists(_folderService.RootPath))
                    return;

                var companyList = companies?.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList()
                    ?? new List<EmployerCompany>();

                int repairedTemplatePaths = 0;
                int warnings = 0;

                foreach (var company in companyList)
                {
                    try
                    {
                        _folderService.EnsureCompanyStructure(company.Name);
                        var result = ValidateTemplateIndex(company.Name);
                        repairedTemplatePaths += result.RepairedEntries;
                        warnings += result.WarningCount;
                    }
                    catch (Exception ex)
                    {
                        warnings++;
                        RegisterWarning();
                        LoggingService.LogWarning("StartupIntegrityService.BackgroundCheck",
                            $"Company '{company.Name}' validation failed: {ex.Message}");
                    }
                }

                LoggingService.LogInfo("StartupIntegrityService.BackgroundCheck",
                    $"Completed in {stopwatch.ElapsedMilliseconds} ms. Companies={companyList.Count}, repaired_template_paths={repairedTemplatePaths}, warnings={warnings}.");

                ShowStartupSummaryIfNeeded();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("StartupIntegrityService.BackgroundCheck", ex);
            }
        }

        private void EnsureArchiveLogIsReadableOrRepair()
        {
            try
            {
                var archiveFolder = _folderService.GetArchiveFolder();
                if (string.IsNullOrWhiteSpace(archiveFolder))
                    return;

                var archiveLogPath = Path.Combine(archiveFolder, "archive_log.json");
                if (!File.Exists(archiveLogPath))
                    return;

                var strictOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                try
                {
                    SafeFileService.ReadJsonOrDefault(archiveLogPath, new List<ArchiveLogEntry>(), strictOptions, Encoding.UTF8);
                    return;
                }
                catch (JsonException)
                {
                    // The file may still be recoverable, e.g. a trailing comma in the array.
                }

                var tolerantOptions = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                };

                var entries = SafeFileService.ReadJson<List<ArchiveLogEntry>>(archiveLogPath, tolerantOptions, Encoding.UTF8)
                    ?? new List<ArchiveLogEntry>();

                SafeFileService.WriteJsonAtomic(archiveLogPath, entries, strictOptions, Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.ArchiveLog",
                    $"archive_log.json was auto-repaired during startup. Entries preserved: {entries.Count}.");
            }
            catch (Exception ex)
            {
                RegisterWarning();
                LoggingService.LogError("StartupIntegrityService.ArchiveLog", ex);
            }
        }

        private void EnsureActivityLogIsReadableOrRepair()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_folderService.RootPath))
                    return;

                var activityLogPath = Path.Combine(_folderService.RootPath, "activity_log.json");
                if (!File.Exists(activityLogPath))
                    return;

                var strictOptions = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                try
                {
                    SafeFileService.ReadJsonOrDefault(activityLogPath, new List<ActivityLogEntry>(), strictOptions, Encoding.UTF8);
                    return;
                }
                catch (JsonException)
                {
                    // The file may still be recoverable, e.g. a trailing comma in the array.
                }

                var tolerantOptions = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                };

                var entries = SafeFileService.ReadJson<List<ActivityLogEntry>>(activityLogPath, tolerantOptions, Encoding.UTF8)
                    ?? new List<ActivityLogEntry>();

                SafeFileService.WriteJsonAtomic(activityLogPath, entries, strictOptions, Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.ActivityLog",
                    $"activity_log.json was auto-repaired during startup. Entries preserved: {entries.Count}.");
            }
            catch (Exception ex)
            {
                RegisterWarning();
                LoggingService.LogError("StartupIntegrityService.ActivityLog", ex);
            }
        }

        private void EnsureRootFolders()
        {
            Directory.CreateDirectory(_folderService.RootPath);
            Directory.CreateDirectory(_folderService.GetArchiveFolder());
            Directory.CreateDirectory(_folderService.GetCandidatesFolder());
            Directory.CreateDirectory(_folderService.GetBackupsFolder());
        }

        private void EnsureDatabaseChecksumOrRestore()
        {
            var databasePath = _folderService.DatabaseFilePath;
            if (string.IsNullOrWhiteSpace(databasePath))
                return;

            if (!File.Exists(databasePath))
            {
                var legacyDatabasePath = Path.Combine(_folderService.RootPath, LegacyDatabaseFileName);
                if (File.Exists(legacyDatabasePath))
                {
                    LoggingService.LogInfo("StartupIntegrityService.Database",
                        "Legacy company_data.json detected. Migration will run during load.");
                }

                return;
            }

            var encryptedData = SafeFileService.ReadAllBytes(databasePath);
            var currentChecksum = ComputeHash(encryptedData);
            var checksumPath = _folderService.DatabaseChecksumPath;

            if (!File.Exists(checksumPath))
            {
                SafeFileService.WriteTextAtomic(checksumPath, currentChecksum, Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.Database",
                    "database.json.sha256 was missing and has been recreated.");
                return;
            }

            var storedChecksum = SafeFileService.ReadAllText(checksumPath, Encoding.UTF8).Trim();
            if (string.Equals(storedChecksum, currentChecksum, StringComparison.Ordinal))
                return;

            if (CurrentDatabaseLooksValid(encryptedData))
            {
                SafeFileService.WriteTextAtomic(checksumPath, currentChecksum, Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.Database",
                    "database.json checksum mismatch detected, but the current database is valid. The checksum has been refreshed.");
                return;
            }

            RegisterWarning();
            LoggingService.LogWarning("StartupIntegrityService.Database",
                "database.json checksum mismatch detected. Attempting restore from latest backup.");

            if (TryRestoreDatabaseFromBackup(databasePath, checksumPath))
                return;

            LoggingService.LogError("StartupIntegrityService.Database",
                "database.json checksum mismatch detected, but restore from backup failed.");
        }

        private bool TryRestoreDatabaseFromBackup(string databasePath, string checksumPath)
        {
            try
            {
                var backupsFolder = _folderService.GetBackupsFolder();
                if (!Directory.Exists(backupsFolder))
                    return false;

                var latestBackup = new DirectoryInfo(backupsFolder)
                    .GetFiles("*.bak")
                    .OrderByDescending(file => file.CreationTimeUtc)
                    .FirstOrDefault();

                if (latestBackup == null)
                    return false;

                var restoredData = SafeFileService.ReadAllBytes(latestBackup.FullName);
                SafeFileService.WriteBytesAtomic(databasePath, restoredData);
                SafeFileService.WriteTextAtomic(checksumPath, ComputeHash(restoredData), Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.Database",
                    $"Restored database.json from backup '{latestBackup.Name}'.");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("StartupIntegrityService.TryRestoreDatabaseFromBackup", ex);
                return false;
            }
        }

        private (int RepairedEntries, int WarningCount) ValidateTemplateIndex(string companyName)
        {
            var templatesFolder = _folderService.GetTemplatesFolder(companyName);
            if (string.IsNullOrWhiteSpace(templatesFolder) || !Directory.Exists(templatesFolder))
                return (0, 0);

            var indexPath = Path.Combine(templatesFolder, TemplateIndexFileName);
            if (!File.Exists(indexPath))
            {
                if (Directory.GetDirectories(templatesFolder).Length > 0)
                    return RebuildMissingTemplateIndex(companyName, templatesFolder, indexPath);

                return (0, 0);
            }

            try
            {
                var indexEntries = SafeFileService.ReadJsonOrDefault(indexPath, new List<TemplateIndexEntry>());
                var currentTemplatesFolderName = Path.GetFileName(templatesFolder);
                var repairedEntries = 0;
                var warningCount = 0;
                var changed = false;

                foreach (var entry in indexEntries)
                {
                    var normalizedPath = NormalizeTemplateRelativePath(entry.Path, currentTemplatesFolderName);
                    if (!string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
                    {
                        entry.Path = normalizedPath;
                        repairedEntries++;
                        changed = true;
                    }

                    if (!TemplateEntryExists(companyName, entry.Path))
                    {
                        warningCount++;
                        RegisterWarning();
                        LoggingService.LogWarning("StartupIntegrityService.TemplateIndex",
                            $"Missing template target for company '{companyName}': '{entry.Path}'.");
                    }
                }

                if (changed)
                {
                    SafeFileService.WriteJsonAtomic(indexPath, indexEntries, encoding: Encoding.UTF8);
                    LoggingService.LogInfo("StartupIntegrityService.TemplateIndex",
                        $"Normalized {repairedEntries} template path(s) for company '{companyName}'.");
                }

                return (repairedEntries, warningCount);
            }
            catch (Exception ex)
            {
                RegisterWarning();
                LoggingService.LogWarning("StartupIntegrityService.TemplateIndex",
                    $"Failed to validate template index for company '{companyName}': {ex.Message}");
                return (0, 1);
            }
        }

        private (int RepairedEntries, int WarningCount) RebuildMissingTemplateIndex(string companyName, string templatesFolder, string indexPath)
        {
            try
            {
                var entries = BuildTemplateIndexEntries(templatesFolder);
                if (entries.Count == 0)
                {
                    RegisterWarning();
                    LoggingService.LogWarning("StartupIntegrityService.TemplateIndex",
                        $"Template index is missing for company '{companyName}', and no recoverable templates were found.");
                    return (0, 1);
                }

                SafeFileService.WriteJsonAtomic(indexPath, entries, encoding: Encoding.UTF8);
                RegisterRecovery();
                LoggingService.LogWarning("StartupIntegrityService.TemplateIndex",
                    $"Rebuilt missing template index for company '{companyName}' with {entries.Count} template(s).");
                return (entries.Count, 0);
            }
            catch (Exception ex)
            {
                RegisterWarning();
                LoggingService.LogWarning("StartupIntegrityService.TemplateIndex",
                    $"Failed to rebuild missing template index for company '{companyName}': {ex.Message}");
                return (0, 1);
            }
        }

        private static List<TemplateIndexEntry> BuildTemplateIndexEntries(string templatesFolder)
        {
            var templatesFolderName = Path.GetFileName(templatesFolder);
            var entries = new List<TemplateIndexEntry>();

            foreach (var templateDirectory in Directory.GetDirectories(templatesFolder))
            {
                var entry = TryBuildTemplateIndexEntry(templateDirectory, templatesFolderName);
                if (entry != null)
                    entries.Add(entry);
            }

            return entries
                .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static TemplateIndexEntry? TryBuildTemplateIndexEntry(string templateDirectory, string templatesFolderName)
        {
            var metadataPath = Path.Combine(templateDirectory, "metadata.json");
            var metadata = File.Exists(metadataPath)
                ? SafeFileService.ReadJsonOrDefault(metadataPath, new TemplateMetadata(), encoding: Encoding.UTF8)
                : new TemplateMetadata();

            var format = ResolveTemplateFormat(templateDirectory, metadata.Format);
            if (string.IsNullOrWhiteSpace(format))
                return null;

            var templateFileName = ResolveTemplateFileName(templateDirectory, format);
            if (string.IsNullOrWhiteSpace(templateFileName))
                return null;

            var templateFolderName = Path.GetFileName(templateDirectory);
            return new TemplateIndexEntry
            {
                Name = string.IsNullOrWhiteSpace(metadata.Name) ? templateFolderName : metadata.Name,
                Format = format,
                Path = Path.Combine(templatesFolderName, templateFolderName, templateFileName),
                Updated = metadata.UpdatedAt != default ? metadata.UpdatedAt : Directory.GetLastWriteTime(templateDirectory)
            };
        }

        private static string ResolveTemplateFormat(string templateDirectory, string metadataFormat)
        {
            if (!string.IsNullOrWhiteSpace(metadataFormat))
                return metadataFormat.Trim().ToUpperInvariant();

            if (File.Exists(Path.Combine(templateDirectory, "template.docx")) || File.Exists(Path.Combine(templateDirectory, "content.rtf")))
                return "DOCX";
            if (File.Exists(Path.Combine(templateDirectory, "template.xlsx")))
                return "XLSX";
            if (File.Exists(Path.Combine(templateDirectory, "template.pdf")))
                return "PDF";

            return string.Empty;
        }

        private static string ResolveTemplateFileName(string templateDirectory, string format)
        {
            return format switch
            {
                "DOCX" when File.Exists(Path.Combine(templateDirectory, "template.docx")) => "template.docx",
                "DOCX" when File.Exists(Path.Combine(templateDirectory, "content.rtf")) => "template.docx",
                "XLSX" when File.Exists(Path.Combine(templateDirectory, "template.xlsx")) => "template.xlsx",
                "PDF" when File.Exists(Path.Combine(templateDirectory, "template.pdf")) => "template.pdf",
                _ => string.Empty
            };
        }

        private bool TemplateEntryExists(string companyName, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            var companyFolder = _folderService.GetCompanyFolder(companyName);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return false;

            var fullPath = Path.Combine(companyFolder, relativePath);
            if (File.Exists(fullPath))
                return true;

            var directory = Path.GetDirectoryName(fullPath);
            return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
        }

        private static string NormalizeTemplateRelativePath(string relativePath, string currentTemplatesFolderName)
        {
            var parts = (relativePath ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return relativePath ?? string.Empty;

            if (FolderNames.AllTemplatesFolderNames.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
                parts[0] = currentTemplatesFolderName;

            return Path.Combine(parts);
        }

        private static bool CurrentDatabaseLooksValid(byte[] encryptedData)
        {
            try
            {
                var json = PersistenceService.TryDecryptDatabasePayload(encryptedData, out var v2Json)
                    ? v2Json
                    : Decrypt(encryptedData);
                return JsonSerializer.Deserialize<DatabaseRoot>(json) != null;
            }
            catch
            {
                return false;
            }
        }

        private static string Decrypt(byte[] encryptedData)
        {
            using var aes = Aes.Create();
            aes.Key = BuildFixedBytes("AgencyContractorSecretKey2024_Secure", 32);
            aes.IV = BuildFixedBytes("AgencyContractor", 16);

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(encryptedData);
            using var cryptoStream = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(cryptoStream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] BuildFixedBytes(string value, int length)
        {
            var result = new byte[length];
            var bytes = Encoding.UTF8.GetBytes(value);
            Array.Copy(bytes, result, Math.Min(bytes.Length, result.Length));
            return result;
        }

        private void RegisterRecovery(int count = 1)
        {
            if (count > 0)
                _recoveryCount += count;
        }

        private void RegisterWarning(int count = 1)
        {
            if (count > 0)
                _warningCount += count;
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private void ShowStartupSummaryIfNeeded()
        {
            if (_recoveryCount <= 0 && _warningCount <= 0)
                return;

            var message = _recoveryCount > 0 && _warningCount > 0
                ? Res("MsgStartupHealthRecoveredAndWarnings")
                : _recoveryCount > 0
                    ? Res("MsgStartupHealthRecovered")
                    : Res("MsgStartupHealthWarnings");

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (string.IsNullOrWhiteSpace(message) || Application.Current?.MainWindow?.IsVisible != true)
                    return;

                if (_warningCount > 0)
                    ToastService.Instance.Warning(message);
                else
                    ToastService.Instance.Info(message);
            });
        }

        private static string ComputeHash(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(data));
        }
    }
}

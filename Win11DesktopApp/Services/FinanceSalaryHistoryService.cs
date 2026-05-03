using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceSalaryHistoryService
    {
        private const string SalaryHistoryFile = "salary_history.json";

        private readonly FolderService _folderService;
        private readonly LocalDbService? _localDbService;
        private readonly CompanyService _companyService;
        private readonly Func<string, string?> _resolveEmployeeId;
        private readonly Func<string, string?, string> _resolveEmployeeFolder;
        private bool _useLocalDb;

        public FinanceSalaryHistoryService(
            FolderService folderService,
            LocalDbService? localDbService,
            CompanyService companyService,
            Func<string, string?> resolveEmployeeId,
            Func<string, string?, string> resolveEmployeeFolder)
        {
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _localDbService = localDbService;
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _resolveEmployeeId = resolveEmployeeId;
            _resolveEmployeeFolder = resolveEmployeeFolder;
        }

        public LocalDbMigrationResult EnsureMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var sources = BuildSalaryHistoryMigrationSources().ToList();
                var result = _localDbService.MigrateSalaryHistoryIfNeeded(sources);
                _useLocalDb = result.IsSuccessful;
                return result;
            }
            catch (Exception ex)
            {
                _useLocalDb = false;
                LoggingService.LogError("FinanceSalaryHistoryService.EnsureMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public void SaveSalaryHistoryRecord(string employeeFolder, SalaryHistoryRecord record)
        {
            employeeFolder = _resolveEmployeeFolder(employeeFolder, null);
            if (string.IsNullOrEmpty(employeeFolder) || !Directory.Exists(employeeFolder))
                return;

            try
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                if (_useLocalDb && _localDbService != null)
                {
                    _localDbService.UpsertSalaryHistoryRecord(employeeId, employeeFolder, record);
                    return;
                }

                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);
                records.RemoveAll(r => r.Year == record.Year && r.Month == record.Month && r.FirmName == record.FirmName);
                records.Add(record);
                records = records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToList();
                SafeFileService.WriteJsonAtomic(filePath, records);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.SaveSalaryHistoryRecord", ex);
            }
        }

        public void RemoveSalaryHistoryRecord(string employeeFolder, int year, int month, string firmName)
        {
            employeeFolder = _resolveEmployeeFolder(employeeFolder, null);
            if (string.IsNullOrEmpty(employeeFolder) || !Directory.Exists(employeeFolder))
                return;

            try
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                if (_useLocalDb && _localDbService != null)
                {
                    _localDbService.DeleteSalaryHistoryRecord(employeeId, employeeFolder, year, month, firmName);
                    return;
                }

                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);
                var before = records.Count;
                records.RemoveAll(r => r.Year == year && r.Month == month && r.FirmName == firmName);
                if (records.Count == before)
                    return;

                SafeFileService.WriteJsonAtomic(filePath, records);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.RemoveSalaryHistoryRecord", ex);
            }
        }

        public List<SalaryHistoryRecord> LoadSalaryHistory(string employeeFolder)
        {
            try
            {
                employeeFolder = _resolveEmployeeFolder(employeeFolder, null);
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                if (_useLocalDb && _localDbService != null)
                {
                    var dbRecords = _localDbService.GetSalaryHistory(employeeId, employeeFolder);
                    if (dbRecords.Count > 0 || _localDbService.IsSalaryHistoryMigrationCompleted())
                        return dbRecords;
                }

                return LoadSalaryHistoryFromResolvedFolder(employeeFolder, employeeId);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.LoadSalaryHistory", ex);
                return new List<SalaryHistoryRecord>();
            }
        }

        public List<SalaryHistoryRecord> LoadSalaryHistoryFromResolvedFolder(string employeeFolder, string? employeeId = null)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbRecords = _localDbService.GetSalaryHistory(employeeId ?? string.Empty, employeeFolder);
                if (dbRecords.Count > 0 || _localDbService.IsSalaryHistoryMigrationCompleted())
                    return dbRecords;
            }

            var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
            if (!File.Exists(filePath))
                return new List<SalaryHistoryRecord>();

            return SafeFileService.ReadJsonOrDefault(filePath, new List<SalaryHistoryRecord>());
        }

        public int CleanupMigratedSalaryHistoryBackups()
        {
            if (_localDbService == null)
                return 0;

            try
            {
                return _localDbService.CleanupMigratedSalaryHistoryBackups(BuildSalaryHistoryCleanupSources());
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.CleanupMigratedSalaryHistoryBackups", ex);
                return 0;
            }
        }

        private IEnumerable<SalaryHistoryMigrationSource> BuildSalaryHistoryMigrationSources()
        {
            foreach (var employeeFolder in EnumerateSalaryHistoryEmployeeFolders())
            {
                var historyJsonPath = Path.Combine(employeeFolder, SalaryHistoryFile);
                if (!File.Exists(historyJsonPath) || File.Exists(historyJsonPath + ".migrated"))
                    continue;

                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                IReadOnlyList<SalaryHistoryRecord> records;
                try
                {
                    records = SafeFileService.ReadJsonOrDefault(historyJsonPath, new List<SalaryHistoryRecord>());
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FinanceSalaryHistoryService.BuildSalaryHistoryMigrationSources",
                        $"Skipped salary history migration because salary_history.json could not be read: {employeeFolder}. {ex.Message}");
                    continue;
                }

                yield return new SalaryHistoryMigrationSource
                {
                    EmployeeId = employeeId,
                    EmployeeFolder = employeeFolder,
                    HistoryJsonPath = historyJsonPath,
                    Records = records
                };
            }
        }

        private IEnumerable<SalaryHistoryMigrationSource> BuildSalaryHistoryCleanupSources()
        {
            foreach (var employeeFolder in EnumerateSalaryHistoryEmployeeFolders())
            {
                var historyJsonPath = Path.Combine(employeeFolder, SalaryHistoryFile);
                if (File.Exists(historyJsonPath) || !File.Exists(historyJsonPath + ".migrated"))
                    continue;

                yield return new SalaryHistoryMigrationSource
                {
                    EmployeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty,
                    EmployeeFolder = employeeFolder,
                    HistoryJsonPath = historyJsonPath
                };
            }
        }

        private IEnumerable<string> EnumerateSalaryHistoryEmployeeFolders()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in _companyService.Companies)
            {
                var employeesFolder = _folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrWhiteSpace(employeesFolder) || !Directory.Exists(employeesFolder))
                    continue;

                foreach (var folder in Directory.GetDirectories(employeesFolder))
                {
                    if (seen.Add(folder))
                        yield return folder;
                }
            }

            var archiveFolder = _folderService.GetArchiveFolder();
            if (string.IsNullOrWhiteSpace(archiveFolder) || !Directory.Exists(archiveFolder))
                yield break;

            foreach (var folder in Directory.GetDirectories(archiveFolder))
            {
                if (seen.Add(folder))
                    yield return folder;
            }
        }
    }
}

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
        private readonly IFinanceSalaryHistoryStorage? _salaryHistoryStorage;
        private readonly CompanyService _companyService;
        private readonly Func<string, string?> _resolveEmployeeId;
        private readonly Func<string, string?, string> _resolveEmployeeFolder;
        private bool _useLocalDb;

        public FinanceSalaryHistoryService(
            FolderService folderService,
            IFinanceSalaryHistoryStorage? salaryHistoryStorage,
            CompanyService companyService,
            Func<string, string?> resolveEmployeeId,
            Func<string, string?, string> resolveEmployeeFolder)
        {
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _salaryHistoryStorage = salaryHistoryStorage;
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _resolveEmployeeId = resolveEmployeeId;
            _resolveEmployeeFolder = resolveEmployeeFolder;
            _useLocalDb = _salaryHistoryStorage != null;
        }

        public void SaveSalaryHistoryRecord(string employeeFolder, SalaryHistoryRecord record)
        {
            employeeFolder = _resolveEmployeeFolder(employeeFolder, null);
            if (string.IsNullOrEmpty(employeeFolder) || !Directory.Exists(employeeFolder))
                return;

            try
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                if (_useLocalDb && _salaryHistoryStorage != null)
                {
                    _salaryHistoryStorage.UpsertSalaryHistoryRecord(employeeId, employeeFolder, record);
                    return;
                }

                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);
                var firmKey = NormalizeSalaryHistoryFirmKey(record.FirmName);
                records.RemoveAll(r =>
                    r.Year == record.Year
                    && r.Month == record.Month
                    && NormalizeSalaryHistoryFirmKey(r.FirmName) == firmKey);
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
                if (_useLocalDb && _salaryHistoryStorage != null)
                {
                    _salaryHistoryStorage.DeleteSalaryHistoryRecord(employeeId, employeeFolder, year, month, firmName);
                    return;
                }

                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);
                var before = records.Count;
                var firmKey = NormalizeSalaryHistoryFirmKey(firmName);
                records.RemoveAll(r =>
                    r.Year == year
                    && r.Month == month
                    && NormalizeSalaryHistoryFirmKey(r.FirmName) == firmKey);
                if (records.Count == before)
                    return;

                SafeFileService.WriteJsonAtomic(filePath, records);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.RemoveSalaryHistoryRecord", ex);
            }
        }

        public int DeleteSalaryHistoryForEmployee(string? employeeId, string? originalFolder, string? deletedFolder)
        {
            if (_salaryHistoryStorage == null)
                return 0;

            try
            {
                return _salaryHistoryStorage.DeleteSalaryHistoryForEmployee(employeeId, originalFolder, deletedFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.DeleteSalaryHistoryForEmployee", ex);
                return 0;
            }
        }

        public int RemapEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            if (_salaryHistoryStorage == null || string.IsNullOrWhiteSpace(toFolder))
                return 0;

            try
            {
                return _salaryHistoryStorage.RemapEmployeeFolder(employeeId, fromFolderA, fromFolderB, toFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.RemapEmployeeFolder", ex);
                return 0;
            }
        }

        public List<SalaryHistoryRecord> LoadSalaryHistory(string employeeFolder)
        {
            try
            {
                employeeFolder = _resolveEmployeeFolder(employeeFolder, null);
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                if (_useLocalDb && _salaryHistoryStorage != null)
                {
                    var dbRecords = _salaryHistoryStorage.GetSalaryHistory(employeeId, employeeFolder);
                    return DeduplicateSalaryHistoryRecords(dbRecords);
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
            if (_useLocalDb && _salaryHistoryStorage != null)
            {
                var dbRecords = _salaryHistoryStorage.GetSalaryHistory(employeeId ?? string.Empty, employeeFolder);
                return DeduplicateSalaryHistoryRecords(dbRecords);
            }

            var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
            if (!File.Exists(filePath))
                return new List<SalaryHistoryRecord>();

            return DeduplicateSalaryHistoryRecords(SafeFileService.ReadJsonOrDefault(filePath, new List<SalaryHistoryRecord>()));
        }

        public int RemoveDuplicateSalaryHistoryRecordsAtStartup()
        {
            if (_salaryHistoryStorage == null)
                return 0;

            try
            {
                return _salaryHistoryStorage.RemoveDuplicateSalaryHistoryRecords();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceSalaryHistoryService.RemoveDuplicateSalaryHistoryRecordsAtStartup", ex);
                return 0;
            }
        }

        private static List<SalaryHistoryRecord> DeduplicateSalaryHistoryRecords(IEnumerable<SalaryHistoryRecord> records)
        {
            var source = records
                .Where(record => record != null)
                .ToList();

            var deduped = SalaryHistoryDuplicateCleanup.DeduplicateRecords(source);

            if (deduped.Count != source.Count)
            {
                LoggingService.LogWarning(
                    "FinanceSalaryHistoryService.DeduplicateSalaryHistoryRecords",
                    $"Hidden duplicate salary history rows. Original={source.Count}, Deduped={deduped.Count}.");
            }

            return deduped;
        }

        private static string NormalizeSalaryHistoryFirmKey(string? firmName)
            => SalaryHistoryDuplicateCleanup.NormalizeSalaryHistoryFirmKey(firmName);
    }
}

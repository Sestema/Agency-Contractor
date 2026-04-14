using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceReportsService
    {
        private readonly LocalDbService? _localDbService;
        private readonly IList<MonthlySalaryReport> _reports;
        private bool _useLocalDb;

        public FinanceReportsService(LocalDbService? localDbService, IList<MonthlySalaryReport> reports)
        {
            _localDbService = localDbService;
            _reports = reports;
        }

        public bool UseLocalDb => _useLocalDb;

        public LocalDbMigrationResult EnsureMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var result = _localDbService.MigrateReportsIfNeeded(_reports.ToList());
                _useLocalDb = result.IsSuccessful;

                if (!result.WasMigrationAttempted && _localDbService.IsReportsMigrationCompleted())
                    _useLocalDb = true;

                return result;
            }
            catch (Exception ex)
            {
                _useLocalDb = false;
                LoggingService.LogError("FinanceReportsService.EnsureMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public MonthlySalaryReport? GetReport(string companyId, int year, int month)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbReport = _localDbService.GetSalaryReport(companyId, year, month);
                if (dbReport != null || _localDbService.IsReportsMigrationCompleted())
                    return dbReport;
            }

            return _reports.FirstOrDefault(r => r.CompanyId == companyId && r.Year == year && r.Month == month);
        }

        public MonthlySalaryReport? GetGlobalReport(int year, int month)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbReport = _localDbService.GetSalaryReport(FinanceConstants.GlobalKey, year, month);
                if (dbReport != null || _localDbService.IsReportsMigrationCompleted())
                    return dbReport;
            }

            return _reports.FirstOrDefault(r => r.CompanyId == FinanceConstants.GlobalKey && r.Year == year && r.Month == month);
        }

        public MonthlySalaryReport GetOrCreateReport(string companyId, string companyName, int year, int month)
        {
            var report = GetReport(companyId, year, month);
            if (report != null)
                return report;

            report = new MonthlySalaryReport
            {
                CompanyId = companyId,
                CompanyName = companyName,
                Year = year,
                Month = month
            };

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.UpsertSalaryReport(report);
                return _localDbService.GetSalaryReport(companyId, year, month) ?? report;
            }

            _reports.Add(report);
            return report;
        }

        public MonthlySalaryReport GetOrCreateGlobalReport(int year, int month)
        {
            var report = GetGlobalReport(year, month);
            if (report != null)
                return report;

            report = new MonthlySalaryReport
            {
                CompanyId = FinanceConstants.GlobalKey,
                CompanyName = "All",
                Year = year,
                Month = month
            };

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.UpsertSalaryReport(report);
                return _localDbService.GetSalaryReport(FinanceConstants.GlobalKey, year, month) ?? report;
            }

            _reports.Add(report);
            return report;
        }

        public void SaveReport(MonthlySalaryReport report)
        {
            report.UpdatedAt = DateTime.Now;

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.UpsertSalaryReport(report);
                return;
            }

            for (int i = 0; i < _reports.Count; i++)
            {
                if (_reports[i].Id != report.Id)
                    continue;

                _reports[i] = report;
                return;
            }

            _reports.Add(report);
        }

        public List<MonthlySalaryReport> GetReportsForCompany(string companyId)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbReports = _localDbService.GetSalaryReportsForCompany(companyId);
                if (dbReports.Count > 0 || _localDbService.IsReportsMigrationCompleted())
                    return dbReports;
            }

            return _reports
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .ToList();
        }

        public List<string> GetAvailableMonths(string companyId)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var dbMonths = _localDbService.GetAvailableReportMonths(companyId);
                if (dbMonths.Count > 0 || _localDbService.IsReportsMigrationCompleted())
                    return dbMonths;
            }

            return _reports
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Year)
                .ThenByDescending(r => r.Month)
                .Select(r => r.MonthKey)
                .Distinct()
                .ToList();
        }

        public bool RemoveCustomFieldReferences(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return false;

            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.RemoveCustomFieldReferencesFromReports(fieldId);
                return false;
            }

            var changed = false;
            foreach (var report in _reports)
            {
                var reportChanged = false;
                foreach (var entry in report.Entries)
                {
                    if (entry.CustomValues.Remove(fieldId))
                        reportChanged = true;
                }

                if (!reportChanged)
                    continue;

                report.UpdatedAt = DateTime.Now;
                changed = true;
            }

            return changed;
        }

        public bool RemoveEmployeeEntries(string? employeeId, string originalFolder, string deletedFolder)
        {
            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.RemoveEmployeeEntriesFromReports(employeeId ?? string.Empty, originalFolder, deletedFolder);
                return false;
            }

            bool Matches(SalaryEntry entry)
            {
                if (!string.IsNullOrWhiteSpace(employeeId)
                    && string.Equals(entry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return (!string.IsNullOrWhiteSpace(originalFolder) && string.Equals(entry.EmployeeFolder, originalFolder, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(deletedFolder) && string.Equals(entry.EmployeeFolder, deletedFolder, StringComparison.OrdinalIgnoreCase));
            }

            var changed = false;
            foreach (var report in _reports)
            {
                var removed = report.Entries.RemoveAll(Matches);
                if (removed <= 0)
                    continue;

                report.UpdatedAt = DateTime.Now;
                changed = true;
            }

            return changed;
        }
    }
}

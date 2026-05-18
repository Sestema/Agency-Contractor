using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceReportsService
    {
        private readonly IFinanceReportsStorage? _reportsStorage;

        public FinanceReportsService(IFinanceReportsStorage? reportsStorage)
        {
            _reportsStorage = reportsStorage;
        }

        public MonthlySalaryReport? GetReport(string companyId, int year, int month)
        {
            return RequireLocalDb().GetSalaryReport(companyId, year, month);
        }

        public MonthlySalaryReport? GetGlobalReport(int year, int month)
        {
            return RequireLocalDb().GetSalaryReport(FinanceConstants.GlobalKey, year, month);
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

            var localDb = RequireLocalDb();
            localDb.UpsertSalaryReport(report);
            return localDb.GetSalaryReport(companyId, year, month) ?? report;
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

            var localDb = RequireLocalDb();
            localDb.UpsertSalaryReport(report);
            return localDb.GetSalaryReport(FinanceConstants.GlobalKey, year, month) ?? report;
        }

        public void SaveReport(MonthlySalaryReport report)
        {
            report.UpdatedAt = DateTime.Now;

            RequireLocalDb().UpsertSalaryReport(report);
        }

        public List<MonthlySalaryReport> GetReportsForCompany(string companyId)
        {
            return RequireLocalDb().GetSalaryReportsForCompany(companyId);
        }

        public List<string> GetAvailableMonths(string companyId)
        {
            return RequireLocalDb().GetAvailableReportMonths(companyId);
        }

        public bool RemoveCustomFieldReferences(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return false;

            RequireLocalDb().RemoveCustomFieldReferencesFromReports(fieldId);
            return false;
        }

        public bool RemoveEmployeeEntries(string? employeeId, string originalFolder, string deletedFolder)
        {
            RequireLocalDb().RemoveEmployeeEntriesFromReports(employeeId ?? string.Empty, originalFolder, deletedFolder);
            return false;
        }

        private IFinanceReportsStorage RequireLocalDb()
        {
            if (_reportsStorage == null)
                throw new InvalidOperationException("Reports storage is required.");

            return _reportsStorage;
        }
    }
}

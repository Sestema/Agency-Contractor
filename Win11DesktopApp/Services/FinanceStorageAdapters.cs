using System;
using System.Collections.Generic;
using System.Threading;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public interface IFinanceMonthPaymentsStorage
    {
        bool MonthDbExists(int year, int month);
        IEnumerable<(int year, int month, string path)> EnumerateMonthDatabases();
        (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadMonthPayments(int year, int month);
        void SaveMonthPayments(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses);
        void UpsertSalaryEntries(int year, int month, IReadOnlyList<SalaryEntry> entries);
        void ReplaceFirmPaymentsForFirm(int year, int month, string firmName, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses);
        void UpsertFirmExpense(int year, int month, FirmExpense expense);
        bool DeleteFirmExpense(int year, int month, string expenseId);
        void ReplaceFirmExpensesForFirm(int year, int month, string firmName, IReadOnlyList<FirmExpense> expenses);
        void UpdateHourlyRateForward(string? employeeId, string employeeFolder, string firmName, decimal newRate, string fromMonthKey, CancellationToken cancellationToken = default);
        Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForAllRequests(
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests);
        Dictionary<string, (decimal netSalary, bool paid)> GetSavedPaymentsForEmployee(
            string employeeFolder,
            string? employeeId,
            string firmName,
            string beforeMonthKey);
    }

    public interface IFinanceAdvancesStorage
    {
        void InsertAdvance(string employeeId, string employeeFolder, AdvancePayment advance);
        void DeleteAdvance(string advanceId);
        void DeleteAdvancesForEmployee(string employeeId, string originalFolder, string deletedFolder);
        List<AdvancePayment> GetAdvances(string companyId, string monthKey);
        decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string companyId, string monthKey);
        decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string monthKey);
        List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeId, string employeeFolder, string monthKey);
        List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeId, string employeeFolder, string firmName, string companyId, string monthKey);
        Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests, string monthKey, string companyId);
        List<AdvancePayment> GetAllAdvancesForEmployee(string employeeId, string employeeFolder);
    }

    public interface IFinanceCustomFieldsStorage
    {
        List<CustomSalaryField> GetCustomSalaryFields();
        void UpsertCustomSalaryField(CustomSalaryField field);
        void ReplaceCustomSalaryFields(IReadOnlyList<CustomSalaryField> fields);
        void DeleteCustomSalaryField(string fieldId);
    }

    public interface IFinanceReportsStorage
    {
        MonthlySalaryReport? GetSalaryReport(string companyId, int year, int month);
        void UpsertSalaryReport(MonthlySalaryReport report);
        List<MonthlySalaryReport> GetSalaryReportsForCompany(string companyId);
        List<string> GetAvailableReportMonths(string companyId);
        void RemoveCustomFieldReferencesFromReports(string fieldId);
        void RemoveEmployeeEntriesFromReports(string employeeId, string originalFolder, string deletedFolder);
    }

    public interface IFinanceSalaryHistoryStorage
    {
        LocalDbMigrationResult MigrateSalaryHistoryIfNeeded(IEnumerable<SalaryHistoryMigrationSource> sources);
        void UpsertSalaryHistoryRecord(string employeeId, string employeeFolder, SalaryHistoryRecord record);
        void DeleteSalaryHistoryRecord(string employeeId, string employeeFolder, int year, int month, string firmName);
        List<SalaryHistoryRecord> GetSalaryHistory(string employeeId, string employeeFolder);
        bool IsSalaryHistoryMigrationCompleted();
        int CleanupMigratedSalaryHistoryBackups(IEnumerable<SalaryHistoryMigrationSource> sources);
    }

    public interface IActivityLogStorage
    {
        LocalDbMigrationResult MigrateActivityLogIfNeeded(string jsonPath, IReadOnlyList<ActivityLogEntry> sourceEntries);
        void InsertActivityLog(ActivityLogEntry entry);
        List<ActivityLogEntry> GetAllActivityLogs();
        void RemoveActivityLogEntries(string originalFolder, string deletedFolder, string employeeName, string firmName);
        void ClearActivityLogs();
    }

    public interface IArchiveLogStorage
    {
        LocalDbMigrationResult MigrateArchiveLogIfNeeded(string jsonPath, IReadOnlyList<ArchiveLogEntry> sourceEntries);
        List<ArchiveLogEntry> GetAllArchiveLogs();
        ArchiveLogEntry? GetActiveArchiveLogEntry(string operationId);
        void InsertArchiveLog(ArchiveLogEntry entry);
        bool MarkArchiveLogReverted(string operationId, string undoOperationId);
    }

    public interface IEmployeeHistoryStorage
    {
        LocalDbMigrationResult MigrateEmployeeHistoryIfNeeded(IEnumerable<EmployeeHistoryMigrationSource> sources);
        void InsertEmployeeHistory(string employeeId, string employeeFolder, string firmName, EmployeeHistoryEntry entry);
        List<EmployeeHistoryEntry> GetEmployeeHistory(string employeeId);
        void DeleteEmployeeHistoryEntry(string employeeId, long historyEntryId);
        void DeleteEmployeeHistory(string employeeId);
        int CleanupMigratedEmployeeHistoryBackups(IEnumerable<EmployeeHistoryMigrationSource> sources);
    }

    public sealed class SqliteFinanceMonthPaymentsStorage : IFinanceMonthPaymentsStorage
    {
        private readonly SalaryDbService _salaryDbService;

        public SqliteFinanceMonthPaymentsStorage(SalaryDbService salaryDbService)
        {
            _salaryDbService = salaryDbService ?? throw new ArgumentNullException(nameof(salaryDbService));
        }

        public bool MonthDbExists(int year, int month)
            => _salaryDbService.MonthDbExists(year, month);

        public IEnumerable<(int year, int month, string path)> EnumerateMonthDatabases()
            => _salaryDbService.EnumerateMonthDatabases();

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadMonthPayments(int year, int month)
            => _salaryDbService.LoadMonthPayments(year, month);

        public void SaveMonthPayments(int year, int month, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
            => _salaryDbService.SaveMonthPayments(year, month, entries, expenses);

        public void UpsertSalaryEntries(int year, int month, IReadOnlyList<SalaryEntry> entries)
            => _salaryDbService.SaveMonthPayments(year, month, entries, Array.Empty<FirmExpense>());

        public void ReplaceFirmPaymentsForFirm(int year, int month, string firmName, IReadOnlyList<SalaryEntry> entries, IReadOnlyList<FirmExpense> expenses)
        {
            var existing = _salaryDbService.LoadMonthPayments(year, month);
            var mergedEntries = existing.entries
                .Where(entry => !string.Equals(entry.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                .Concat(entries)
                .ToList();
            var mergedExpenses = existing.expenses
                .Where(expense => !string.Equals(expense.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                .Concat(expenses)
                .ToList();
            _salaryDbService.SaveMonthPayments(year, month, mergedEntries, mergedExpenses);
        }

        public void UpsertFirmExpense(int year, int month, FirmExpense expense)
            => _salaryDbService.UpsertFirmExpense(year, month, expense);

        public bool DeleteFirmExpense(int year, int month, string expenseId)
            => _salaryDbService.DeleteFirmExpense(year, month, expenseId);

        public void ReplaceFirmExpensesForFirm(int year, int month, string firmName, IReadOnlyList<FirmExpense> expenses)
            => _salaryDbService.ReplaceFirmExpensesForFirm(year, month, firmName, expenses);

        public void UpdateHourlyRateForward(string? employeeId, string employeeFolder, string firmName, decimal newRate, string fromMonthKey, CancellationToken cancellationToken = default)
            => _salaryDbService.UpdateHourlyRateForward(employeeId, employeeFolder, firmName, newRate, fromMonthKey, cancellationToken);

        public Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForAllRequests(
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests)
            => _salaryDbService.GetSavedPaymentsForAllRequests(beforeMonthKey, requests);

        public Dictionary<string, (decimal netSalary, bool paid)> GetSavedPaymentsForEmployee(
            string employeeFolder,
            string? employeeId,
            string firmName,
            string beforeMonthKey)
            => _salaryDbService.GetSavedPaymentsForEmployee(employeeFolder, employeeId, firmName, beforeMonthKey);
    }

    public sealed class SqliteFinanceAdvancesStorage : IFinanceAdvancesStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteFinanceAdvancesStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public void InsertAdvance(string employeeId, string employeeFolder, AdvancePayment advance)
            => _localDbService.InsertAdvance(employeeId, employeeFolder, advance);

        public void DeleteAdvance(string advanceId)
            => _localDbService.DeleteAdvance(advanceId);

        public void DeleteAdvancesForEmployee(string employeeId, string originalFolder, string deletedFolder)
            => _localDbService.DeleteAdvancesForEmployee(employeeId, originalFolder, deletedFolder);

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
            => _localDbService.GetAdvances(companyId, monthKey);

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string companyId, string monthKey)
            => _localDbService.GetTotalAdvancesForEmployee(employeeId, employeeFolder, companyId, monthKey);

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string monthKey)
            => _localDbService.GetTotalAdvancesForEmployee(employeeId, employeeFolder, monthKey);

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeId, string employeeFolder, string monthKey)
            => _localDbService.GetAdvancesForEmployeeMonth(employeeId, employeeFolder, monthKey);

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeId, string employeeFolder, string firmName, string companyId, string monthKey)
            => _localDbService.GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, companyId, monthKey);

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests, string monthKey, string companyId)
            => _localDbService.GetTotalAdvancesForEmployeeFirms(requests, monthKey, companyId);

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeId, string employeeFolder)
            => _localDbService.GetAllAdvancesForEmployee(employeeId, employeeFolder);
    }

    public sealed class SqliteFinanceCustomFieldsStorage : IFinanceCustomFieldsStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteFinanceCustomFieldsStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public List<CustomSalaryField> GetCustomSalaryFields()
            => _localDbService.GetCustomSalaryFields();

        public void UpsertCustomSalaryField(CustomSalaryField field)
            => _localDbService.UpsertCustomSalaryField(field);

        public void ReplaceCustomSalaryFields(IReadOnlyList<CustomSalaryField> fields)
            => _localDbService.ReplaceCustomSalaryFields(fields);

        public void DeleteCustomSalaryField(string fieldId)
            => _localDbService.DeleteCustomSalaryField(fieldId);
    }

    public sealed class SqliteFinanceReportsStorage : IFinanceReportsStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteFinanceReportsStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public MonthlySalaryReport? GetSalaryReport(string companyId, int year, int month)
            => _localDbService.GetSalaryReport(companyId, year, month);

        public void UpsertSalaryReport(MonthlySalaryReport report)
            => _localDbService.UpsertSalaryReport(report);

        public List<MonthlySalaryReport> GetSalaryReportsForCompany(string companyId)
            => _localDbService.GetSalaryReportsForCompany(companyId);

        public List<string> GetAvailableReportMonths(string companyId)
            => _localDbService.GetAvailableReportMonths(companyId);

        public void RemoveCustomFieldReferencesFromReports(string fieldId)
            => _localDbService.RemoveCustomFieldReferencesFromReports(fieldId);

        public void RemoveEmployeeEntriesFromReports(string employeeId, string originalFolder, string deletedFolder)
            => _localDbService.RemoveEmployeeEntriesFromReports(employeeId, originalFolder, deletedFolder);
    }

    public sealed class SqliteFinanceSalaryHistoryStorage : IFinanceSalaryHistoryStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteFinanceSalaryHistoryStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public LocalDbMigrationResult MigrateSalaryHistoryIfNeeded(IEnumerable<SalaryHistoryMigrationSource> sources)
            => _localDbService.MigrateSalaryHistoryIfNeeded(sources);

        public void UpsertSalaryHistoryRecord(string employeeId, string employeeFolder, SalaryHistoryRecord record)
            => _localDbService.UpsertSalaryHistoryRecord(employeeId, employeeFolder, record);

        public void DeleteSalaryHistoryRecord(string employeeId, string employeeFolder, int year, int month, string firmName)
            => _localDbService.DeleteSalaryHistoryRecord(employeeId, employeeFolder, year, month, firmName);

        public List<SalaryHistoryRecord> GetSalaryHistory(string employeeId, string employeeFolder)
            => _localDbService.GetSalaryHistory(employeeId, employeeFolder);

        public bool IsSalaryHistoryMigrationCompleted()
            => _localDbService.IsSalaryHistoryMigrationCompleted();

        public int CleanupMigratedSalaryHistoryBackups(IEnumerable<SalaryHistoryMigrationSource> sources)
            => _localDbService.CleanupMigratedSalaryHistoryBackups(sources);
    }

    public sealed class SqliteActivityLogStorage : IActivityLogStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteActivityLogStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public LocalDbMigrationResult MigrateActivityLogIfNeeded(string jsonPath, IReadOnlyList<ActivityLogEntry> sourceEntries)
            => _localDbService.MigrateActivityLogIfNeeded(jsonPath, sourceEntries);

        public void InsertActivityLog(ActivityLogEntry entry)
            => _localDbService.InsertActivityLog(entry);

        public List<ActivityLogEntry> GetAllActivityLogs()
            => _localDbService.GetAllActivityLogs();

        public void RemoveActivityLogEntries(string originalFolder, string deletedFolder, string employeeName, string firmName)
            => _localDbService.RemoveActivityLogEntries(originalFolder, deletedFolder, employeeName, firmName);

        public void ClearActivityLogs()
            => _localDbService.ClearActivityLogs();
    }

    public sealed class SqliteArchiveLogStorage : IArchiveLogStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteArchiveLogStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public LocalDbMigrationResult MigrateArchiveLogIfNeeded(string jsonPath, IReadOnlyList<ArchiveLogEntry> sourceEntries)
            => _localDbService.MigrateArchiveLogIfNeeded(jsonPath, sourceEntries);

        public List<ArchiveLogEntry> GetAllArchiveLogs()
            => _localDbService.GetAllArchiveLogs();

        public ArchiveLogEntry? GetActiveArchiveLogEntry(string operationId)
            => _localDbService.GetActiveArchiveLogEntry(operationId);

        public void InsertArchiveLog(ArchiveLogEntry entry)
            => _localDbService.InsertArchiveLog(entry);

        public bool MarkArchiveLogReverted(string operationId, string undoOperationId)
            => _localDbService.MarkArchiveLogReverted(operationId, undoOperationId);
    }

    public sealed class SqliteEmployeeHistoryStorage : IEmployeeHistoryStorage
    {
        private readonly LocalDbService _localDbService;

        public SqliteEmployeeHistoryStorage(LocalDbService localDbService)
        {
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
        }

        public LocalDbMigrationResult MigrateEmployeeHistoryIfNeeded(IEnumerable<EmployeeHistoryMigrationSource> sources)
            => _localDbService.MigrateEmployeeHistoryIfNeeded(sources);

        public void InsertEmployeeHistory(string employeeId, string employeeFolder, string firmName, EmployeeHistoryEntry entry)
            => _localDbService.InsertEmployeeHistory(employeeId, employeeFolder, firmName, entry);

        public List<EmployeeHistoryEntry> GetEmployeeHistory(string employeeId)
            => _localDbService.GetEmployeeHistory(employeeId);

        public void DeleteEmployeeHistoryEntry(string employeeId, long historyEntryId)
            => _localDbService.DeleteEmployeeHistoryEntry(employeeId, historyEntryId);

        public void DeleteEmployeeHistory(string employeeId)
            => _localDbService.DeleteEmployeeHistory(employeeId);

        public int CleanupMigratedEmployeeHistoryBackups(IEnumerable<EmployeeHistoryMigrationSource> sources)
            => _localDbService.CleanupMigratedEmployeeHistoryBackups(sources);
    }
}

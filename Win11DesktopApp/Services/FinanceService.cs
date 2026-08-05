using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using Win11DesktopApp.Models;
using System.Globalization;

namespace Win11DesktopApp.Services
{
    public class FinanceService
    {
        private readonly bool _suppressStartupNotifications;
        private readonly FolderService _folderService;
        private readonly SalaryDbService? _salaryDbService;
        private readonly LocalDbService? _localDbService;
        private readonly CompanyService _companyService;
        private readonly EmployeeIndexDbService? _employeeIndexDbService;
        private readonly bool _isPostgresRuntimeStorage;
        public const string GlobalKey = FinanceConstants.GlobalKey;
        public const string AllFirmsKey = FinanceConstants.AllFirmsKey;
        public FinanceAdvancesService AdvancesService { get; private set; } = null!;
        public FinanceSalaryHistoryService SalaryHistoryService { get; private set; } = null!;
        public FinanceMonthPaymentsService MonthPaymentsService { get; private set; } = null!;
        public FinanceCustomFieldsService CustomFieldsService { get; private set; } = null!;
        public FinanceReportsService ReportsService { get; private set; } = null!;
        public bool WasRecoveredFromBackupOnLoad { get; private set; }
        public bool WasResetToDefaultsOnLoad { get; private set; }
        public string LastSalaryConflictMessage { get; private set; } = string.Empty;
        public string? LastSaveRecoveryPath { get; private set; }

        public FinanceService(
            FolderService folderService,
            SalaryDbService? salaryDbService = null,
            LocalDbService? localDbService = null,
            CompanyService? companyService = null,
            EmployeeIndexDbService? employeeIndexDbService = null,
            SharedOperationLockService? sharedOperationLockService = null,
            bool suppressStartupNotifications = false,
            AppDataStorageFactory? storageFactory = null,
            FirmFinanceRenameService? firmFinanceRenameService = null)
        {
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _suppressStartupNotifications = suppressStartupNotifications;
            _salaryDbService = salaryDbService;
            _localDbService = localDbService;
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _employeeIndexDbService = employeeIndexDbService;
            _isPostgresRuntimeStorage = storageFactory?.IsPostgresExplicitlyEnabled == true;
            var advancesStorage = storageFactory?.CreateAdvancesStorage()
                ?? (_localDbService == null ? null : new SqliteFinanceAdvancesStorage(_localDbService));
            var customFieldsStorage = storageFactory?.CreateCustomFieldsStorage()
                ?? (_localDbService == null ? null : new SqliteFinanceCustomFieldsStorage(_localDbService));
            var reportsStorage = storageFactory?.CreateReportsStorage()
                ?? (_localDbService == null ? null : new SqliteFinanceReportsStorage(_localDbService));
            var salaryHistoryStorage = storageFactory?.CreateSalaryHistoryStorage()
                ?? (_localDbService == null ? null : new SqliteFinanceSalaryHistoryStorage(_localDbService));
            var monthPaymentsStorage = storageFactory?.CreateMonthPaymentsStorage()
                ?? (_salaryDbService == null ? null : new SqliteFinanceMonthPaymentsStorage(_salaryDbService));
            AdvancesService = new FinanceAdvancesService(
                advancesStorage,
                ResolveEmployeeId,
                ResolveEmployeeFolder);
            SalaryHistoryService = new FinanceSalaryHistoryService(
                _folderService,
                salaryHistoryStorage,
                _companyService,
                ResolveEmployeeId,
                ResolveEmployeeFolder);
            MonthPaymentsService = new FinanceMonthPaymentsService(
                _folderService,
                monthPaymentsStorage,
                () => LastSaveRecoveryPath = null,
                () =>
                {
                    LastSalaryConflictMessage = string.Empty;
                    LastSaveRecoveryPath = null;
                },
                message => LastSalaryConflictMessage = message,
                sharedOperationLockService);
            CustomFieldsService = new FinanceCustomFieldsService(customFieldsStorage);
            ReportsService = new FinanceReportsService(reportsStorage);
            if (firmFinanceRenameService != null)
                firmFinanceRenameService.FirmRenamed += () => MonthPaymentsService.InvalidatePaymentsCache();
        }

        private static T? ReadJson<T>(string path)
        {
            // Salary files can be read while a save is in flight; allow shared read access
            // so the app is less likely to block its own replace/copy path.
            return SafeFileService.ReadJsonShared<T>(path);
        }

        private static void WriteJsonAtomic<T>(string path, T value)
        {
            SafeFileService.WriteJsonAtomic(path, value);
        }

        #region Facade Delegations

        public List<CustomSalaryField> GetCustomFields()
            => CustomFieldsService.GetCustomFields();

        public List<CustomSalaryField> GetFieldsForFirm(string firmName)
            => CustomFieldsService.GetFieldsForFirm(firmName);

        public List<CustomSalaryField> GetActiveFields(IEnumerable<string> visibleFirms)
            => CustomFieldsService.GetActiveFields(visibleFirms);

        public void AddCustomField(CustomSalaryField field)
            => CustomFieldsService.AddCustomField(field);

        public void UpdateCustomField(CustomSalaryField updated)
            => CustomFieldsService.UpdateCustomField(updated);

        public void RemoveCustomField(string fieldId)
        {
            CustomFieldsService.RemoveCustomField(fieldId);
            ReportsService.RemoveCustomFieldReferences(fieldId);
        }

        public void ReorderCustomFields(List<CustomSalaryField> orderedFields)
            => CustomFieldsService.ReorderCustomFields(orderedFields);

        public MonthlySalaryReport? GetReport(string companyId, int year, int month)
            => ReportsService.GetReport(companyId, year, month);

        public MonthlySalaryReport? GetGlobalReport(int year, int month)
            => ReportsService.GetGlobalReport(year, month);

        public MonthlySalaryReport GetOrCreateReport(string companyId, string companyName, int year, int month)
            => ReportsService.GetOrCreateReport(companyId, companyName, year, month);

        public MonthlySalaryReport GetOrCreateGlobalReport(int year, int month)
            => ReportsService.GetOrCreateGlobalReport(year, month);

        public void SaveReport(MonthlySalaryReport report)
            => ReportsService.SaveReport(report);

        public List<MonthlySalaryReport> GetReportsForCompany(string companyId)
            => ReportsService.GetReportsForCompany(companyId);

        public List<string> GetAvailableMonths(string companyId)
            => ReportsService.GetAvailableMonths(companyId);

        public void AddAdvance(AdvancePayment advance)
            => AdvancesService.AddAdvance(advance);

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
            => AdvancesService.GetAdvances(companyId, monthKey);

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string companyId, string monthKey)
            => AdvancesService.GetTotalAdvancesForEmployee(employeeFolder, companyId, monthKey);

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string monthKey)
            => AdvancesService.GetTotalAdvancesForEmployee(employeeFolder, monthKey);

        public void RemoveAdvance(string advanceId)
            => AdvancesService.RemoveAdvance(advanceId);

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeFolder, string monthKey)
            => AdvancesService.GetAdvancesForEmployeeMonth(employeeFolder, monthKey);

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeFolder, string firmName, string monthKey)
            => AdvancesService.GetAdvancesForEmployeeFirmMonth(employeeFolder, firmName, monthKey);

        public decimal GetTotalAdvancesForEmployeeFirm(string employeeFolder, string firmName, string monthKey)
            => AdvancesService.GetTotalAdvancesForEmployeeFirm(employeeFolder, firmName, monthKey);

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey)
            => AdvancesService.GetTotalAdvancesForEmployeeFirms(requests, monthKey);

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeFolder)
            => AdvancesService.GetAllAdvancesForEmployee(employeeFolder);

        public List<FirmExpense> GetFirmExpenses(int year, int month)
            => MonthPaymentsService.GetFirmExpenses(year, month);

        public List<FirmExpense> GetFirmExpenses(int year, int month, string firmName)
            => MonthPaymentsService.GetFirmExpenses(year, month, firmName);

        public List<FirmExpense> GetFirmExpensesForFirms(int year, int month, IEnumerable<string> firmNames)
            => MonthPaymentsService.GetFirmExpensesForFirms(year, month, firmNames);

        public void AddFirmExpense(FirmExpense expense)
            => MonthPaymentsService.AddFirmExpense(expense);

        public Task AddFirmExpenseAsync(FirmExpense expense, CancellationToken cancellationToken = default)
            => MonthPaymentsService.AddFirmExpenseAsync(expense, cancellationToken);

        public void UpdateFirmExpense(FirmExpense updated)
            => MonthPaymentsService.UpdateFirmExpense(updated);

        public Task UpdateFirmExpenseAsync(FirmExpense updated, CancellationToken cancellationToken = default)
            => MonthPaymentsService.UpdateFirmExpenseAsync(updated, cancellationToken);

        public void RemoveFirmExpense(string expenseId)
            => MonthPaymentsService.RemoveFirmExpense(expenseId);

        public void RemoveFirmExpense(string expenseId, int year, int month)
            => MonthPaymentsService.RemoveFirmExpense(expenseId, year, month);

        public Task RemoveFirmExpenseAsync(string expenseId, int year, int month, CancellationToken cancellationToken = default)
            => MonthPaymentsService.RemoveFirmExpenseAsync(expenseId, year, month, cancellationToken);

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month, string? firmNameFilter = null)
            => MonthPaymentsService.SaveFirmExpenses(expenses, year, month, firmNameFilter);

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
            => MonthPaymentsService.SaveAllFirmPayments(year, month, allEntries, allExpenses);

        public Task<bool> SaveAllFirmPaymentsAsync(
            int year,
            int month,
            List<SalaryEntry> allEntries,
            List<FirmExpense> allExpenses,
            CancellationToken cancellationToken = default)
            => MonthPaymentsService.SaveAllFirmPaymentsAsync(year, month, allEntries, allExpenses, cancellationToken);

        public bool UpsertSalaryEntries(int year, int month, List<SalaryEntry> entries)
            => MonthPaymentsService.UpsertSalaryEntries(year, month, entries);

        public Task<bool> UpsertSalaryEntriesAsync(int year, int month, List<SalaryEntry> entries, CancellationToken cancellationToken = default)
            => MonthPaymentsService.UpsertSalaryEntriesAsync(year, month, entries, cancellationToken);

        public bool SaveFirmPayments(int year, int month, string firmName, List<SalaryEntry> entries, List<FirmExpense> expenses)
            => MonthPaymentsService.SaveFirmPayments(year, month, firmName, entries, expenses);

        public Task<bool> SaveFirmPaymentsAsync(
            int year,
            int month,
            string firmName,
            List<SalaryEntry> entries,
            List<FirmExpense> expenses,
            CancellationToken cancellationToken = default)
            => MonthPaymentsService.SaveFirmPaymentsAsync(year, month, firmName, entries, expenses, cancellationToken);

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month, bool forceReload = false)
            => MonthPaymentsService.LoadAllFirmPayments(year, month, forceReload);

        public (bool success, List<SalaryEntry> entries, List<FirmExpense> expenses, string errorMessage) TryLoadAllFirmPayments(int year, int month, bool forceReload = false)
            => MonthPaymentsService.TryLoadAllFirmPayments(year, month, forceReload);

        public void InvalidatePaymentsCache(int? year = null, int? month = null)
            => MonthPaymentsService.InvalidatePaymentsCache(year, month);

        public bool MonthDataExists(int year, int month)
        {
            return MonthPaymentsService.MonthDataExists(year, month);
        }

        public IReadOnlyList<(int year, int month)> GetAvailableSalaryMonths()
        {
            return MonthPaymentsService.GetAvailableMonths();
        }

        public void SaveSalaryHistoryRecord(string employeeFolder, SalaryHistoryRecord record)
            => SalaryHistoryService.SaveSalaryHistoryRecord(employeeFolder, record);

        public void RemoveSalaryHistoryRecord(string employeeFolder, int year, int month, string firmName)
            => SalaryHistoryService.RemoveSalaryHistoryRecord(employeeFolder, year, month, firmName);

        public List<SalaryHistoryRecord> LoadSalaryHistory(string employeeFolder)
            => SalaryHistoryService.LoadSalaryHistory(employeeFolder);

        public int RemoveDuplicateSalaryHistoryRecordsAtStartup()
            => SalaryHistoryService.RemoveDuplicateSalaryHistoryRecordsAtStartup();

        #endregion

        #region Cross-Cutting Finance Operations

        public void RemoveEmployeeReferences(string originalFolder, string deletedFolder, string? employeeId = null)
        {
            bool Matches(string? folder, string? id = null)
            {
                if (!string.IsNullOrWhiteSpace(employeeId) && !string.IsNullOrWhiteSpace(id)
                    && string.Equals(id, employeeId, StringComparison.OrdinalIgnoreCase))
                    return true;

                return (!string.IsNullOrWhiteSpace(originalFolder) && string.Equals(folder, originalFolder, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(deletedFolder) && string.Equals(folder, deletedFolder, StringComparison.OrdinalIgnoreCase));
            }

            AdvancesService.RemoveAdvancesForEmployee(employeeId, originalFolder, deletedFolder);
            RequireLocalDb().RemoveAccommodationsForEmployee(originalFolder);
            if (!string.IsNullOrEmpty(deletedFolder))
                RequireLocalDb().RemoveAccommodationsForEmployee(deletedFolder);
            ReportsService.RemoveEmployeeEntries(employeeId, originalFolder, deletedFolder);

            // Modern month DBs / Postgres salary_entries (legacy JSON cleaned below).
            try
            {
                var removedEntries = MonthPaymentsService.RemoveEmployeeSalaryEntries(employeeId, originalFolder, deletedFolder);
                if (removedEntries > 0)
                    LoggingService.LogInfo("FinanceService.RemoveEmployeeReferences", $"Removed {removedEntries} salary_entries for employee.");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemoveEmployeeReferences.SalaryEntries", ex);
            }

            try
            {
                SalaryHistoryService.DeleteSalaryHistoryForEmployee(employeeId, originalFolder, deletedFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemoveEmployeeReferences.SalaryHistory", ex);
            }

            CleanupPaymentFiles(Matches);
        }

        /// <summary>
        /// After restoring from Recently Deleted, rewrite finance rows that still point at old folders.
        /// </summary>
        public void RemapEmployeeFolderReferences(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            if (string.IsNullOrWhiteSpace(toFolder))
                return;

            try
            {
                MonthPaymentsService.RemapEmployeeFolder(employeeId, fromFolderA, fromFolderB, toFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemapEmployeeFolderReferences.SalaryEntries", ex);
            }

            try
            {
                AdvancesService.RemapEmployeeFolder(employeeId, fromFolderA, fromFolderB, toFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemapEmployeeFolderReferences.Advances", ex);
            }

            try
            {
                SalaryHistoryService.RemapEmployeeFolder(employeeId, fromFolderA, fromFolderB, toFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemapEmployeeFolderReferences.SalaryHistory", ex);
            }

            try
            {
                var localDb = RequireLocalDb();
                if (!string.IsNullOrWhiteSpace(fromFolderA))
                    localDb.RemapAccommodationEmployeeFolder(fromFolderA, toFolder);
                if (!string.IsNullOrWhiteSpace(fromFolderB)
                    && !string.Equals(fromFolderB, fromFolderA, StringComparison.OrdinalIgnoreCase))
                    localDb.RemapAccommodationEmployeeFolder(fromFolderB, toFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.RemapEmployeeFolderReferences.Accommodations", ex);
            }

            InvalidatePaymentsCache();
        }

        #endregion

        #region Core Finance Orchestration

        public (decimal totalDebt, List<DebtInfoItem> details) CalculateCarriedDebt(string employeeFolder, int targetYear, int targetMonth)
        {
            return CalculateCarriedDebtForFirm(employeeFolder, null, targetYear, targetMonth);
        }

        public (decimal totalDebt, List<DebtInfoItem> details) CalculateCarriedDebtForFirm(string employeeFolder, string? firmName, int targetYear, int targetMonth)
        {
            var targetKey = $"{targetYear:D4}-{targetMonth:D2}";
            var savedPayments = LoadSavedPaymentsForEmployee(employeeFolder, firmName, targetKey);

            if (savedPayments.Count == 0)
                return (0, new List<DebtInfoItem>());

            var monthKeys = savedPayments.Keys.OrderBy(m => m).ToList();

            decimal runningDebt = 0;
            var debtDetails = new List<DebtInfoItem>();

            foreach (var mk in monthKeys)
            {
                var saved = savedPayments[mk];
                if (!saved.paid)
                    continue;

                if (saved.netSalary < 0)
                {
                    runningDebt = Math.Abs(saved.netSalary);
                    debtDetails.Clear();
                    debtDetails.Add(new DebtInfoItem { FromMonthKey = mk, Amount = runningDebt });
                }
                else
                {
                    runningDebt = 0;
                    debtDetails.Clear();
                }
            }

            return (runningDebt, debtDetails);
        }

        public Dictionary<string, decimal> CalculateCarriedDebtForEntries(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            int targetYear,
            int targetMonth)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var resolveCacheMs = 0L;
            var salaryHistoryLoadMs = 0L;
            var storedPaymentsMs = 0L;
            var mergeMs = 0L;
            var salaryHistoryRecordsLoaded = 0;
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (requests.Count == 0)
                return result;

            var targetKey = $"{targetYear:D4}-{targetMonth:D2}";
            var salaryHistoryByRequest = new Dictionary<string, List<SalaryHistoryRecord>>(StringComparer.OrdinalIgnoreCase);
            var storageRequestMap = new Dictionary<string, (string employeeFolder, string? employeeId)>(StringComparer.OrdinalIgnoreCase);
            var originalFolderByNormalizedKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var employeeIdByNormalizedKey = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                var normalizedFolder = NormalizeEmployeePath(request.employeeFolder);
                if (!originalFolderByNormalizedKey.ContainsKey(normalizedFolder))
                    originalFolderByNormalizedKey[normalizedFolder] = request.employeeFolder;

                if (!string.IsNullOrWhiteSpace(request.employeeId) && !employeeIdByNormalizedKey.ContainsKey(normalizedFolder))
                    employeeIdByNormalizedKey[normalizedFolder] = request.employeeId;
            }

            var resolvedFolderCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var employeeIdCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var salaryHistoryCache = new Dictionary<string, List<SalaryHistoryRecord>>(StringComparer.OrdinalIgnoreCase);

            var resolveCacheSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var pair in originalFolderByNormalizedKey)
            {
                var normalizedFolder = pair.Key;
                var originalFolder = pair.Value;
                employeeIdByNormalizedKey.TryGetValue(normalizedFolder, out var knownEmployeeId);
                var resolvedEmployeeFolder = !string.IsNullOrWhiteSpace(knownEmployeeId)
                    ? ResolveEmployeeFolder(originalFolder, knownEmployeeId)
                    : ResolveEmployeeFolder(originalFolder);
                resolvedFolderCache[normalizedFolder] = resolvedEmployeeFolder;
                employeeIdCache[normalizedFolder] = !string.IsNullOrWhiteSpace(knownEmployeeId)
                    ? knownEmployeeId
                    : ResolveEmployeeId(resolvedEmployeeFolder);
            }
            resolveCacheMs = resolveCacheSw.ElapsedMilliseconds;

            var salaryHistoryLoadSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var pair in originalFolderByNormalizedKey)
            {
                var normalizedFolder = pair.Key;
                try
                {
                    var resolvedEmployeeFolder = resolvedFolderCache[normalizedFolder];
                    var salaryHistory = SalaryHistoryService.LoadSalaryHistoryFromResolvedFolder(resolvedEmployeeFolder, employeeIdCache[normalizedFolder]);
                    salaryHistoryCache[normalizedFolder] = salaryHistory;
                    salaryHistoryRecordsLoaded += salaryHistory.Count;
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("FinanceService.CalculateCarriedDebtForEntries", ex);
                    salaryHistoryCache[normalizedFolder] = new List<SalaryHistoryRecord>();
                }
            }
            salaryHistoryLoadMs = salaryHistoryLoadSw.ElapsedMilliseconds;

            foreach (var request in requests)
            {
                result[request.requestKey] = 0m;
                var normalizedFolder = NormalizeEmployeePath(request.employeeFolder);
                salaryHistoryByRequest[request.requestKey] = salaryHistoryCache.TryGetValue(normalizedFolder, out var salaryHistory)
                    ? salaryHistory
                    : new List<SalaryHistoryRecord>();

                var resolvedEmployeeFolder = resolvedFolderCache.TryGetValue(normalizedFolder, out var resolvedFolder)
                    ? resolvedFolder
                    : ResolveEmployeeFolder(request.employeeFolder);
                var employeeId = employeeIdCache.TryGetValue(normalizedFolder, out var cachedEmployeeId)
                    ? cachedEmployeeId
                    : ResolveEmployeeId(resolvedEmployeeFolder);
                storageRequestMap[request.requestKey] = (resolvedEmployeeFolder, employeeId);
            }

            var storedPaymentsByRequest = new Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var storedPaymentsSw = System.Diagnostics.Stopwatch.StartNew();
                var storageRequests = requests
                    .Select(request =>
                    {
                        var storageRequest = storageRequestMap[request.requestKey];
                        return (
                            request.requestKey,
                            request.firmName,
                            storageRequest.employeeFolder,
                            storageRequest.employeeId);
                    })
                    .ToList();

                storedPaymentsByRequest = MonthPaymentsService.GetSavedPaymentsForAllRequests(targetKey, storageRequests);
                storedPaymentsMs = storedPaymentsSw.ElapsedMilliseconds;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.CalculateCarriedDebtForEntries.StoredPayments", ex.Message);
            }

            var mergeSw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var request in requests)
            {
                var savedPayments = new Dictionary<string, (decimal netSalary, bool paid)>(StringComparer.OrdinalIgnoreCase);

                if (salaryHistoryByRequest.TryGetValue(request.requestKey, out var salaryHistory))
                {
                    foreach (var record in salaryHistory)
                    {
                        var monthKey = $"{record.Year:D4}-{record.Month:D2}";
                        if (string.Compare(monthKey, targetKey, StringComparison.Ordinal) >= 0)
                            continue;

                        if (!string.Equals(record.FirmName, request.firmName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        savedPayments[monthKey] = (record.NetSalary, true);
                    }
                }

                if (storedPaymentsByRequest.TryGetValue(request.requestKey, out var storedPayments))
                {
                    foreach (var pair in storedPayments)
                        savedPayments.TryAdd(pair.Key, pair.Value);
                }

                if (savedPayments.Count == 0)
                    continue;

                decimal runningDebt = 0;
                foreach (var monthKey in savedPayments.Keys.OrderBy(m => m, StringComparer.OrdinalIgnoreCase))
                {
                    var saved = savedPayments[monthKey];
                    if (!saved.paid)
                        continue;

                    if (saved.netSalary < 0)
                    {
                        runningDebt = Math.Abs(saved.netSalary);
                    }
                    else
                    {
                        runningDebt = 0;
                    }
                }

                result[request.requestKey] = runningDebt;
            }
            mergeMs = mergeSw.ElapsedMilliseconds;

            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.CalculateCarriedDebtForEntries",
                $"CalculateCarriedDebtForEntries {targetYear:D4}-{targetMonth:D2} total={totalSw.ElapsedMilliseconds}ms | " +
                $"resolveCache={resolveCacheMs}ms | salaryHistoryLoad={salaryHistoryLoadMs}ms | " +
                $"storedPayments={storedPaymentsMs}ms | merge={mergeMs}ms | " +
                $"requests={requests.Count} | uniqueFolders={originalFolderByNormalizedKey.Count} | " +
                $"salaryHistoryRecords={salaryHistoryRecordsLoaded}");

            return result;
        }

        private Dictionary<string, (decimal netSalary, bool paid)> LoadSavedPaymentsForEmployee(
            string employeeFolder, string? firmName, string beforeMonthKey)
        {
            var result = new Dictionary<string, (decimal netSalary, bool paid)>();

            try
            {
                var salaryHistory = LoadSalaryHistory(employeeFolder);
                foreach (var r in salaryHistory)
                {
                    var mk = $"{r.Year:D4}-{r.Month:D2}";
                    if (string.Compare(mk, beforeMonthKey, StringComparison.Ordinal) >= 0) continue;
                    if (firmName != null && r.FirmName != firmName) continue;
                    result[mk] = (r.NetSalary, true);
                }
            }
            catch (Exception ex) { LoggingService.LogError("FinanceService.LoadSavedPaymentsForEmployee", ex); }

            if (firmName != null)
            {
                var resolvedEmployeeFolder = ResolveEmployeeFolder(employeeFolder);
                var employeeId = ResolveEmployeeId(resolvedEmployeeFolder);

                try
                {
                    var storedPayments = MonthPaymentsService.GetSavedPaymentsForEmployee(
                        resolvedEmployeeFolder,
                        employeeId,
                        firmName,
                        beforeMonthKey);

                    foreach (var pair in storedPayments)
                        result.TryAdd(pair.Key, pair.Value);

                    return result;
                }
                catch (Exception ex) { LoggingService.LogError("FinanceService.LoadSavedPaymentsForEmployee", ex); }
            }

            return result;
        }

        public void UpdateHourlyRateForward(string? employeeId, string employeeFolder, string firmName, decimal newRate, int fromYear, int fromMonth, CancellationToken cancellationToken = default)
        {
            var fromKey = $"{fromYear:D4}-{fromMonth:D2}";
            var resolvedEmployeeFolder = ResolveEmployeeFolder(employeeFolder, employeeId);

            try
            {
                MonthPaymentsService.UpdateHourlyRateForward(employeeId, resolvedEmployeeFolder, firmName, newRate, fromKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.UpdateHourlyRateForward", ex.Message);
            }

            InvalidatePaymentsCache();
        }

        public SalaryHistoryRecord BuildHistoryRecord(SalaryEntry entry, int year, int month, List<CustomSalaryField>? fields)
        {
            var record = new SalaryHistoryRecord
            {
                Year = year,
                Month = month,
                FirmName = entry.FirmName,
                FullName = entry.FullName,
                HoursWorked = entry.HoursWorked,
                HourlyRate = entry.HourlyRate,
                GrossSalary = entry.GrossSalary,
                Advance = entry.Advance,
                NetSalary = entry.NetSalary,
                Note = entry.Note,
                CustomValues = new Dictionary<string, decimal>(entry.CustomValues)
            };

            if (fields != null)
            {
                foreach (var f in fields.Where(fd => fd.FirmName == AllFirmsKey || fd.FirmName == entry.FirmName))
                {
                    if (entry.CustomValues.TryGetValue(f.Id, out var val) && val != 0)
                    {
                        record.CustomFields.Add(new CustomFieldSnapshot
                        {
                            Name = f.Name,
                            Operation = f.Operation.ToString(),
                            Value = val
                        });
                    }
                }
            }

            return record;
        }

        #endregion

        #region Accommodations

        public void AddAccommodation(AccommodationRecord rec)
        {
            RequireLocalDb().UpsertAccommodation(rec);
        }

        public decimal GetAccommodationForEmployee(string employeeFolder, string companyId, int year, int month)
        {
            return RequireLocalDb().GetAccommodationSum(employeeFolder, companyId, year, month);
        }

        public decimal GetAccommodationForEmployee(string employeeFolder, int year, int month)
        {
            return RequireLocalDb().GetAccommodationSum(employeeFolder, year, month);
        }

        #endregion

        #region Employee Resolution

        private readonly object _employeeIndexLock = new();
        private readonly object _employeeIndexBuildLock = new();
        private Dictionary<string, string> _idToFolderCache = new();
        private HashSet<string> _ghostFolders = new(StringComparer.OrdinalIgnoreCase);

        private LocalDbService RequireLocalDb()
        {
            if (_localDbService == null)
                throw new InvalidOperationException("LocalDbService is required for finance runtime storage.");

            return _localDbService;
        }

        public void BuildEmployeeIdIndex()
        {
            lock (_employeeIndexBuildLock)
            {
                var idToFolderCache = new Dictionary<string, string>();
                var ghostFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(_folderService.RootPath))
                {
                    SwapEmployeeIndex(idToFolderCache, ghostFolders);
                    return;
                }

                var archiveFolder = _folderService.GetArchiveFolder();

                var companies = _companyService.Companies;
                foreach (var company in companies)
                {
                    var empFolder = _folderService.GetEmployeesFolder(company.Name);
                    if (string.IsNullOrEmpty(empFolder) || !Directory.Exists(empFolder)) continue;
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(empFolder))
                        {
                            var jsonPath = Path.Combine(dir, "employee.json");
                            if (!File.Exists(jsonPath)) continue;
                            try
                            {
                                var data = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(jsonPath);
                                if (data == null) continue;
                                if (data.IsArchived)
                                {
                                    ghostFolders.Add(dir);
                                    continue;
                                }
                                if (!string.IsNullOrEmpty(data.UniqueId))
                                    idToFolderCache[data.UniqueId] = dir;
                            }
                            catch (Exception innerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", innerEx); }
                        }
                    }
                    catch (Exception outerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", outerEx); }
                }

                if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
                {
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(archiveFolder))
                        {
                            var jsonPath = Path.Combine(dir, "employee.json");
                            if (!File.Exists(jsonPath)) continue;
                            try
                            {
                                var data = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(jsonPath);
                                if (data != null && !string.IsNullOrEmpty(data.UniqueId) && !idToFolderCache.ContainsKey(data.UniqueId))
                                    idToFolderCache[data.UniqueId] = dir;
                            }
                            catch (Exception innerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", innerEx); }
                        }
                    }
                    catch (Exception outerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", outerEx); }
                }

                SwapEmployeeIndex(idToFolderCache, ghostFolders);
            }
        }

        private void SwapEmployeeIndex(Dictionary<string, string> idToFolderCache, HashSet<string> ghostFolders)
        {
            lock (_employeeIndexLock)
            {
                _idToFolderCache = idToFolderCache;
                _ghostFolders = ghostFolders;
            }
        }

        private List<string> SnapshotGhostFolders()
        {
            lock (_employeeIndexLock)
                return _ghostFolders.ToList();
        }

        private bool IsGhostFolder(string folder)
        {
            lock (_employeeIndexLock)
                return _ghostFolders.Contains(folder);
        }

        private bool TryGetCachedEmployeeFolder(string employeeId, out string cachedFolder)
        {
            lock (_employeeIndexLock)
            {
                if (_idToFolderCache.TryGetValue(employeeId, out var folder))
                {
                    cachedFolder = folder;
                    return true;
                }

                cachedFolder = string.Empty;
                return false;
            }
        }

        private void RemoveGhostFoldersFromIndex(IEnumerable<string> ghostFolders)
        {
            lock (_employeeIndexLock)
            {
                foreach (var ghost in ghostFolders)
                    _ghostFolders.Remove(ghost);
            }
        }

        public void CleanupGhostFolders()
        {
            var ghostSnapshot = SnapshotGhostFolders();
            foreach (var ghost in ghostSnapshot)
            {
                try
                {
                    if (!Directory.Exists(ghost)) continue;
                    var folderName = Path.GetFileName(ghost.TrimEnd('\\', '/'));

                    var archiveFolder = _folderService.GetArchiveFolder();
                    bool existsElsewhere = false;

                    if (!string.IsNullOrEmpty(archiveFolder))
                    {
                        var archCandidate = Path.Combine(archiveFolder, folderName);
                        if (Directory.Exists(archCandidate) && !string.Equals(archCandidate, ghost, StringComparison.OrdinalIgnoreCase))
                            existsElsewhere = true;
                    }

                    if (!existsElsewhere)
                    {
                        foreach (var company in _companyService.Companies)
                        {
                            var empFolder = _folderService.GetEmployeesFolder(company.Name);
                            if (string.IsNullOrEmpty(empFolder)) continue;
                            var candidate = Path.Combine(empFolder, folderName);
                            if (Directory.Exists(candidate) && !string.Equals(candidate, ghost, StringComparison.OrdinalIgnoreCase))
                            {
                                var cJson = Path.Combine(candidate, "employee.json");
                                if (File.Exists(cJson))
                                {
                                    try
                                    {
                                        var d = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(cJson);
                                        if (d != null && !d.IsArchived) { existsElsewhere = true; break; }
                                    }
                                    catch (Exception innerEx) { LoggingService.LogError("FinanceService.CleanupGhostFolders", innerEx); }
                                }
                            }
                        }
                    }

                    if (existsElsewhere)
                    {
                        foreach (var file in Directory.GetFiles(ghost, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, System.IO.FileAttributes.Normal);
                        Directory.Delete(ghost, true);
                        System.Diagnostics.Debug.WriteLine($"Cleaned ghost folder: {ghost}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("FinanceService.CleanupGhostFolders", ex);
                }
            }
            RemoveGhostFoldersFromIndex(ghostSnapshot);
        }

        public string? ResolveByEmployeeId(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId)) return null;
            if (TryGetCachedEmployeeFolder(employeeId, out var cached) && Directory.Exists(cached))
                return cached;

            BuildEmployeeIdIndex();
            if (TryGetCachedEmployeeFolder(employeeId, out cached) && Directory.Exists(cached))
                return cached;

            return null;
        }

        public string ResolveEmployeeFolder(string originalFolder, string? employeeId = null)
        {
            if (!string.IsNullOrEmpty(employeeId))
            {
                var byId = ResolveByEmployeeId(employeeId);
                if (byId != null) return byId;
            }

            if (!string.IsNullOrEmpty(originalFolder) && Directory.Exists(originalFolder))
            {
                if (!IsGhostFolder(originalFolder))
                    return originalFolder;
            }

            var trimmed = originalFolder?.TrimEnd('\\', '/') ?? "";
            var folderName = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(folderName)) return originalFolder ?? "";

            if (string.IsNullOrEmpty(_folderService.RootPath)) return originalFolder ?? "";
            foreach (var company in _companyService.Companies)
            {
                var empFolder = _folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(empFolder) || !Directory.Exists(empFolder)) continue;
                var candidate = Path.Combine(empFolder, folderName);
                if (Directory.Exists(candidate) && !IsGhostFolder(candidate))
                    return candidate;
            }

            var archiveFolder = _folderService.GetArchiveFolder();
            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
            {
                var candidate = Path.Combine(archiveFolder, folderName);
                if (Directory.Exists(candidate)) return candidate;
            }

            return originalFolder ?? "";
        }

        #endregion

        #region Legacy Cleanup

        internal static string NormalizeEmployeePath(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        internal string? ResolveEmployeeId(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            try
            {
                var indexRow = _employeeIndexDbService?.GetEmployeeRowByFolder(employeeFolder);
                if (indexRow != null && !string.IsNullOrWhiteSpace(indexRow.UniqueId))
                    return indexRow.UniqueId;

                var employeePath = Path.Combine(employeeFolder, "employee.json");
                return File.Exists(employeePath)
                    ? SafeFileService.ReadJson<EmployeeModels.EmployeeData>(employeePath)?.UniqueId
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void CleanupPaymentFiles(Func<string?, string?, bool> matches)
        {
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var paymentFolder in EnumeratePaymentFolders())
            {
                if (string.IsNullOrWhiteSpace(paymentFolder) || !Directory.Exists(paymentFolder))
                    continue;

                foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
                {
                    if (!processedFiles.Add(file))
                        continue;

                    try
                    {
                        var data = ReadJson<FirmPaymentData>(file);
                        if (data == null)
                            continue;

                        var removed = data.Entries.RemoveAll(e => matches(e.EmployeeFolder, e.EmployeeId));
                        if (removed <= 0)
                            continue;

                        data.UpdatedAt = DateTime.Now;
                        WriteJsonAtomic(file, data);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("FinanceService.CleanupPaymentFiles", ex);
                    }
                }
            }
        }

        private IEnumerable<string> EnumeratePaymentFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var company in _companyService.Companies)
            {
                var paymentFolder = _folderService.GetPaymentFolder(company.Name);
                if (!string.IsNullOrWhiteSpace(paymentFolder))
                    folders.Add(paymentFolder);
            }

            var archiveFolder = _folderService.GetArchiveFolder();
            if (!string.IsNullOrWhiteSpace(archiveFolder) && Directory.Exists(archiveFolder))
            {
                foreach (var dir in Directory.GetDirectories(archiveFolder))
                {
                    var paymentFolder = FindPaymentFolder(dir);
                    if (!string.IsNullOrWhiteSpace(paymentFolder))
                        folders.Add(paymentFolder);
                }
            }

            return folders;
        }

        private static string? FindPaymentFolder(string parentDir)
        {
            foreach (var name in Helpers.FolderNames.AllPaymentFolderNames)
            {
                var path = Path.Combine(parentDir, name);
                if (Directory.Exists(path)) return path;
            }
            return null;
        }

        #endregion
    }
}

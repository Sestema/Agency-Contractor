using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Win11DesktopApp.Models;
using System.Globalization;

namespace Win11DesktopApp.Services
{
    public class FinanceService
    {
        private readonly bool _suppressStartupNotifications;
        private readonly SalaryDbService? _salaryDbService;
        private readonly LocalDbService? _localDbService;
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

        public FinanceService(FolderService folderService, SalaryDbService? salaryDbService = null, LocalDbService? localDbService = null, bool suppressStartupNotifications = false)
        {
            _suppressStartupNotifications = suppressStartupNotifications;
            _salaryDbService = salaryDbService;
            _localDbService = localDbService;
            AdvancesService = new FinanceAdvancesService(
                _localDbService,
                ResolveEmployeeId,
                ResolveEmployeeFolder);
            SalaryHistoryService = new FinanceSalaryHistoryService(
                _localDbService,
                ResolveEmployeeId,
                ResolveEmployeeFolder);
            MonthPaymentsService = new FinanceMonthPaymentsService(
                _salaryDbService,
                () => LastSaveRecoveryPath = null,
                () =>
                {
                    LastSalaryConflictMessage = string.Empty;
                    LastSaveRecoveryPath = null;
                },
                message => LastSalaryConflictMessage = message);
            CustomFieldsService = new FinanceCustomFieldsService(_localDbService);
            ReportsService = new FinanceReportsService(_localDbService);
        }

        public SalaryDbMigrationResult EnsureSalaryMigratedToLocalDb()
        {
            try
            {
                if (_salaryDbService == null || _localDbService == null)
                    return new SalaryDbMigrationResult { Message = "SalaryDbService or LocalDbService is not configured." };

                if (IsSalaryMigrationCompleted())
                {
                    return new SalaryDbMigrationResult
                    {
                        WasMigrationAttempted = false,
                        IsSuccessful = true,
                        Message = "Salary migration already completed."
                    };
                }

                return MigrateSalaryJsonToMonthlyDatabases();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.EnsureSalaryMigratedToLocalDb", ex);
                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
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

        private bool IsSalaryMigrationCompleted()
        {
            try
            {
                using var connection = _localDbService!.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(1)
FROM migration_journal
WHERE stage = 'salary_entries'
  AND status = 'completed';";
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
            }
            catch
            {
                return false;
            }
        }

        private SalaryDbMigrationResult MigrateSalaryJsonToMonthlyDatabases()
        {
            if (_salaryDbService == null || _localDbService == null)
                return new SalaryDbMigrationResult { Message = "Salary migration services are not configured." };

            var monthBuckets = new Dictionary<string, (int year, int month, List<SalaryEntry> entries, List<FirmExpense> expenses)>(StringComparer.OrdinalIgnoreCase);
            var filesScanned = 0;
            var filesSkipped = 0;
            var recordsFound = 0;
            var expensesFound = 0;
            var databasesCreated = 0;

            _localDbService.RecordMigrationJournal("salary_entries", "started", 0, 0, null, 0, 0);

            try
            {
                foreach (var paymentFolder in EnumeratePaymentFolders())
                {
                    if (string.IsNullOrWhiteSpace(paymentFolder) || !Directory.Exists(paymentFolder))
                        continue;

                    foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
                    {
                        filesScanned++;

                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var parts = fileName.Split('_');
                        if (parts.Length != 3
                            || !int.TryParse(parts[1], out var year)
                            || !int.TryParse(parts[2], out var month))
                        {
                            filesSkipped++;
                            LoggingService.LogWarning("FinanceService.SalaryMigration",
                                $"Skipped unreadable salary filename: {file}");
                            continue;
                        }

                        try
                        {
                            var data = ReadJson<FirmPaymentData>(file);
                            if (data == null)
                            {
                                filesSkipped++;
                                LoggingService.LogWarning("FinanceService.SalaryMigration",
                                    $"Skipped unreadable salary JSON: {file}");
                                continue;
                            }

                            var monthKey = $"{year:D4}-{month:D2}";
                            if (!monthBuckets.TryGetValue(monthKey, out var bucket))
                            {
                                bucket = (year, month, new List<SalaryEntry>(), new List<FirmExpense>());
                            }

                            bucket.entries.AddRange(data.Entries.Select(CloneSalaryEntryForMigration));
                            bucket.expenses.AddRange(data.Expenses.Select(CloneFirmExpenseForSalaryMigration));
                            monthBuckets[monthKey] = bucket;

                            recordsFound += data.Entries.Count;
                            expensesFound += data.Expenses.Count;
                        }
                        catch (Exception ex)
                        {
                            filesSkipped++;
                            LoggingService.LogError("FinanceService.SalaryMigration.ReadFile",
                                new IOException($"file={file}: {ex.Message}", ex));
                        }
                    }
                }

                var recordsImported = 0;
                var expensesImported = 0;

                foreach (var bucket in monthBuckets.Values.OrderBy(v => v.year).ThenBy(v => v.month))
                {
                    var monthDbExisted = _salaryDbService.MonthDbExists(bucket.year, bucket.month);
                    _salaryDbService.ReplaceMonthData(bucket.year, bucket.month, bucket.entries, bucket.expenses);
                    if (!monthDbExisted)
                        databasesCreated++;

                    var snapshot = _salaryDbService.GetMonthValidationSnapshot(bucket.year, bucket.month);
                    ValidateMonthSnapshot(bucket.year, bucket.month, bucket.entries, bucket.expenses, snapshot);

                    recordsImported += snapshot.EntryCount;
                    expensesImported += snapshot.ExpenseCount;
                }

                _localDbService.RecordMigrationJournal(
                    "salary_entries",
                    "completed",
                    recordsFound + expensesFound,
                    recordsImported + expensesImported,
                    null,
                    filesScanned,
                    filesSkipped);

                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    FilesScanned = filesScanned,
                    FilesSkipped = filesSkipped,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    ExpensesFound = expensesFound,
                    ExpensesImported = expensesImported,
                    DatabasesCreated = databasesCreated,
                    Message = $"Migrated salary JSON to {monthBuckets.Count} monthly SQLite databases."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.MigrateSalaryJsonToMonthlyDatabases", ex);
                _localDbService.RecordMigrationJournal(
                    "salary_entries",
                    "failed",
                    recordsFound + expensesFound,
                    0,
                    ex.Message,
                    filesScanned,
                    filesSkipped);

                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    FilesScanned = filesScanned,
                    FilesSkipped = filesSkipped,
                    RecordsFound = recordsFound,
                    ExpensesFound = expensesFound,
                    Message = ex.Message
                };
            }
        }

        private static SalaryEntry CloneSalaryEntryForMigration(SalaryEntry entry)
            => SalaryEntryCloneHelper.CloneEntry(entry);

        private static FirmExpense CloneFirmExpenseForSalaryMigration(FirmExpense expense)
        {
            return new FirmExpense
            {
                Id = expense.Id,
                FirmName = expense.FirmName,
                Year = expense.Year,
                Month = expense.Month,
                Name = expense.Name,
                Amount = expense.Amount
            };
        }

        private static void ValidateMonthSnapshot(
            int year,
            int month,
            IReadOnlyCollection<SalaryEntry> expectedEntries,
            IReadOnlyCollection<FirmExpense> expectedExpenses,
            (int EntryCount, int ExpenseCount, decimal SavedNetSalaryTotal, Dictionary<string, int> StatusCounts) snapshot)
        {
            if (snapshot.EntryCount != expectedEntries.Count)
                throw new InvalidOperationException($"Salary entry count mismatch for {year:D4}-{month:D2}: expected {expectedEntries.Count}, got {snapshot.EntryCount}.");

            if (snapshot.ExpenseCount != expectedExpenses.Count)
                throw new InvalidOperationException($"Salary expense count mismatch for {year:D4}-{month:D2}: expected {expectedExpenses.Count}, got {snapshot.ExpenseCount}.");

            var expectedNetTotal = expectedEntries.Sum(e => e.SavedNetSalary);
            if (expectedNetTotal != snapshot.SavedNetSalaryTotal)
                throw new InvalidOperationException($"SavedNetSalary mismatch for {year:D4}-{month:D2}: expected {expectedNetTotal}, got {snapshot.SavedNetSalaryTotal}.");

            var expectedStatusCounts = expectedEntries
                .GroupBy(e => e.Status ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var pair in expectedStatusCounts)
            {
                if (!snapshot.StatusCounts.TryGetValue(pair.Key, out var actual) || actual != pair.Value)
                    throw new InvalidOperationException($"Status distribution mismatch for {year:D4}-{month:D2}: status={pair.Key}, expected {pair.Value}, got {actual}.");
            }
        }

        #region Facade Delegations

        public LocalDbMigrationResult EnsureSalaryHistoryMigratedToLocalDb()
            => SalaryHistoryService.EnsureMigratedToLocalDb();

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

        public void UpdateFirmExpense(FirmExpense updated)
            => MonthPaymentsService.UpdateFirmExpense(updated);

        public void RemoveFirmExpense(string expenseId)
            => MonthPaymentsService.RemoveFirmExpense(expenseId);

        public void RemoveFirmExpense(string expenseId, int year, int month)
            => MonthPaymentsService.RemoveFirmExpense(expenseId, year, month);

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month, string? firmNameFilter = null)
            => MonthPaymentsService.SaveFirmExpenses(expenses, year, month, firmNameFilter);

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
            => MonthPaymentsService.SaveAllFirmPayments(year, month, allEntries, allExpenses);

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month, bool forceReload = false)
            => MonthPaymentsService.LoadAllFirmPayments(year, month, forceReload);

        public void InvalidatePaymentsCache(int? year = null, int? month = null)
            => MonthPaymentsService.InvalidatePaymentsCache(year, month);

        public bool MonthDataExists(int year, int month)
        {
            return _salaryDbService?.MonthDbExists(year, month) == true;
        }

        public void SaveSalaryHistoryRecord(string employeeFolder, SalaryHistoryRecord record)
            => SalaryHistoryService.SaveSalaryHistoryRecord(employeeFolder, record);

        public void RemoveSalaryHistoryRecord(string employeeFolder, int year, int month, string firmName)
            => SalaryHistoryService.RemoveSalaryHistoryRecord(employeeFolder, year, month, firmName);

        public List<SalaryHistoryRecord> LoadSalaryHistory(string employeeFolder)
            => SalaryHistoryService.LoadSalaryHistory(employeeFolder);

        public int CleanupMigratedSalaryHistoryBackups()
            => SalaryHistoryService.CleanupMigratedSalaryHistoryBackups();

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

            CleanupPaymentFiles(Matches);
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
            var sqliteSavedPaymentsMs = 0L;
            var mergeMs = 0L;
            var salaryHistoryRecordsLoaded = 0;
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (requests.Count == 0)
                return result;

            var targetKey = $"{targetYear:D4}-{targetMonth:D2}";
            var salaryHistoryByRequest = new Dictionary<string, List<SalaryHistoryRecord>>(StringComparer.OrdinalIgnoreCase);
            var sqliteRequestMap = new Dictionary<string, (string employeeFolder, string? employeeId)>(StringComparer.OrdinalIgnoreCase);
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
                sqliteRequestMap[request.requestKey] = (resolvedEmployeeFolder, employeeId);
            }

            var sqliteSavedPaymentsByRequest = new Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>>(StringComparer.OrdinalIgnoreCase);
            if (_salaryDbService != null)
            {
                var sqliteSavedPaymentsSw = System.Diagnostics.Stopwatch.StartNew();
                var sqliteRequests = requests
                    .Select(request =>
                    {
                        var sqliteRequest = sqliteRequestMap[request.requestKey];
                        return (
                            request.requestKey,
                            request.firmName,
                            sqliteRequest.employeeFolder,
                            sqliteRequest.employeeId);
                    })
                    .ToList();

                sqliteSavedPaymentsByRequest = _salaryDbService.GetSavedPaymentsForAllRequests(targetKey, sqliteRequests);
                sqliteSavedPaymentsMs = sqliteSavedPaymentsSw.ElapsedMilliseconds;
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

                if (sqliteSavedPaymentsByRequest.TryGetValue(request.requestKey, out var sqliteSavedPayments))
                {
                    foreach (var pair in sqliteSavedPayments)
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
                $"sqliteSavedPayments={sqliteSavedPaymentsMs}ms | merge={mergeMs}ms | " +
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
                    if (_salaryDbService != null)
                    {
                        var sqliteSavedPayments = _salaryDbService.GetSavedPaymentsForEmployee(
                            resolvedEmployeeFolder,
                            employeeId,
                            firmName,
                            beforeMonthKey);

                        foreach (var pair in sqliteSavedPayments)
                            result.TryAdd(pair.Key, pair.Value);

                        return result;
                    }
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
                _salaryDbService?.UpdateHourlyRateForward(employeeId, resolvedEmployeeFolder, firmName, newRate, fromKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.UpdateHourlyRateForward.SQLite", ex.Message);
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

        private readonly Dictionary<string, string> _idToFolderCache = new();
        private readonly HashSet<string> _ghostFolders = new(StringComparer.OrdinalIgnoreCase);

        private LocalDbService RequireLocalDb()
        {
            if (_localDbService == null)
                throw new InvalidOperationException("LocalDbService is required for finance runtime storage.");

            return _localDbService;
        }

        public void BuildEmployeeIdIndex()
        {
            _idToFolderCache.Clear();
            _ghostFolders.Clear();
            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return;

            var archiveFolder = folderService.GetArchiveFolder();

            var companies = App.CompanyService?.Companies;
            if (companies == null) return;
            foreach (var company in companies)
            {
                var empFolder = folderService.GetEmployeesFolder(company.Name);
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
                                _ghostFolders.Add(dir);
                                continue;
                            }
                            if (!string.IsNullOrEmpty(data.UniqueId))
                                _idToFolderCache[data.UniqueId] = dir;
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
                            if (data != null && !string.IsNullOrEmpty(data.UniqueId) && !_idToFolderCache.ContainsKey(data.UniqueId))
                                _idToFolderCache[data.UniqueId] = dir;
                        }
                        catch (Exception innerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", innerEx); }
                    }
                }
                catch (Exception outerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", outerEx); }
            }
        }

        public void CleanupGhostFolders()
        {
            foreach (var ghost in _ghostFolders.ToList())
            {
                try
                {
                    if (!Directory.Exists(ghost)) continue;
                    var folderName = Path.GetFileName(ghost.TrimEnd('\\', '/'));

                    var folderService = App.FolderService;
                    if (folderService == null) continue;
                    var archiveFolder = folderService.GetArchiveFolder();
                    bool existsElsewhere = false;

                    if (!string.IsNullOrEmpty(archiveFolder))
                    {
                        var archCandidate = Path.Combine(archiveFolder, folderName);
                        if (Directory.Exists(archCandidate) && !string.Equals(archCandidate, ghost, StringComparison.OrdinalIgnoreCase))
                            existsElsewhere = true;
                    }

                    if (!existsElsewhere)
                    {
                        var ghostCompanies = App.CompanyService?.Companies;
                        if (ghostCompanies != null)
                        foreach (var company in ghostCompanies)
                        {
                            var empFolder = folderService.GetEmployeesFolder(company.Name);
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
                    System.Diagnostics.Debug.WriteLine($"CleanupGhostFolders error: {ex.Message}");
                    LoggingService.LogError("FinanceService.CleanupGhostFolders", ex);
                }
            }
            _ghostFolders.Clear();
        }

        public string? ResolveByEmployeeId(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId)) return null;
            if (_idToFolderCache.TryGetValue(employeeId, out var cached) && Directory.Exists(cached))
                return cached;

            BuildEmployeeIdIndex();
            if (_idToFolderCache.TryGetValue(employeeId, out cached) && Directory.Exists(cached))
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
                if (!_ghostFolders.Contains(originalFolder))
                    return originalFolder;
            }

            var trimmed = originalFolder?.TrimEnd('\\', '/') ?? "";
            var folderName = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(folderName)) return originalFolder ?? "";

            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return originalFolder ?? "";

            var companies = App.CompanyService?.Companies;
            if (companies == null) return originalFolder ?? "";
            foreach (var company in companies)
            {
                var empFolder = folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(empFolder) || !Directory.Exists(empFolder)) continue;
                var candidate = Path.Combine(empFolder, folderName);
                if (Directory.Exists(candidate) && !_ghostFolders.Contains(candidate))
                    return candidate;
            }

            var archiveFolder = folderService.GetArchiveFolder();
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
                var indexRow = App.EmployeeIndexDbService?.GetEmployeeRowByFolder(employeeFolder);
                if (indexRow != null && !string.IsNullOrWhiteSpace(indexRow.UniqueId))
                    return indexRow.UniqueId;

                return App.EmployeeService?.LoadEmployeeData(employeeFolder)?.UniqueId;
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
            var folderService = App.FolderService;
            var companies = App.CompanyService?.Companies;

            if (folderService == null || companies == null)
                return folders;

            foreach (var company in companies)
            {
                var paymentFolder = folderService.GetPaymentFolder(company.Name);
                if (!string.IsNullOrWhiteSpace(paymentFolder))
                    folders.Add(paymentFolder);
            }

            var archiveFolder = folderService.GetArchiveFolder();
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceMonthPaymentsService
    {
        private readonly SalaryDbService? _salaryDbService;
        private readonly LocalDbService? _localDbService;
        private readonly FinanceDatabase _db;
        private readonly Action _clearLastSaveRecoveryPath;
        private readonly Action _clearSalarySaveState;
        private readonly Action<string> _setSalaryConflictMessage;
        private readonly object _paymentsCacheLock = new();
        private readonly Dictionary<(int year, int month), (List<SalaryEntry> entries, List<FirmExpense> expenses)> _paymentsCache = new();

        public FinanceMonthPaymentsService(
            SalaryDbService? salaryDbService,
            LocalDbService? localDbService,
            FinanceDatabase db,
            Action clearLastSaveRecoveryPath,
            Action clearSalarySaveState,
            Action<string> setSalaryConflictMessage)
        {
            _salaryDbService = salaryDbService;
            _localDbService = localDbService;
            _db = db;
            _clearLastSaveRecoveryPath = clearLastSaveRecoveryPath;
            _clearSalarySaveState = clearSalarySaveState;
            _setSalaryConflictMessage = setSalaryConflictMessage;
        }

        public SalaryDbMigrationResult EnsureMigratedToSalaryDb()
        {
            try
            {
                if (_localDbService != null && _localDbService.IsFirmExpensesMigrationCompleted())
                {
                    return new SalaryDbMigrationResult
                    {
                        WasMigrationAttempted = false,
                        IsSuccessful = true,
                        Message = "Firm expenses migration already completed."
                    };
                }

                if (_salaryDbService == null)
                {
                    return new SalaryDbMigrationResult
                    {
                        WasMigrationAttempted = false,
                        IsSuccessful = false,
                        Message = "SalaryDbService is not configured."
                    };
                }

                var legacyExpenses = CloneFirmExpenses(_db.FirmExpenses);
                if (legacyExpenses.Count == 0)
                {
                    _localDbService?.RecordMigrationJournal("firm_expenses_salary_db", "completed", 0, 0, null, 0, 0);
                    return new SalaryDbMigrationResult
                    {
                        WasMigrationAttempted = false,
                        IsSuccessful = true,
                        Message = "No legacy firm expenses found for migration."
                    };
                }

                var monthBuckets = legacyExpenses
                    .GroupBy(expense => (expense.Year, expense.Month))
                    .OrderBy(group => group.Key.Year)
                    .ThenBy(group => group.Key.Month)
                    .ToList();

                var importedExpenses = 0;
                var databasesCreated = 0;
                _localDbService?.RecordMigrationJournal("firm_expenses_salary_db", "started", legacyExpenses.Count, 0, null, 0, 0);

                foreach (var bucket in monthBuckets)
                {
                    var year = bucket.Key.Year;
                    var month = bucket.Key.Month;
                    var monthDbExists = _salaryDbService.MonthDbExists(year, month);
                    if (!monthDbExists)
                        databasesCreated++;

                    var monthData = _salaryDbService.LoadMonthPayments(year, month);
                    var mergedExpenses = new Dictionary<string, FirmExpense>(StringComparer.OrdinalIgnoreCase);

                    foreach (var existingExpense in monthData.expenses)
                    {
                        var id = string.IsNullOrWhiteSpace(existingExpense.Id) ? Guid.NewGuid().ToString() : existingExpense.Id;
                        existingExpense.Id = id;
                        mergedExpenses[id] = existingExpense;
                    }

                    foreach (var legacyExpense in bucket)
                    {
                        var id = string.IsNullOrWhiteSpace(legacyExpense.Id) ? Guid.NewGuid().ToString() : legacyExpense.Id;
                        legacyExpense.Id = id;
                        mergedExpenses[id] = legacyExpense;
                        importedExpenses++;
                    }

                    _salaryDbService.SaveMonthPayments(
                        year,
                        month,
                        CloneSalaryEntries(monthData.entries),
                        CloneFirmExpenses(mergedExpenses.Values
                            .OrderBy(expense => expense.FirmName, StringComparer.Ordinal)
                            .ThenBy(expense => expense.Name, StringComparer.Ordinal)
                            .ThenBy(expense => expense.Id, StringComparer.Ordinal)));

                    InvalidatePaymentsCache(year, month);
                }

                _db.FirmExpenses.Clear();
                _localDbService?.RecordMigrationJournal("firm_expenses_salary_db", "completed", legacyExpenses.Count, importedExpenses, null, 0, 0);

                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    ExpensesFound = legacyExpenses.Count,
                    ExpensesImported = importedExpenses,
                    DatabasesCreated = databasesCreated,
                    Message = $"Migrated {importedExpenses} legacy firm expenses to salary month SQLite."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.EnsureMigratedToSalaryDb", ex);
                _localDbService?.RecordMigrationJournal("firm_expenses_salary_db", "failed", _db.FirmExpenses.Count, 0, ex.Message, 0, 0);
                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    ExpensesFound = _db.FirmExpenses.Count,
                    Message = ex.Message
                };
            }
        }

        public List<FirmExpense> GetFirmExpenses(int year, int month)
            => LoadFirmExpensesForMonth(year, month);

        public List<FirmExpense> GetFirmExpenses(int year, int month, string firmName)
        {
            return LoadFirmExpensesForMonth(year, month)
                .Where(e => e.FirmName == firmName)
                .ToList();
        }

        public List<FirmExpense> GetFirmExpensesForFirms(int year, int month, IEnumerable<string> firmNames)
        {
            var set = new HashSet<string>(firmNames);
            return LoadFirmExpensesForMonth(year, month)
                .Where(e => set.Contains(e.FirmName))
                .ToList();
        }

        public void AddFirmExpense(FirmExpense expense)
        {
            if (string.IsNullOrEmpty(expense.Id))
                expense.Id = Guid.NewGuid().ToString();

            if (_salaryDbService != null)
            {
                var monthEntries = LoadAllFirmPayments(expense.Year, expense.Month).entries;
                var monthExpenses = LoadFirmExpensesForMonth(expense.Year, expense.Month);
                monthExpenses.Add(CloneFirmExpense(expense));
                SaveAllFirmPayments(expense.Year, expense.Month, monthEntries, monthExpenses);
                return;
            }

            _db.FirmExpenses.Add(expense);
            InvalidatePaymentsCache(expense.Year, expense.Month);
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            if (_salaryDbService != null)
            {
                var monthEntries = LoadAllFirmPayments(updated.Year, updated.Month).entries;
                var monthExpenses = LoadFirmExpensesForMonth(updated.Year, updated.Month);
                var idx = monthExpenses.FindIndex(e => e.Id == updated.Id);
                if (idx >= 0)
                    monthExpenses[idx] = CloneFirmExpense(updated);
                else
                    monthExpenses.Add(CloneFirmExpense(updated));

                SaveAllFirmPayments(updated.Year, updated.Month, monthEntries, monthExpenses);
                return;
            }

            var legacyIdx = _db.FirmExpenses.FindIndex(e => e.Id == updated.Id);
            if (legacyIdx >= 0)
                _db.FirmExpenses[legacyIdx] = updated;
            InvalidatePaymentsCache(updated.Year, updated.Month);
        }

        public void RemoveFirmExpense(string expenseId)
        {
            var removed = _db.FirmExpenses.FirstOrDefault(e => e.Id == expenseId);
            if (removed != null)
            {
                RemoveFirmExpense(expenseId, removed.Year, removed.Month);
                return;
            }

            if (_salaryDbService != null)
            {
                foreach (var monthDb in _salaryDbService.EnumerateMonthDatabases())
                {
                    var monthExpenses = LoadFirmExpensesForMonth(monthDb.year, monthDb.month);
                    var match = monthExpenses.FirstOrDefault(e => e.Id == expenseId);
                    if (match == null)
                        continue;

                    RemoveFirmExpense(expenseId, monthDb.year, monthDb.month);
                    return;
                }
            }

            _db.FirmExpenses.RemoveAll(e => e.Id == expenseId);
        }

        public void RemoveFirmExpense(string expenseId, int year, int month)
        {
            if (_salaryDbService != null)
            {
                var monthEntries = LoadAllFirmPayments(year, month).entries;
                var monthExpenses = LoadFirmExpensesForMonth(year, month);
                var removedCount = monthExpenses.RemoveAll(e => e.Id == expenseId);
                if (removedCount > 0)
                    SaveAllFirmPayments(year, month, monthEntries, monthExpenses);
                return;
            }

            var removed = _db.FirmExpenses.FirstOrDefault(e => e.Id == expenseId);
            _db.FirmExpenses.RemoveAll(e => e.Id == expenseId);
            if (removed != null)
                InvalidatePaymentsCache(removed.Year, removed.Month);
        }

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month, string? firmNameFilter = null)
        {
            if (_salaryDbService != null)
            {
                List<FirmExpense> monthExpenses;
                if (string.IsNullOrWhiteSpace(firmNameFilter))
                {
                    monthExpenses = CloneFirmExpenses(expenses);
                }
                else
                {
                    monthExpenses = LoadFirmExpensesForMonth(year, month)
                        .Where(expense => !string.Equals(expense.FirmName, firmNameFilter, StringComparison.Ordinal))
                        .ToList();
                    monthExpenses.AddRange(CloneFirmExpenses(expenses));
                }

                var monthEntries = LoadAllFirmPayments(year, month).entries;
                SaveAllFirmPayments(year, month, monthEntries, monthExpenses);
                return;
            }

            _db.FirmExpenses.RemoveAll(e =>
                e.Year == year
                && e.Month == month
                && (string.IsNullOrWhiteSpace(firmNameFilter) || string.Equals(e.FirmName, firmNameFilter, StringComparison.Ordinal)));
            _db.FirmExpenses.AddRange(CloneFirmExpenses(expenses));
            InvalidatePaymentsCache(year, month);
        }

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            _clearLastSaveRecoveryPath();

            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath))
                return false;

            try
            {
                _salaryDbService?.SaveMonthPayments(year, month, CloneSalaryEntries(allEntries), CloneFirmExpenses(allExpenses));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Multiple salary DB files found", StringComparison.Ordinal))
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.SQLite", ex.Message);
                _setSalaryConflictMessage(
                    $"Знайдено кілька файлів виплати за {year:D4}-{month:D2}. Збереження зупинено, щоб не втратити дані. Приберіть дубльований файл і повторіть спробу.");
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.SaveAllFirmPayments.SQLite", ex);
                return false;
            }

            InvalidatePaymentsCache(year, month);
            _clearSalarySaveState();
            return true;
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month, bool forceReload = false)
        {
            var cacheKey = (year, month);
            if (!forceReload)
            {
                lock (_paymentsCacheLock)
                {
                    if (_paymentsCache.TryGetValue(cacheKey, out var cached))
                        return (CloneSalaryEntries(cached.entries), CloneFirmExpenses(cached.expenses));
                }
            }

            if (_salaryDbService != null)
            {
                try
                {
                    if (_salaryDbService.MonthDbExists(year, month))
                    {
                        var sqliteResult = _salaryDbService.LoadMonthPayments(year, month);
                        var sqliteEntries = CloneSalaryEntries(sqliteResult.entries);
                        var sqliteExpenses = CloneFirmExpenses(sqliteResult.expenses);
                        lock (_paymentsCacheLock)
                        {
                            _paymentsCache[cacheKey] = (sqliteEntries, sqliteExpenses);
                        }

                        return (CloneSalaryEntries(sqliteEntries), CloneFirmExpenses(sqliteExpenses));
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Multiple salary DB files found", StringComparison.Ordinal))
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.LoadAllFirmPayments.SQLite", ex.Message);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.LoadAllFirmPayments.SQLite", ex.Message);
                }
            }

            return (new List<SalaryEntry>(), new List<FirmExpense>());
        }

        public void InvalidatePaymentsCache(int? year = null, int? month = null)
        {
            lock (_paymentsCacheLock)
            {
                if (year.HasValue && month.HasValue)
                {
                    _paymentsCache.Remove((year.Value, month.Value));
                    return;
                }

                _paymentsCache.Clear();
            }
        }

        private List<FirmExpense> LoadFirmExpensesForMonth(int year, int month)
        {
            if (_salaryDbService != null)
            {
                try
                {
                    if (_salaryDbService.MonthDbExists(year, month))
                        return LoadAllFirmPayments(year, month).expenses;
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.LoadFirmExpensesForMonth.SQLite", ex.Message);
                }
            }

            return _db.FirmExpenses
                .Where(e => e.Year == year && e.Month == month)
                .Select(CloneFirmExpense)
                .ToList();
        }

        private static FirmExpense CloneFirmExpense(FirmExpense expense)
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

        private static List<FirmExpense> CloneFirmExpenses(IEnumerable<FirmExpense> source)
            => source.Select(CloneFirmExpense).ToList();

        private static List<SalaryEntry> CloneSalaryEntries(IEnumerable<SalaryEntry> source)
            => SalaryEntryCloneHelper.CloneEntries(source);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceMonthPaymentsService
    {
        private readonly FolderService _folderService;
        private readonly IFinanceMonthPaymentsStorage? _monthPaymentsStorage;
        private readonly SharedOperationLockService? _sharedOperationLockService;
        private readonly Action _clearLastSaveRecoveryPath;
        private readonly Action _clearSalarySaveState;
        private readonly Action<string> _setSalaryConflictMessage;
        private readonly object _paymentsCacheLock = new();
        private readonly Dictionary<(int year, int month), (List<SalaryEntry> entries, List<FirmExpense> expenses)> _paymentsCache = new();

        public FinanceMonthPaymentsService(
            FolderService folderService,
            IFinanceMonthPaymentsStorage? monthPaymentsStorage,
            Action clearLastSaveRecoveryPath,
            Action clearSalarySaveState,
            Action<string> setSalaryConflictMessage,
            SharedOperationLockService? sharedOperationLockService = null)
        {
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _monthPaymentsStorage = monthPaymentsStorage;
            _sharedOperationLockService = sharedOperationLockService;
            _clearLastSaveRecoveryPath = clearLastSaveRecoveryPath;
            _clearSalarySaveState = clearSalarySaveState;
            _setSalaryConflictMessage = setSalaryConflictMessage;
        }

        public List<FirmExpense> GetFirmExpenses(int year, int month)
            => LoadFirmExpensesForMonth(year, month);

        public List<FirmExpense> GetFirmExpenses(int year, int month, string firmName)
        {
            return LoadFirmExpensesForMonth(year, month)
                .Where(e => string.Equals(e.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public List<FirmExpense> GetFirmExpensesForFirms(int year, int month, IEnumerable<string> firmNames)
        {
            var set = new HashSet<string>(firmNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return LoadFirmExpensesForMonth(year, month)
                .Where(e => set.Contains(e.FirmName))
                .ToList();
        }

        public void AddFirmExpense(FirmExpense expense)
        {
            if (string.IsNullOrEmpty(expense.Id))
                expense.Id = Guid.NewGuid().ToString();

            EnsureSalaryDbConfigured();
            using var salaryLock = TryAcquireSalaryWriteLock(expense.Year, expense.Month, expense.FirmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.AddFirmExpense", BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                return;
            }

            PersistFirmExpenseAdd(expense);
        }

        public async Task AddFirmExpenseAsync(FirmExpense expense, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(expense.Id))
                expense.Id = Guid.NewGuid().ToString();

            EnsureSalaryDbConfigured();
            using var salaryLock = await TryAcquireSalaryWriteLockAsync(expense.Year, expense.Month, expense.FirmName, cancellationToken)
                .ConfigureAwait(false);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.AddFirmExpense", BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                return;
            }

            PersistFirmExpenseAdd(expense);
        }

        private void PersistFirmExpenseAdd(FirmExpense expense)
        {
            _monthPaymentsStorage!.UpsertFirmExpense(expense.Year, expense.Month, CloneFirmExpense(expense));
            InvalidatePaymentsCache(expense.Year, expense.Month);
            _clearSalarySaveState();
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            EnsureSalaryDbConfigured();
            using var salaryLock = TryAcquireSalaryWriteLock(updated.Year, updated.Month, updated.FirmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(updated.Year, updated.Month, updated.FirmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.UpdateFirmExpense", BuildSalaryLockMessage(updated.Year, updated.Month, updated.FirmName));
                return;
            }

            PersistFirmExpenseUpdate(updated);
        }

        public async Task UpdateFirmExpenseAsync(FirmExpense updated, CancellationToken cancellationToken = default)
        {
            EnsureSalaryDbConfigured();
            using var salaryLock = await TryAcquireSalaryWriteLockAsync(updated.Year, updated.Month, updated.FirmName, cancellationToken)
                .ConfigureAwait(false);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(updated.Year, updated.Month, updated.FirmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.UpdateFirmExpense", BuildSalaryLockMessage(updated.Year, updated.Month, updated.FirmName));
                return;
            }

            PersistFirmExpenseUpdate(updated);
        }

        private void PersistFirmExpenseUpdate(FirmExpense updated)
        {
            _monthPaymentsStorage!.UpsertFirmExpense(updated.Year, updated.Month, CloneFirmExpense(updated));
            InvalidatePaymentsCache(updated.Year, updated.Month);
            _clearSalarySaveState();
        }

        public void RemoveFirmExpense(string expenseId)
        {
            EnsureSalaryDbConfigured();
            foreach (var monthDb in _monthPaymentsStorage!.EnumerateMonthDatabases())
            {
                var monthExpenses = LoadFirmExpensesForMonth(monthDb.year, monthDb.month);
                var match = monthExpenses.FirstOrDefault(e => e.Id == expenseId);
                if (match == null)
                    continue;

                RemoveFirmExpense(expenseId, monthDb.year, monthDb.month);
                return;
            }
        }

        public void RemoveFirmExpense(string expenseId, int year, int month)
        {
            EnsureSalaryDbConfigured();
            var monthExpenses = LoadFirmExpensesForMonth(year, month);
            var firmName = monthExpenses.FirstOrDefault(expense => expense.Id == expenseId)?.FirmName ?? string.Empty;
            using var salaryLock = TryAcquireSalaryWriteLock(year, month, firmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(year, month, firmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.RemoveFirmExpense", BuildSalaryLockMessage(year, month, firmName));
                return;
            }

            PersistFirmExpenseRemove(expenseId, year, month);
        }

        public async Task RemoveFirmExpenseAsync(string expenseId, int year, int month, CancellationToken cancellationToken = default)
        {
            EnsureSalaryDbConfigured();
            var monthExpenses = LoadFirmExpensesForMonth(year, month);
            var firmName = monthExpenses.FirstOrDefault(expense => expense.Id == expenseId)?.FirmName ?? string.Empty;
            using var salaryLock = await TryAcquireSalaryWriteLockAsync(year, month, firmName, cancellationToken)
                .ConfigureAwait(false);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(year, month, firmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.RemoveFirmExpense", BuildSalaryLockMessage(year, month, firmName));
                return;
            }

            PersistFirmExpenseRemove(expenseId, year, month);
        }

        private void PersistFirmExpenseRemove(string expenseId, int year, int month)
        {
            if (_monthPaymentsStorage!.DeleteFirmExpense(year, month, expenseId))
            {
                InvalidatePaymentsCache(year, month);
                _clearSalarySaveState();
            }
        }

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month, string? firmNameFilter = null)
        {
            EnsureSalaryDbConfigured();
            if (string.IsNullOrWhiteSpace(firmNameFilter))
            {
                // Expenses-only write: never reload/rewrite salary_entries from cache
                // (that could overwrite newer multi-PC salary edits).
                using var salaryLock = TryAcquireSalaryWriteLock(year, month);
                if (_sharedOperationLockService != null && salaryLock == null)
                {
                    _setSalaryConflictMessage(BuildSalaryLockMessage(year, month));
                    LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmExpenses", BuildSalaryLockMessage(year, month));
                    return;
                }

                _monthPaymentsStorage!.ReplaceAllFirmExpenses(year, month, CloneFirmExpenses(expenses));
                InvalidatePaymentsCache(year, month);
                _clearSalarySaveState();
            }
            else
            {
                using var salaryLock = TryAcquireSalaryWriteLock(year, month, firmNameFilter);
                if (_sharedOperationLockService != null && salaryLock == null)
                {
                    _setSalaryConflictMessage(BuildSalaryLockMessage(year, month, firmNameFilter));
                    LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmExpenses", BuildSalaryLockMessage(year, month, firmNameFilter));
                    return;
                }

                var filteredExpenses = CloneFirmExpenses(expenses)
                    .Where(expense => string.Equals(expense.FirmName, firmNameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _monthPaymentsStorage!.ReplaceFirmExpensesForFirm(year, month, firmNameFilter, filteredExpenses);
                InvalidatePaymentsCache(year, month);
                _clearSalarySaveState();
            }
        }

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            using var salaryLock = TryAcquireSalaryWriteLock(year, month);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            return SaveAllFirmPaymentsCore(year, month, allEntries, allExpenses);
        }

        public async Task<bool> SaveAllFirmPaymentsAsync(
            int year,
            int month,
            List<SalaryEntry> allEntries,
            List<FirmExpense> allExpenses,
            CancellationToken cancellationToken = default)
        {
            using var salaryLock = await TryAcquireSalaryWriteLockAsync(year, month, null, cancellationToken)
                .ConfigureAwait(false);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            return SaveAllFirmPaymentsCore(year, month, allEntries, allExpenses);
        }

        private bool SaveAllFirmPaymentsCore(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_monthPaymentsStorage == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.Storage", "Month payments storage is not configured.");
                return false;
            }

            try
            {
                // Pass live entry instances (not clones) so InsertSalaryEntry can write the new
                // UpdatedAt back onto the UI/model rows. Cloning left the grid with stale
                // UpdatedAt and the next upsert failed EnsureSalaryEntryNotStale.
                _monthPaymentsStorage.SaveMonthPayments(year, month, allEntries, CloneFirmExpenses(allExpenses));
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

        public int RemoveEmployeeSalaryEntries(string? employeeId, string? originalFolder, string? deletedFolder)
        {
            if (_monthPaymentsStorage == null)
                return 0;

            try
            {
                var removed = _monthPaymentsStorage.RemoveEmployeeSalaryEntries(employeeId, originalFolder, deletedFolder);
                if (removed > 0)
                    InvalidatePaymentsCache();
                return removed;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.RemoveEmployeeSalaryEntries", ex);
                return 0;
            }
        }

        public int RemapEmployeeFolder(string? employeeId, string? fromFolderA, string? fromFolderB, string toFolder)
        {
            if (_monthPaymentsStorage == null || string.IsNullOrWhiteSpace(toFolder))
                return 0;

            try
            {
                var updated = _monthPaymentsStorage.RemapEmployeeFolder(employeeId, fromFolderA, fromFolderB, toFolder);
                if (updated > 0)
                    InvalidatePaymentsCache();
                return updated;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.RemapEmployeeFolder", ex);
                return 0;
            }
        }

        public bool UpsertSalaryEntries(int year, int month, List<SalaryEntry> entries)
            => UpsertSalaryEntriesCore(year, month, entries);

        public Task<bool> UpsertSalaryEntriesAsync(int year, int month, List<SalaryEntry> entries, CancellationToken cancellationToken = default)
            => Task.Run(() => UpsertSalaryEntriesCore(year, month, entries), cancellationToken);

        private bool UpsertSalaryEntriesCore(int year, int month, List<SalaryEntry> entries)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_monthPaymentsStorage == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.UpsertSalaryEntries.Storage", "Month payments storage is not configured.");
                return false;
            }

            if (entries == null || entries.Count == 0)
                return true;

            try
            {
                _monthPaymentsStorage.UpsertSalaryEntries(year, month, entries);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("вже змінено", StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.UpsertSalaryEntries.Conflict", ex.Message);
                _setSalaryConflictMessage(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.UpsertSalaryEntries", ex);
                return false;
            }

            InvalidatePaymentsCache(year, month);
            _clearSalarySaveState();
            return true;
        }

        public bool SaveFirmPayments(int year, int month, string firmName, List<SalaryEntry> entries, List<FirmExpense> expenses)
        {
            using var salaryLock = TryAcquireSalaryWriteLock(year, month, firmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month, firmName);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            return SaveFirmPaymentsCore(year, month, firmName, entries, expenses);
        }

        public async Task<bool> SaveFirmPaymentsAsync(
            int year,
            int month,
            string firmName,
            List<SalaryEntry> entries,
            List<FirmExpense> expenses,
            CancellationToken cancellationToken = default)
        {
            using var salaryLock = await TryAcquireSalaryWriteLockAsync(year, month, firmName, cancellationToken)
                .ConfigureAwait(false);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month, firmName);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            return SaveFirmPaymentsCore(year, month, firmName, entries, expenses);
        }

        private bool SaveFirmPaymentsCore(int year, int month, string firmName, List<SalaryEntry> entries, List<FirmExpense> expenses)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_monthPaymentsStorage == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.Storage", "Month payments storage is not configured.");
                return false;
            }

            try
            {
                // Keep the caller's entry instances so UpdatedAt is written back after save.
                var filteredEntries = entries
                    .Where(entry => string.Equals(entry.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var filteredExpenses = CloneFirmExpenses(expenses)
                    .Where(expense => string.Equals(expense.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                _monthPaymentsStorage.ReplaceFirmPaymentsForFirm(year, month, firmName, filteredEntries, filteredExpenses);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Multiple salary DB files found", StringComparison.Ordinal))
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.SQLite", ex.Message);
                _setSalaryConflictMessage(
                    $"Знайдено кілька файлів виплати за {year:D4}-{month:D2}. Збереження зупинено, щоб не втратити дані. Приберіть дубльований файл і повторіть спробу.");
                return false;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceMonthPaymentsService.SaveFirmPayments", ex);
                return false;
            }

            InvalidatePaymentsCache(year, month);
            _clearSalarySaveState();
            return true;
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month, bool forceReload = false)
        {
            var result = TryLoadAllFirmPayments(year, month, forceReload);
            return (result.entries, result.expenses);
        }

        public bool MonthDataExists(int year, int month)
        {
            EnsureSalaryDbConfigured();
            return _monthPaymentsStorage!.MonthDbExists(year, month);
        }

        public IReadOnlyList<(int year, int month)> GetAvailableMonths()
        {
            if (_monthPaymentsStorage == null)
                return Array.Empty<(int year, int month)>();

            try
            {
                return _monthPaymentsStorage.EnumerateMonthDatabases()
                    .Select(db => (db.year, db.month))
                    .Distinct()
                    .OrderByDescending(item => item.year)
                    .ThenByDescending(item => item.month)
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.GetAvailableMonths", ex.Message);
                return Array.Empty<(int year, int month)>();
            }
        }

        public void UpdateHourlyRateForward(
            string? employeeId,
            string employeeFolder,
            string firmName,
            decimal newRate,
            string fromMonthKey,
            System.Threading.CancellationToken cancellationToken = default)
        {
            EnsureSalaryDbConfigured();
            _monthPaymentsStorage!.UpdateHourlyRateForward(employeeId, employeeFolder, firmName, newRate, fromMonthKey, cancellationToken);
            InvalidatePaymentsCache();
            _clearSalarySaveState();
        }

        public Dictionary<string, Dictionary<string, (decimal netSalary, bool paid)>> GetSavedPaymentsForAllRequests(
            string beforeMonthKey,
            IReadOnlyList<(string requestKey, string firmName, string employeeFolder, string? employeeId)> requests)
        {
            EnsureSalaryDbConfigured();
            return _monthPaymentsStorage!.GetSavedPaymentsForAllRequests(beforeMonthKey, requests);
        }

        public Dictionary<string, (decimal netSalary, bool paid)> GetSavedPaymentsForEmployee(
            string employeeFolder,
            string? employeeId,
            string firmName,
            string beforeMonthKey)
        {
            EnsureSalaryDbConfigured();
            return _monthPaymentsStorage!.GetSavedPaymentsForEmployee(employeeFolder, employeeId, firmName, beforeMonthKey);
        }

        public (bool success, List<SalaryEntry> entries, List<FirmExpense> expenses, string errorMessage) TryLoadAllFirmPayments(
            int year,
            int month,
            bool forceReload = false)
        {
            var cacheKey = (year, month);
            if (!forceReload)
            {
                lock (_paymentsCacheLock)
                {
                    if (_paymentsCache.TryGetValue(cacheKey, out var cached))
                    {
                        return (true, CloneSalaryEntries(cached.entries), CloneFirmExpenses(cached.expenses), string.Empty);
                    }
                }
            }

            if (_monthPaymentsStorage != null)
            {
                try
                {
                    if (_monthPaymentsStorage.MonthDbExists(year, month))
                    {
                        var sqliteResult = _monthPaymentsStorage.LoadMonthPayments(year, month);
                        var sqliteEntries = CloneSalaryEntries(sqliteResult.entries);
                        var sqliteExpenses = CloneFirmExpenses(sqliteResult.expenses);
                        lock (_paymentsCacheLock)
                        {
                            _paymentsCache[cacheKey] = (sqliteEntries, sqliteExpenses);
                        }

                        return (true, CloneSalaryEntries(sqliteEntries), CloneFirmExpenses(sqliteExpenses), string.Empty);
                    }

                    return (true, new List<SalaryEntry>(), new List<FirmExpense>(), string.Empty);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Multiple salary DB files found", StringComparison.Ordinal))
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.LoadAllFirmPayments.SQLite", ex.Message);
                    return (false, new List<SalaryEntry>(), new List<FirmExpense>(), ex.Message);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.LoadAllFirmPayments.SQLite", ex.Message);
                    return (false, new List<SalaryEntry>(), new List<FirmExpense>(), ex.Message);
                }
            }

            return (false, new List<SalaryEntry>(), new List<FirmExpense>(), "Month payments storage is not configured.");
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

        private IDisposable? TryAcquireSalaryWriteLock(int year, int month, string? firmName = null)
            => _sharedOperationLockService?.TryAcquire(BuildSalaryLockName(year, month, firmName), TimeSpan.FromSeconds(15));

        private Task<IDisposable?> TryAcquireSalaryWriteLockAsync(
            int year,
            int month,
            string? firmName = null,
            CancellationToken cancellationToken = default)
        {
            if (_sharedOperationLockService == null)
                return Task.FromResult<IDisposable?>(null);

            return _sharedOperationLockService.TryAcquireAsync(
                BuildSalaryLockName(year, month, firmName),
                TimeSpan.FromSeconds(15),
                cancellationToken);
        }

        private static string BuildSalaryLockName(int year, int month, string? firmName = null)
            => string.IsNullOrWhiteSpace(firmName)
                ? $"salary-{year:D4}-{month:D2}"
                : $"salary-{year:D4}-{month:D2}-{firmName.Trim().ToLowerInvariant()}";

        private static string BuildSalaryLockMessage(int year, int month, string? firmName = null)
            => string.IsNullOrWhiteSpace(firmName)
                ? $"Зарплати за {month:D2}.{year:D4} зараз зберігаються на іншому ПК. Спробуйте ще раз через кілька секунд."
                : $"Фірма {firmName} за {month:D2}.{year:D4} зараз зберігається на іншому ПК. Інші фірми можна редагувати, а цю спробуйте ще раз через кілька секунд.";

        private List<FirmExpense> LoadFirmExpensesForMonth(int year, int month)
        {
            EnsureSalaryDbConfigured();
            try
            {
                lock (_paymentsCacheLock)
                {
                    if (_paymentsCache.TryGetValue((year, month), out var cached))
                        return CloneFirmExpenses(cached.expenses);
                }

                if (_monthPaymentsStorage!.MonthDbExists(year, month))
                    return CloneFirmExpenses(_monthPaymentsStorage.LoadFirmExpensesOnly(year, month));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.LoadFirmExpensesForMonth.SQLite", ex.Message);
            }

            return new List<FirmExpense>();
        }

        private void EnsureSalaryDbConfigured()
        {
            if (_monthPaymentsStorage == null)
                throw new InvalidOperationException("Month payments storage is required for firm expenses storage.");
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

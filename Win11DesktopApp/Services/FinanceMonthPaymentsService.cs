using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            EnsureSalaryDbConfigured();
            using var salaryLock = TryAcquireSalaryWriteLock(expense.Year, expense.Month, expense.FirmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                _setSalaryConflictMessage(BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                LoggingService.LogWarning("FinanceMonthPaymentsService.AddFirmExpense", BuildSalaryLockMessage(expense.Year, expense.Month, expense.FirmName));
                return;
            }

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
                var monthResult = TryLoadAllFirmPayments(year, month);
                if (!monthResult.success)
                {
                    LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmExpenses", monthResult.errorMessage);
                    return;
                }

                var monthEntries = monthResult.entries;
                SaveAllFirmPayments(year, month, monthEntries, CloneFirmExpenses(expenses));
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
                    .Where(expense => string.Equals(expense.FirmName, firmNameFilter, StringComparison.Ordinal))
                    .ToList();
                _monthPaymentsStorage!.ReplaceFirmExpensesForFirm(year, month, firmNameFilter, filteredExpenses);
                InvalidatePaymentsCache(year, month);
                _clearSalarySaveState();
            }
        }

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_monthPaymentsStorage == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.Storage", "Month payments storage is not configured.");
                return false;
            }

            using var salaryLock = TryAcquireSalaryWriteLock(year, month);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            try
            {
                _monthPaymentsStorage.SaveMonthPayments(year, month, CloneSalaryEntries(allEntries), CloneFirmExpenses(allExpenses));
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

        public bool SaveFirmPayments(int year, int month, string firmName, List<SalaryEntry> entries, List<FirmExpense> expenses)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_monthPaymentsStorage == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.Storage", "Month payments storage is not configured.");
                return false;
            }

            using var salaryLock = TryAcquireSalaryWriteLock(year, month, firmName);
            if (_sharedOperationLockService != null && salaryLock == null)
            {
                var message = BuildSalaryLockMessage(year, month, firmName);
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveFirmPayments.Lock", message);
                _setSalaryConflictMessage(message);
                return false;
            }

            try
            {
                var filteredEntries = CloneSalaryEntries(entries)
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
                if (_monthPaymentsStorage!.MonthDbExists(year, month))
                    return LoadAllFirmPayments(year, month).expenses;
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

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
        private readonly SalaryDbService? _salaryDbService;
        private readonly Action _clearLastSaveRecoveryPath;
        private readonly Action _clearSalarySaveState;
        private readonly Action<string> _setSalaryConflictMessage;
        private readonly object _paymentsCacheLock = new();
        private readonly Dictionary<(int year, int month), (List<SalaryEntry> entries, List<FirmExpense> expenses)> _paymentsCache = new();

        public FinanceMonthPaymentsService(
            FolderService folderService,
            SalaryDbService? salaryDbService,
            Action clearLastSaveRecoveryPath,
            Action clearSalarySaveState,
            Action<string> setSalaryConflictMessage)
        {
            _folderService = folderService ?? throw new InvalidOperationException("FolderService is not initialized.");
            _salaryDbService = salaryDbService;
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
            _salaryDbService!.UpsertFirmExpense(expense.Year, expense.Month, CloneFirmExpense(expense));
            InvalidatePaymentsCache(expense.Year, expense.Month);
            _clearSalarySaveState();
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            EnsureSalaryDbConfigured();
            _salaryDbService!.UpsertFirmExpense(updated.Year, updated.Month, CloneFirmExpense(updated));
            InvalidatePaymentsCache(updated.Year, updated.Month);
            _clearSalarySaveState();
        }

        public void RemoveFirmExpense(string expenseId)
        {
            EnsureSalaryDbConfigured();
            foreach (var monthDb in _salaryDbService!.EnumerateMonthDatabases())
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
            if (_salaryDbService!.DeleteFirmExpense(year, month, expenseId))
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
                var filteredExpenses = CloneFirmExpenses(expenses)
                    .Where(expense => string.Equals(expense.FirmName, firmNameFilter, StringComparison.Ordinal))
                    .ToList();
                _salaryDbService!.ReplaceFirmExpensesForFirm(year, month, firmNameFilter, filteredExpenses);
                InvalidatePaymentsCache(year, month);
                _clearSalarySaveState();
            }
        }

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            _clearLastSaveRecoveryPath();

            if (string.IsNullOrEmpty(_folderService.RootPath))
                return false;

            if (_salaryDbService == null)
            {
                LoggingService.LogWarning("FinanceMonthPaymentsService.SaveAllFirmPayments.SQLite", "SalaryDbService is not configured.");
                return false;
            }

            try
            {
                _salaryDbService.SaveMonthPayments(year, month, CloneSalaryEntries(allEntries), CloneFirmExpenses(allExpenses));
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
            var result = TryLoadAllFirmPayments(year, month, forceReload);
            return (result.entries, result.expenses);
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

            return (false, new List<SalaryEntry>(), new List<FirmExpense>(), "SalaryDbService is not configured.");
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
            EnsureSalaryDbConfigured();
            try
            {
                if (_salaryDbService!.MonthDbExists(year, month))
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
            if (_salaryDbService == null)
                throw new InvalidOperationException("SalaryDbService is required for firm expenses storage.");
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

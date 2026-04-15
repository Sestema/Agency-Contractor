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
        private readonly Action _clearLastSaveRecoveryPath;
        private readonly Action _clearSalarySaveState;
        private readonly Action<string> _setSalaryConflictMessage;
        private readonly object _paymentsCacheLock = new();
        private readonly Dictionary<(int year, int month), (List<SalaryEntry> entries, List<FirmExpense> expenses)> _paymentsCache = new();

        public FinanceMonthPaymentsService(
            SalaryDbService? salaryDbService,
            Action clearLastSaveRecoveryPath,
            Action clearSalarySaveState,
            Action<string> setSalaryConflictMessage)
        {
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
            var monthEntries = LoadAllFirmPayments(expense.Year, expense.Month).entries;
            var monthExpenses = LoadFirmExpensesForMonth(expense.Year, expense.Month);
            monthExpenses.Add(CloneFirmExpense(expense));
            SaveAllFirmPayments(expense.Year, expense.Month, monthEntries, monthExpenses);
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            EnsureSalaryDbConfigured();
            var monthEntries = LoadAllFirmPayments(updated.Year, updated.Month).entries;
            var monthExpenses = LoadFirmExpensesForMonth(updated.Year, updated.Month);
            var idx = monthExpenses.FindIndex(e => e.Id == updated.Id);
            if (idx >= 0)
                monthExpenses[idx] = CloneFirmExpense(updated);
            else
                monthExpenses.Add(CloneFirmExpense(updated));

            SaveAllFirmPayments(updated.Year, updated.Month, monthEntries, monthExpenses);
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
            var monthEntries = LoadAllFirmPayments(year, month).entries;
            var monthExpenses = LoadFirmExpensesForMonth(year, month);
            var removedCount = monthExpenses.RemoveAll(e => e.Id == expenseId);
            if (removedCount > 0)
                SaveAllFirmPayments(year, month, monthEntries, monthExpenses);
        }

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month, string? firmNameFilter = null)
        {
            EnsureSalaryDbConfigured();
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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One expense line in the firm expenses block, with its own inline-edit state.</summary>
    public sealed class ExpenseRow : ViewModelBase
    {
        public ExpenseRow(FirmExpense expense) => Expense = expense;

        public FirmExpense Expense { get; }
        public string Id => Expense.Id;
        public string Name => Expense.Name;
        public string AmountText => SalaryViewModel.FormatMoney(Expense.Amount);
        public bool CanEdit => Expense.CanEdit;

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProperty(ref _isEditing, value))
                    OnPropertyChanged(nameof(IsNotEditing));
            }
        }

        public bool IsNotEditing => !IsEditing;

        private string _editName = string.Empty;
        public string EditName { get => _editName; set => SetProperty(ref _editName, value); }

        private string _editAmountText = string.Empty;
        public string EditAmountText { get => _editAmountText; set => SetProperty(ref _editAmountText, value); }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(AmountText));
            OnPropertyChanged(nameof(CanEdit));
        }
    }

    /// <summary>Expenses of one firm (a group is shown only when all firms are listed).</summary>
    public sealed class ExpenseGroup
    {
        public string FirmName { get; init; } = string.Empty;
        public string TotalText { get; init; } = string.Empty;
        public List<ExpenseRow> Rows { get; init; } = new();

        /// <summary>With one expense the firm total would just repeat the row amount.</summary>
        public bool ShowTotal => Rows.Count > 1;
    }

    /// <summary>A name used before for this firm, with the latest amount.</summary>
    public sealed class ExpenseSuggestion
    {
        public string Name { get; init; } = string.Empty;
        public decimal LastAmount { get; init; }
        public string LastAmountText { get; init; } = string.Empty;
    }

    public partial class SalaryViewModel
    {
        private const int SuggestionMonthsBack = 6;
        private List<ExpenseSuggestion> _allExpenseSuggestions = new();

        public ObservableCollection<ExpenseGroup> ExpenseGroups { get; } = new();
        public ObservableCollection<ExpenseSuggestion> ExpenseSuggestions { get; } = new();

        public ICommand AddNewExpenseCommand { get; private set; } = null!;
        public ICommand PickExpenseSuggestionCommand { get; private set; } = null!;
        public ICommand BeginEditExpenseCommand { get; private set; } = null!;
        public ICommand SaveExpenseEditCommand { get; private set; } = null!;
        public ICommand CancelExpenseEditCommand { get; private set; } = null!;

        /// <summary>Show firm group headers only when expenses of all firms are listed.</summary>
        public bool ShowExpenseGroupHeaders => !IsFirmFiltered;

        public string ExpenseFirmTitle => IsFirmFiltered ? _selectedFirmFilter : (L("FinExpenseAllFirms") ?? "All firms");

        public bool HasNoExpenses => FirmExpenses.Count == 0;

        public string TotalExpensesText => FormatMoney(TotalExpenses) + CurrencySuffix;

        // ===== Right panel: section captions and the invoice-like summary =====
        public string TotalNetText => FormatMoney(TotalNet) + CurrencySuffix;
        public string GrandTotalText => FormatMoney(GrandTotal) + CurrencySuffix;

        /// <summary>"25 · 4 651 123 Kč" — how many firms and how much they get in total.</summary>
        public string FirmsSectionSummaryText
            => $"{FirmSummaries.Count} · {FormatMoney(FirmSummaries.Sum(firm => firm.TotalNet))}{CurrencySuffix}";

        /// <summary>"2 · 12 500 Kč" — how many expenses are listed and their sum.</summary>
        public string ExpensesSectionSummaryText => $"{FirmExpenses.Count} · {TotalExpensesText}";

        private string _newExpenseName = string.Empty;
        public string NewExpenseName
        {
            get => _newExpenseName;
            set
            {
                if (SetProperty(ref _newExpenseName, value))
                {
                    FilterExpenseSuggestions();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _newExpenseAmountText = string.Empty;
        public string NewExpenseAmountText
        {
            get => _newExpenseAmountText;
            set => SetProperty(ref _newExpenseAmountText, value);
        }

        private bool _isExpenseSuggestionsOpen;
        public bool IsExpenseSuggestionsOpen
        {
            get => _isExpenseSuggestionsOpen;
            set => SetProperty(ref _isExpenseSuggestionsOpen, value);
        }

        private void InitExpenseCommands()
        {
            AddNewExpenseCommand = new AsyncRelayCommand(async _ => await AddNewExpenseAsync(),
                _ => CanEditSelectedFirmExpenses && !string.IsNullOrWhiteSpace(NewExpenseName));
            PickExpenseSuggestionCommand = new RelayCommand(o => PickExpenseSuggestion(o as ExpenseSuggestion));
            BeginEditExpenseCommand = new RelayCommand(o => BeginEditExpense(o as ExpenseRow), o => o is ExpenseRow row && row.CanEdit);
            SaveExpenseEditCommand = new AsyncRelayCommand(async o => await SaveExpenseEditAsync(o as ExpenseRow));
            CancelExpenseEditCommand = new RelayCommand(o => { if (o is ExpenseRow row) row.IsEditing = false; });
        }

        /// <summary>"172 300" — spaces between thousands, like the rest of the finance page.</summary>
        internal static string FormatMoney(decimal amount)
        {
            var format = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            format.NumberGroupSeparator = " ";
            return amount.ToString(amount == decimal.Truncate(amount) ? "#,0" : "#,0.##", format);
        }

        /// <summary>Accepts "105 000", "105000", "1 250,50", "1250.5"; empty means 0.</summary>
        internal static bool TryParseMoney(string? text, out decimal amount)
        {
            amount = 0m;
            var clean = (text ?? string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty).Replace("Kč", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
            if (clean.Length == 0)
                return true;
            clean = clean.Replace(',', '.');
            return decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount >= 0m;
        }

        /// <summary>Called after the expense list changes: rebuilds rows, groups, totals and suggestions.</summary>
        private void RefreshExpenseView(bool reloadSuggestions)
        {
            ExpenseGroups.Clear();
            var groups = FirmExpenses
                .GroupBy(expense => expense.FirmName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
            foreach (var group in groups)
            {
                ExpenseGroups.Add(new ExpenseGroup
                {
                    FirmName = group.First().FirmName,
                    TotalText = FormatMoney(group.Sum(expense => expense.Amount)),
                    Rows = group.Select(expense => new ExpenseRow(expense)).ToList()
                });
            }

            OnPropertyChanged(nameof(HasNoExpenses));
            OnPropertyChanged(nameof(ShowExpenseGroupHeaders));
            OnPropertyChanged(nameof(ExpenseFirmTitle));
            OnPropertyChanged(nameof(TotalExpensesText));
            OnPropertyChanged(nameof(ExpensesSectionSummaryText));
            if (reloadSuggestions)
                LoadExpenseSuggestions();
        }

        /// <summary>Names this firm used in the last months, newest amount first; for quick typing.</summary>
        private void LoadExpenseSuggestions()
        {
            _allExpenseSuggestions = new List<ExpenseSuggestion>();
            if (IsFirmFiltered)
            {
                var firm = _selectedFirmFilter;
                var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
                var month = new DateTime(_selectedYear, _selectedMonth, 1);
                for (var back = 0; back <= SuggestionMonthsBack; back++)
                {
                    var date = month.AddMonths(-back);
                    List<FirmExpense> expenses;
                    try
                    {
                        expenses = _financeService.GetFirmExpenses(date.Year, date.Month, firm);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("SalaryViewModel.LoadExpenseSuggestions", ex.Message);
                        continue;
                    }

                    foreach (var expense in expenses.Where(expense => !string.IsNullOrWhiteSpace(expense.Name)))
                    {
                        var name = expense.Name.Trim();
                        if (!seen.Add(name))
                            continue;
                        // For the current month show what was used before it, so the amount is a useful hint.
                        var amount = back == 0 ? FindPreviousAmount(firm, name, month) ?? expense.Amount : expense.Amount;
                        _allExpenseSuggestions.Add(new ExpenseSuggestion { Name = name, LastAmount = amount, LastAmountText = FormatMoney(amount) });
                    }
                }
            }

            FilterExpenseSuggestions();
        }

        private decimal? FindPreviousAmount(string firm, string name, DateTime month)
        {
            for (var back = 1; back <= SuggestionMonthsBack; back++)
            {
                var date = month.AddMonths(-back);
                try
                {
                    var match = _financeService.GetFirmExpenses(date.Year, date.Month, firm)
                        .FirstOrDefault(expense => string.Equals(expense.Name?.Trim(), name, StringComparison.CurrentCultureIgnoreCase));
                    if (match != null)
                        return match.Amount;
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("SalaryViewModel.FindPreviousAmount", ex.Message);
                    return null;
                }
            }

            return null;
        }

        private void FilterExpenseSuggestions()
        {
            var query = NewExpenseName?.Trim() ?? string.Empty;
            var items = _allExpenseSuggestions
                .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Where(item => !string.Equals(item.Name, query, StringComparison.CurrentCultureIgnoreCase))
                .Take(6)
                .ToList();

            ExpenseSuggestions.Clear();
            foreach (var item in items)
                ExpenseSuggestions.Add(item);
            IsExpenseSuggestionsOpen = query.Length > 0 && ExpenseSuggestions.Count > 0;
        }

        private void PickExpenseSuggestion(ExpenseSuggestion? suggestion)
        {
            if (suggestion == null)
                return;
            _newExpenseName = suggestion.Name;
            OnPropertyChanged(nameof(NewExpenseName));
            if (string.IsNullOrWhiteSpace(NewExpenseAmountText) && suggestion.LastAmount > 0)
                NewExpenseAmountText = FormatMoney(suggestion.LastAmount);
            IsExpenseSuggestionsOpen = false;
            CommandManager.InvalidateRequerySuggested();
            ExpenseAmountFocusRequested?.Invoke();
        }

        /// <summary>The view moves the keyboard to the amount box when this fires.</summary>
        public event Action? ExpenseAmountFocusRequested;

        private async Task AddNewExpenseAsync()
        {
            var name = NewExpenseName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                return;
            if (!TryParseMoney(NewExpenseAmountText, out var amount))
            {
                StatusMessage = L("FinExpenseBadAmount") ?? "Enter the amount as a number, for example 105 000.";
                return;
            }

            var created = await AddExpenseAsync(name, amount);
            if (!created)
                return;

            _newExpenseName = string.Empty;
            OnPropertyChanged(nameof(NewExpenseName));
            NewExpenseAmountText = string.Empty;
            IsExpenseSuggestionsOpen = false;
            LoadExpenseSuggestions();
        }

        private void BeginEditExpense(ExpenseRow? row)
        {
            if (row == null || !row.CanEdit)
                return;
            foreach (var other in ExpenseGroups.SelectMany(group => group.Rows).Where(other => other.IsEditing && !ReferenceEquals(other, row)))
                other.IsEditing = false;
            row.EditName = row.Name;
            row.EditAmountText = FormatMoney(row.Expense.Amount);
            row.IsEditing = true;
        }

        private async Task SaveExpenseEditAsync(ExpenseRow? row)
        {
            if (row == null || !row.IsEditing)
                return;

            var name = row.EditName?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                StatusMessage = L("FinExpenseNameRequired") ?? "Enter the expense name.";
                return;
            }
            if (!TryParseMoney(row.EditAmountText, out var amount))
            {
                StatusMessage = L("FinExpenseBadAmount") ?? "Enter the amount as a number, for example 105 000.";
                return;
            }

            var expense = row.Expense;
            row.IsEditing = false;
            if (string.Equals(expense.Name, name, StringComparison.Ordinal) && expense.Amount == amount)
                return;
            if (!CanEditSalaryFirm(expense.FirmName, "змінити витрату"))
                return;

            // Change both values with one save instead of one save per field.
            expense.PropertyChanged -= OnExpenseChanged;
            expense.Name = name;
            expense.Amount = amount;
            expense.PropertyChanged += OnExpenseChanged;

            await PersistExpenseChangeAsync(expense);
            RecalcTotals();
            RefreshExpenseView(reloadSuggestions: false);
        }
    }
}

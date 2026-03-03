using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Win11DesktopApp.Models
{
    public enum FieldOperation
    {
        Add,
        Subtract,
        Multiply,
        Divide
    }

    public class CustomSalaryField
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public FieldOperation Operation { get; set; } = FieldOperation.Subtract;
        public string FirmName { get; set; } = string.Empty;
        public int Order { get; set; } = 0;
    }

    public class FirmExpense : INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FirmName { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set { _name = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name))); }
        }

        private decimal _amount;
        public decimal Amount
        {
            get => _amount;
            set { _amount = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Amount))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class FinanceDatabase
    {
        public string Version { get; set; } = "2.0";
        public List<MonthlySalaryReport> Reports { get; set; } = new();
        public List<AdvancePayment> Advances { get; set; } = new();
        public List<AccommodationRecord> Accommodations { get; set; } = new();
        public List<CustomSalaryField> CustomFields { get; set; } = new();
        public List<FirmExpense> FirmExpenses { get; set; } = new();
    }

    public class FirmPaymentData
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public List<SalaryEntry> Entries { get; set; } = new();
        public List<FirmExpense> Expenses { get; set; } = new();
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }

    public class MonthlySalaryReport
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public int Year { get; set; }
        public int Month { get; set; }
        public string CompanyId { get; set; } = string.Empty;
        public string CompanyName { get; set; } = string.Empty;
        public List<SalaryEntry> Entries { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
        public string Notes { get; set; } = string.Empty;

        public string MonthKey => $"{Year:D4}-{Month:D2}";
    }

    public class SalaryEntry : INotifyPropertyChanged
    {
        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;

        private decimal _hoursWorked;
        public decimal HoursWorked
        {
            get => _hoursWorked;
            set { _hoursWorked = value; OnPropertyChanged(nameof(HoursWorked)); OnPropertyChanged(nameof(GrossSalary)); RecalcNet(); }
        }

        private decimal _hourlyRate;
        public decimal HourlyRate
        {
            get => _hourlyRate;
            set { _hourlyRate = value; OnPropertyChanged(nameof(HourlyRate)); OnPropertyChanged(nameof(GrossSalary)); RecalcNet(); }
        }

        public decimal GrossSalary => Math.Round(HoursWorked * HourlyRate, 2);

        private decimal _advance;
        public decimal Advance
        {
            get => _advance;
            set { _advance = value; OnPropertyChanged(nameof(Advance)); RecalcNet(); }
        }

        [Obsolete("Use CustomValues")] public decimal Advances { get; set; }
        [Obsolete("Use CustomValues")] public decimal Surcharge { get; set; }
        [Obsolete("Use CustomValues")] public decimal Accommodation { get; set; }
        [Obsolete("Use CustomValues")] public decimal OtherDeductions { get; set; }

        public Dictionary<string, decimal> CustomValues { get; set; } = new();

        [JsonIgnore]
        public decimal this[string fieldId]
        {
            get => CustomValues.TryGetValue(fieldId, out var v) ? v : 0;
            set
            {
                CustomValues[fieldId] = value;
                OnPropertyChanged("Item[]");
                RecalcNet();
            }
        }

        [JsonIgnore]
        internal List<CustomSalaryField>? FieldDefinitions { get; set; }

        private decimal _netSalary;
        [JsonIgnore]
        public decimal NetSalary
        {
            get => _netSalary;
            set { if (_netSalary != value) { _netSalary = value; OnPropertyChanged(nameof(NetSalary)); } }
        }

        public decimal SavedNetSalary { get; set; }

        public void RecalcNet()
        {
            if (FieldDefinitions == null || FieldDefinitions.Count == 0)
            {
                NetSalary = GrossSalary - Advance;
                return;
            }

            decimal result = GrossSalary - Advance;

            foreach (var f in FieldDefinitions.Where(f => f.Operation is FieldOperation.Multiply or FieldOperation.Divide).OrderBy(f => f.Order))
            {
                if (!CustomValues.TryGetValue(f.Id, out var val) || val == 0) continue;
                result = f.Operation == FieldOperation.Multiply ? result * val : result / val;
            }

            foreach (var f in FieldDefinitions.Where(f => f.Operation is FieldOperation.Add or FieldOperation.Subtract).OrderBy(f => f.Order))
            {
                if (!CustomValues.TryGetValue(f.Id, out var val)) continue;
                result = f.Operation == FieldOperation.Add ? result + val : result - val;
            }

            NetSalary = Math.Round(result, 2);
        }

        private string _status = "pending";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsPaid)); }
        }

        [JsonIgnore]
        public bool IsPaid
        {
            get => _status == "paid";
            set { Status = value ? "paid" : "pending"; }
        }

        private string _note = string.Empty;
        public string Note
        {
            get => _note;
            set { _note = value; OnPropertyChanged(nameof(Note)); }
        }

        public string ColorTag { get; set; } = string.Empty;

        private bool _isFinished;
        [JsonIgnore]
        public bool IsFinished
        {
            get => _isFinished;
            set { _isFinished = value; OnPropertyChanged(nameof(IsFinished)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AdvancePayment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string EmployeeFolder { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string CompanyId { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public decimal Amount { get; set; }
        public string Month { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }

    public class AdvanceDisplayItem
    {
        public AdvancePayment Advance { get; set; } = new();
        public bool IsDeducted { get; set; }
        public string MonthDisplay => Advance.Month;
        public DateTime Date => Advance.Date;
        public decimal Amount => Advance.Amount;
        public string Note => Advance.Note ?? "";
    }

    public class DebtInfoItem
    {
        public string FromMonthKey { get; set; } = "";
        public decimal Amount { get; set; }
        public string FromMonthLabel
        {
            get
            {
                if (FromMonthKey.Length == 7)
                {
                    var parts = FromMonthKey.Split('-');
                    if (parts.Length == 2)
                        return $"{parts[1]}.{parts[0]}";
                }
                return FromMonthKey;
            }
        }
    }

    public class SalaryMonthDisplay
    {
        public SalaryHistoryRecord? Salary { get; set; }
        public List<AdvanceDisplayItem> Advances { get; set; } = new();
        public bool HasSalary => Salary != null;
        public bool HasAdvances => Advances.Count > 0;
        public string MonthKey { get; set; } = "";
        public string MonthLabel => HasSalary ? Salary!.MonthDisplay : FormatMonthKey(MonthKey);

        public decimal CarriedDebt { get; set; }
        public decimal MonthBalance { get; set; }
        public bool HasDebt => MonthBalance < 0;
        public bool HasCarriedDebt => CarriedDebt > 0;
        public bool IsNegativeNet => HasSalary && Salary!.NetSalary < 0;

        private static string FormatMonthKey(string key)
        {
            if (key.Length == 7)
            {
                var parts = key.Split('-');
                if (parts.Length == 2)
                    return $"{parts[1]}.{parts[0]}";
            }
            return key;
        }
    }

    public class AccommodationRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string EmployeeFolder { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string CompanyId { get; set; } = string.Empty;
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Amount { get; set; }
        public string Address { get; set; } = string.Empty;
    }

    public class FirmSalarySummary
    {
        public string FirmName { get; set; } = string.Empty;
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalHours { get; set; }
        public decimal TotalAccommodation { get; set; }
        public decimal TotalAdvances { get; set; }
        public int EmployeeCount { get; set; }
        public int PaidCount { get; set; }
        public bool IsSelected { get; set; }
    }

    public class SalaryHistoryRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime PaidAt { get; set; } = DateTime.Now;
        public int Year { get; set; }
        public int Month { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public decimal HoursWorked { get; set; }
        public decimal HourlyRate { get; set; }
        public decimal GrossSalary { get; set; }
        public decimal Advance { get; set; }
        public decimal NetSalary { get; set; }
        public string Note { get; set; } = string.Empty;
        public Dictionary<string, decimal> CustomValues { get; set; } = new();
        public List<CustomFieldSnapshot> CustomFields { get; set; } = new();

        public string MonthDisplay => $"{Month:D2}.{Year}";
    }

    public class CustomFieldSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public decimal Value { get; set; }
    }
}

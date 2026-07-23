using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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

        private bool _canEdit = true;
        [JsonIgnore]
        public bool CanEdit
        {
            get => _canEdit;
            set { _canEdit = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanEdit))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
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
        // Global, app-wide display preference for how DisplayName formats FullName (last-first vs
        // first-last). This intentionally does NOT touch FullName itself, because FullName is used
        // as an identity/matching/sorting key in many places (search, dictionaries, activity log,
        // advance dialogs, DB storage) - only the separate DisplayName property below is affected.
        public static bool ShowLastNameFirst { get; set; }

        // Global, app-wide display/edit preference for HoursWorked: when false (default), hours are
        // shown/edited rounded to 1 decimal place (legacy behavior). When true, HoursDisplayText shows
        // and accepts the exact stored value with any number of decimal places, without rounding.
        public static bool UseCustomHoursFormat { get; set; }

        public string EmployeeId { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string UpdatedAt { get; set; } = string.Empty;

        /// <summary>
        /// Purely presentational version of <see cref="FullName"/> that reorders the first/last
        /// name parts according to <see cref="ShowLastNameFirst"/>. Splits on the first space only
        /// (same convention as <see cref="EmployeeSummary.SplitFullName"/>), so names with more than
        /// two words keep everything after the first word together as the "second" part.
        /// </summary>
        [JsonIgnore]
        public string DisplayName
        {
            get
            {
                if (!ShowLastNameFirst)
                    return FullName;

                var trimmed = (FullName ?? string.Empty).Trim();
                var spaceIndex = trimmed.IndexOf(' ');
                if (spaceIndex <= 0 || spaceIndex >= trimmed.Length - 1)
                    return FullName;

                var firstPart = trimmed[..spaceIndex];
                var secondPart = trimmed[(spaceIndex + 1)..].Trim();
                return $"{secondPart} {firstPart}";
            }
        }

        /// <summary>Call after <see cref="ShowLastNameFirst"/> changes to refresh bound UI.</summary>
        public void NotifyDisplayNameChanged() => OnPropertyChanged(nameof(DisplayName));

        private decimal _hoursWorked;
        public decimal HoursWorked
        {
            get => _hoursWorked;
            set
            {
                if (_hoursWorked == value)
                    return;

                _hoursWorked = value;
                OnPropertyChanged(nameof(HoursWorked));
                OnPropertyChanged(nameof(HoursDisplayText));
                OnPropertyChanged(nameof(GrossSalary));
                RecalcNet();
            }
        }

        /// <summary>
        /// Editable text representation of <see cref="HoursWorked"/> whose formatting/parsing depends
        /// on <see cref="UseCustomHoursFormat"/>: rounded to 1 decimal in the default mode (legacy
        /// behavior), or exact/arbitrary precision in the custom mode. Accepts both ',' and '.' as the
        /// decimal separator on input regardless of mode.
        /// </summary>
        [JsonIgnore]
        public string HoursDisplayText
        {
            get => UseCustomHoursFormat
                ? _hoursWorked.ToString("0.####################", CultureInfo.CurrentCulture)
                : _hoursWorked.ToString("N1", CultureInfo.CurrentCulture);
            set
            {
                var text = (value ?? string.Empty).Trim().Replace(',', '.');
                if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return;

                HoursWorked = UseCustomHoursFormat ? parsed : Math.Round(parsed, 1);
            }
        }

        /// <summary>Call after <see cref="UseCustomHoursFormat"/> changes to refresh bound UI.</summary>
        public void NotifyHoursFormatChanged() => OnPropertyChanged(nameof(HoursDisplayText));

        private decimal _hourlyRate;
        public decimal HourlyRate
        {
            get => _hourlyRate;
            set
            {
                if (_hourlyRate == value)
                    return;

                _hourlyRate = value;
                OnPropertyChanged(nameof(HourlyRate));
                OnPropertyChanged(nameof(GrossSalary));
                RecalcNet();
            }
        }

        public decimal GrossSalary => Math.Round(HoursWorked * HourlyRate, 2);

        private decimal _advance;
        public decimal Advance
        {
            get => _advance;
            set
            {
                if (_advance == value)
                    return;

                _advance = value;
                OnPropertyChanged(nameof(Advance));
                RecalcNet();
            }
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
                if (CustomValues.TryGetValue(fieldId, out var existing) && existing == value)
                    return;

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
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_status, normalized, StringComparison.Ordinal))
                    return;

                _status = normalized;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsPaid));
            }
        }

        [JsonIgnore]
        public bool IsPaid
        {
            get => _status == "paid";
            set
            {
                var next = value ? "paid" : "pending";
                if (string.Equals(_status, next, StringComparison.Ordinal))
                    return;
                Status = next;
            }
        }

        /// <summary>
        /// True when the row carries real salary data and must not be treated as an empty orphan
        /// (hours, rate, advance, saved net, paid flag, note, or custom values).
        /// </summary>
        [JsonIgnore]
        public bool HasMeaningfulSalaryData
        {
            get
            {
                if (HoursWorked != 0m) return true;
                if (HourlyRate != 0m) return true;
                if (Advance != 0m) return true;
                if (SavedNetSalary != 0m) return true;
                if (IsPaid) return true;
                if (!string.IsNullOrWhiteSpace(Note)) return true;
                if (CustomValues != null && CustomValues.Values.Any(v => v != 0m)) return true;
                return false;
            }
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

        private bool _canEditSalary = true;
        [JsonIgnore]
        public bool CanEditSalary
        {
            get => _canEditSalary;
            set { _canEditSalary = value; OnPropertyChanged(nameof(CanEditSalary)); }
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
        // FirmName is tracked on the display itself so that when one calendar
        // month has payments in two different firms, each firm gets its own
        // card (salary + advances + debt scoped per firm).
        public string FirmName { get; set; } = string.Empty;
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

    public class FirmSalarySummary : INotifyPropertyChanged
    {
        private string _firmName = string.Empty;
        public string FirmName
        {
            get => _firmName;
            set
            {
                if (string.Equals(_firmName, value, StringComparison.Ordinal))
                    return;
                _firmName = value ?? string.Empty;
                OnPropertyChanged(nameof(FirmName));
            }
        }

        private decimal _totalGross;
        public decimal TotalGross
        {
            get => _totalGross;
            set
            {
                if (_totalGross == value)
                    return;
                _totalGross = value;
                OnPropertyChanged(nameof(TotalGross));
            }
        }

        private decimal _totalNet;
        public decimal TotalNet
        {
            get => _totalNet;
            set
            {
                if (_totalNet == value)
                    return;
                _totalNet = value;
                OnPropertyChanged(nameof(TotalNet));
            }
        }

        private decimal _totalHours;
        public decimal TotalHours
        {
            get => _totalHours;
            set
            {
                if (_totalHours == value)
                    return;
                _totalHours = value;
                OnPropertyChanged(nameof(TotalHours));
            }
        }

        private decimal _totalAccommodation;
        public decimal TotalAccommodation
        {
            get => _totalAccommodation;
            set
            {
                if (_totalAccommodation == value)
                    return;
                _totalAccommodation = value;
                OnPropertyChanged(nameof(TotalAccommodation));
            }
        }

        private decimal _totalAdvances;
        public decimal TotalAdvances
        {
            get => _totalAdvances;
            set
            {
                if (_totalAdvances == value)
                    return;
                _totalAdvances = value;
                OnPropertyChanged(nameof(TotalAdvances));
            }
        }

        private int _employeeCount;
        public int EmployeeCount
        {
            get => _employeeCount;
            set
            {
                if (_employeeCount == value)
                    return;
                _employeeCount = value;
                OnPropertyChanged(nameof(EmployeeCount));
            }
        }

        private int _paidCount;
        public int PaidCount
        {
            get => _paidCount;
            set
            {
                if (_paidCount == value)
                    return;
                _paidCount = value;
                OnPropertyChanged(nameof(PaidCount));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

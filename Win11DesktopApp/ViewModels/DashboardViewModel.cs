using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class DashboardItem
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Severity { get; set; } = "";
        public string SeverityColor { get; set; } = "#FF9800";
        public string SeverityLabel { get; set; } = "";
        public string UniqueId { get; set; } = "";
        public string EmployeeFolder { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public int DaysLeft { get; set; }
    }

    public class SalaryMonthSummary
    {
        public string MonthKey { get; set; } = "";
        public string MonthLabel { get; set; } = "";
        public decimal TotalGross { get; set; }
        public decimal TotalNet { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalAdvances { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal GrandTotal => TotalNet + TotalExpenses;
        public int TotalEntries { get; set; }
        public int PaidEntries { get; set; }
        public bool IsFullyPaid => PaidEntries > 0 && PaidEntries == TotalEntries;
        public double PaidRatio => TotalNet > 0 ? Math.Min(1.0, (double)(TotalPaid / TotalNet)) : 0;
        public string StatusColor => IsFullyPaid ? "#4CAF50" : PaidEntries > 0 ? "#FF9800" : "#E53935";
        public string StatusIcon => IsFullyPaid ? "\uE73E" : PaidEntries > 0 ? "\uE121" : "\uE783";
        public string GrossText => $"{TotalGross:N0} CZK";
        public string NetText { get; set; } = "";
        public string ExpenseText { get; set; } = "";
        public string GrandTotalText { get; set; } = "";
        public string PaidText => $"{TotalPaid:N0} / {TotalNet:N0} CZK";
        public string CountText { get; set; } = "";
        public bool HasExpenses => TotalExpenses != 0;
    }

    public class DashboardViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly CompanyService _companyService;
        private readonly EmployeeService _employeeService;
        private readonly TemplateService _templateService;
        private readonly FolderService _folderService;
        private readonly FinanceService _financeService;
        private readonly SalaryMonthDisplayService _salaryMonthDisplayService;
        private readonly AppStatisticsService _appStatisticsService;
        private readonly GeminiApiService _geminiApiService;
        private readonly AppSettingsService _appSettingsService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly MainModuleViewModelFactory _mainModuleViewModelFactory;
        private CancellationTokenSource? _loadCts;
        private Task? _loadTask;
        public ICommand GoBackCommand { get; }
        public ICommand OpenEmployeesCommand { get; }
        public ICommand OpenAllEmployeesCommand { get; }
        public ICommand OpenProblemsCommand { get; }
        public ICommand OpenTemplatesCommand { get; }
        public ICommand OpenEmployeeDetailCommand { get; }
        public ICommand OpenMonthlyMovementDetailsCommand { get; }
        public ICommand CloseMonthlyMovementDetailsCommand { get; }
        public ICommand ToggleMovementPeriodMenuCommand { get; }
        public ICommand SetMovementMonthCountCommand { get; }
        public ICommand OpenMovementEmployeeDetailCommand { get; }
        public ICommand SelectMovementTabCommand { get; }
        public ICommand OpenCompanyDetailsCommand { get; }
        public ICommand CloseCompanyDetailsCommand { get; }
        public ICommand OpenCompanyEmployeesCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand AIReportCommand { get; }
        public ICommand CloseAIReportCommand { get; }
        public ICommand SwapWidgetsCommand { get; }

        private string _slot0 = "expiring";
        public string Slot0 { get => _slot0; set => SetProperty(ref _slot0, value); }

        private string _slot1 = "companies";
        public string Slot1 { get => _slot1; set => SetProperty(ref _slot1, value); }

        private string _slot2 = "salary";
        public string Slot2 { get => _slot2; set => SetProperty(ref _slot2, value); }

        private double _columnRatio = 1.0;
        public double ColumnRatio { get => _columnRatio; set => SetProperty(ref _columnRatio, value); }

        private double _rowRatio = 0.4;
        public double RowRatio { get => _rowRatio; set => SetProperty(ref _rowRatio, value); }

        private bool _isAIReportOpen;
        public bool IsAIReportOpen
        {
            get => _isAIReportOpen;
            set => SetProperty(ref _isAIReportOpen, value);
        }

        private bool _isAIReportLoading;
        public bool IsAIReportLoading
        {
            get => _isAIReportLoading;
            set => SetProperty(ref _isAIReportLoading, value);
        }

        private string _aiReportText = string.Empty;
        public string AIReportText
        {
            get => _aiReportText;
            set => SetProperty(ref _aiReportText, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                    OnPropertyChanged(nameof(IsPageLoadingVisible));
            }
        }

        /// <summary>The page-wide loading box; the movement dialog shows its own ring instead.</summary>
        public bool IsPageLoadingVisible => IsLoading && !IsMonthlyMovementDetailsOpen && !IsDocsDialogOpen;

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private bool _isMonthlyMovementDetailsOpen;
        public bool IsMonthlyMovementDetailsOpen
        {
            get => _isMonthlyMovementDetailsOpen;
            set
            {
                if (SetProperty(ref _isMonthlyMovementDetailsOpen, value))
                    OnPropertyChanged(nameof(IsPageLoadingVisible));
            }
        }

        private bool _isMovementPeriodMenuOpen;
        public bool IsMovementPeriodMenuOpen
        {
            get => _isMovementPeriodMenuOpen;
            set => SetProperty(ref _isMovementPeriodMenuOpen, value);
        }

        private int _movementMonthCount = 1;
        public int MovementMonthCount
        {
            get => _movementMonthCount;
            private set
            {
                if (SetProperty(ref _movementMonthCount, NormalizeMovementMonthCount(value)))
                {
                    OnPropertyChanged(nameof(IsMovementPeriod1));
                    OnPropertyChanged(nameof(IsMovementPeriod2));
                    OnPropertyChanged(nameof(IsMovementPeriod3));
                    OnPropertyChanged(nameof(MovementNetCaption));
                }
            }
        }

        public bool IsMovementPeriod1 => MovementMonthCount == 1;
        public bool IsMovementPeriod2 => MovementMonthCount == 2;
        public bool IsMovementPeriod3 => MovementMonthCount == 3;

        private string _movementPeriodHint = string.Empty;
        public string MovementPeriodHint
        {
            get => _movementPeriodHint;
            private set => SetProperty(ref _movementPeriodHint, value);
        }

        private string _movementAddedColumnTitle = string.Empty;
        public string MovementAddedColumnTitle
        {
            get => _movementAddedColumnTitle;
            private set => SetProperty(ref _movementAddedColumnTitle, value);
        }

        private string _movementArchivedColumnTitle = string.Empty;
        public string MovementArchivedColumnTitle
        {
            get => _movementArchivedColumnTitle;
            private set => SetProperty(ref _movementArchivedColumnTitle, value);
        }

        private string _movementAddedEmptyText = string.Empty;
        public string MovementAddedEmptyText
        {
            get => _movementAddedEmptyText;
            private set => SetProperty(ref _movementAddedEmptyText, value);
        }

        private string _movementArchivedEmptyText = string.Empty;
        public string MovementArchivedEmptyText
        {
            get => _movementArchivedEmptyText;
            private set => SetProperty(ref _movementArchivedEmptyText, value);
        }

        private bool _isCompanyDetailsOpen;
        public bool IsCompanyDetailsOpen
        {
            get => _isCompanyDetailsOpen;
            set => SetProperty(ref _isCompanyDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        private int _totalEmployees;
        public int TotalEmployees
        {
            get => _totalEmployees;
            set
            {
                if (SetProperty(ref _totalEmployees, value))
                {
                    OnPropertyChanged(nameof(MovementActiveCount));
                    NotifyDocsTile();
                }
            }
        }

        // Shared movement dialog (MonthlyMovementDetailsOverlay) also used by the report.
        public int MovementActiveCount => TotalEmployees;
        public bool ShowMovementPeriodSettings => true;
        public string MovementDialogTitle => Res("MainStatMonthlyMovement") ?? "Рух за місяць";

        private int _totalProblems;
        public int TotalProblems
        {
            get => _totalProblems;
            set
            {
                if (SetProperty(ref _totalProblems, value))
                    OnPropertyChanged(nameof(DocsDetailText));
            }
        }

        // ===== "Documents in order" tile: people whose documents are neither expired nor ending within 30 days =====
        private int _docsOkEmployees;
        public int DocsOkEmployees
        {
            get => _docsOkEmployees;
            private set
            {
                if (SetProperty(ref _docsOkEmployees, value))
                    NotifyDocsTile();
            }
        }

        private int _docsExpiredCount;
        public int DocsExpiredCount
        {
            get => _docsExpiredCount;
            private set
            {
                if (SetProperty(ref _docsExpiredCount, value))
                    OnPropertyChanged(nameof(DocsDetailText));
            }
        }

        public int DocsNotOkEmployees => Math.Max(0, TotalEmployees - DocsOkEmployees);

        // ===== "Documents in order" dialog =====
        private bool _isDocsDialogOpen;
        public bool IsDocsDialogOpen
        {
            get => _isDocsDialogOpen;
            set
            {
                if (SetProperty(ref _isDocsDialogOpen, value))
                    OnPropertyChanged(nameof(IsPageLoadingVisible));
            }
        }

        private string _docsDialogTab = "expired";
        public string DocsDialogTab
        {
            get => _docsDialogTab;
            set
            {
                if (SetProperty(ref _docsDialogTab, value))
                {
                    OnPropertyChanged(nameof(IsDocsTabExpired));
                    OnPropertyChanged(nameof(IsDocsTabSoon));
                    OnPropertyChanged(nameof(IsDocsTabOk));
                    OnPropertyChanged(nameof(DocsVisiblePeople));
                    OnPropertyChanged(nameof(DocsVisibleEmptyText));
                }
            }
        }

        public bool IsDocsTabExpired => DocsDialogTab == "expired";
        public bool IsDocsTabSoon => DocsDialogTab == "soon";
        public bool IsDocsTabOk => DocsDialogTab == "ok";

        private ObservableCollection<DocumentStatusPerson> _docsExpiredPeople = new();
        public ObservableCollection<DocumentStatusPerson> DocsExpiredPeople { get => _docsExpiredPeople; private set => SetProperty(ref _docsExpiredPeople, value); }

        private ObservableCollection<DocumentStatusPerson> _docsSoonPeople = new();
        public ObservableCollection<DocumentStatusPerson> DocsSoonPeople { get => _docsSoonPeople; private set => SetProperty(ref _docsSoonPeople, value); }

        private ObservableCollection<DocumentStatusPerson> _docsOkPeople = new();
        public ObservableCollection<DocumentStatusPerson> DocsOkPeople { get => _docsOkPeople; private set => SetProperty(ref _docsOkPeople, value); }

        public ObservableCollection<DocumentStatusPerson> DocsVisiblePeople => DocsDialogTab switch
        {
            "soon" => DocsSoonPeople,
            "ok" => DocsOkPeople,
            _ => DocsExpiredPeople
        };

        public string DocsVisibleEmptyText => DocsDialogTab switch
        {
            "soon" => Res("DashDocsEmptySoon") ?? "Нічого не закінчується найближчі 30 днів.",
            "ok" => Res("DashDocsEmptyOk") ?? "Немає людей з усіма документами в порядку.",
            _ => Res("DashDocsEmptyExpired") ?? "Прострочених документів немає."
        };

        private string _docsTopFirmText = string.Empty;
        public string DocsTopFirmText { get => _docsTopFirmText; private set => SetProperty(ref _docsTopFirmText, value); }

        public string DocsDialogDateText => DateTime.Today.ToString("dd.MM.yyyy");

        public ICommand OpenDocsDialogCommand { get; private set; } = null!;
        public ICommand CloseDocsDialogCommand { get; private set; } = null!;
        public ICommand SelectDocsTabCommand { get; private set; } = null!;
        public ICommand OpenDocsEmployeeCommand { get; private set; } = null!;

        private bool _reopenDocsAfterDetails;

        private void ApplyDocsPeople(List<DocumentStatusPerson> people)
        {
            DocsExpiredPeople = new ObservableCollection<DocumentStatusPerson>(people
                .Where(person => person.Status == "expired")
                .OrderBy(person => person.WorstDays)
                .ThenBy(person => person.FullName, StringComparer.CurrentCultureIgnoreCase));
            DocsSoonPeople = new ObservableCollection<DocumentStatusPerson>(people
                .Where(person => person.Status == "soon")
                .OrderBy(person => person.WorstDays)
                .ThenBy(person => person.FullName, StringComparer.CurrentCultureIgnoreCase));
            DocsOkPeople = new ObservableCollection<DocumentStatusPerson>(people
                .Where(person => person.Status == "ok")
                .OrderBy(person => person.WorstDays)
                .ThenBy(person => person.FullName, StringComparer.CurrentCultureIgnoreCase));
            DocsTopFirmText = BuildTopProblemFirmText(people);
            OnPropertyChanged(nameof(DocsVisiblePeople));
        }

        private void InitDocsDialogCommands()
        {
            OpenDocsDialogCommand = new RelayCommand(_ =>
            {
                DocsDialogTab = DocsExpiredPeople.Count > 0 ? "expired" : DocsSoonPeople.Count > 0 ? "soon" : "ok";
                OnPropertyChanged(nameof(DocsDialogDateText));
                OnPropertyChanged(nameof(DocsVisibleEmptyText));
                IsDocsDialogOpen = true;
            });
            CloseDocsDialogCommand = new RelayCommand(_ => IsDocsDialogOpen = false);
            SelectDocsTabCommand = new RelayCommand(o =>
            {
                var tab = o?.ToString();
                if (tab is "expired" or "soon" or "ok")
                    DocsDialogTab = tab;
            });
            OpenDocsEmployeeCommand = new RelayCommand(o =>
            {
                if (o is not DocumentStatusPerson person || string.IsNullOrEmpty(person.EmployeeFolder))
                    return;

                if (EmployeeDetailsVm != null)
                {
                    EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                    EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                    EmployeeDetailsVm.Cleanup();
                }
                EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(person.FirmName, person.EmployeeFolder, employeeId: person.UniqueId);
                EmployeeDetailsVm.RequestClose += OnDetailsClose;
                EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
                _reopenMovementAfterDetails = false;
                _reopenDocsAfterDetails = true;
                IsDocsDialogOpen = false;
                IsEmployeeDetailsOpen = true;
            });
        }

        /// <summary>Firm with the most people that have a problem, e.g. "Bidfood Dyšina · 8"; empty when nobody has one.</summary>
        internal static string BuildTopProblemFirmText(IEnumerable<DocumentStatusPerson> people)
        {
            var top = people
                .Where(person => person.Status != "ok" && !string.IsNullOrWhiteSpace(person.FirmName))
                .GroupBy(person => person.FirmName, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Firm: group.First().FirmName, Count: group.Count()))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Firm, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
            return top.Count == 0 ? string.Empty : $"{top.Firm} · {top.Count}";
        }

        /// <summary>Uses the same documents and rules as <see cref="AreEmployeeDocumentsOk"/>.</summary>
        internal static DocumentStatusPerson BuildDocumentStatusPerson(EmployeeSummary emp, string firmName)
        {
            var docs = new List<(string Name, string Date)>
            {
                (Res("DetDocPassport") ?? "Паспорт", emp.PassportExpiry)
            };
            if (emp.EmployeeType != "eu_citizen")
                docs.Add((Res("DetDocVisa") ?? "Віза", emp.VisaExpiry));
            docs.Add((Res("DetDocInsurance") ?? "Страховка", emp.InsuranceExpiry));
            if (emp.EmployeeType == "work_permit")
                docs.Add((Res("DetDocWorkPermit") ?? "Дозвіл", emp.WorkPermitExpiry));

            var daysShort = Res("DashMovementDaysShort") ?? "дн.";
            var person = new DocumentStatusPerson
            {
                FullName = emp.FullName ?? string.Empty,
                FirmName = firmName ?? string.Empty,
                EmployeeFolder = emp.EmployeeFolder ?? string.Empty,
                UniqueId = emp.UniqueId ?? string.Empty,
                Initials = BuildInitials(emp.FullName)
            };

            var dated = docs
                .Select(doc => (doc.Name, doc.Date, Severity: DateParsingHelper.GetSeverity(doc.Date), Days: DateParsingHelper.GetDaysRemaining(doc.Date)))
                .Where(doc => doc.Severity != "Unknown")
                .OrderBy(doc => doc.Days)
                .ToList();

            foreach (var doc in dated.Where(doc => doc.Severity is "Expired" or "Critical" or "Warning"))
            {
                var expired = doc.Severity == "Expired";
                person.Chips.Add(new DocumentStatusChip
                {
                    Kind = expired ? "expired" : "soon",
                    Text = expired ? $"{doc.Name} \u2212{-doc.Days} {daysShort}" : $"{doc.Name} {doc.Days} {daysShort}"
                });
            }

            person.WorstDays = dated.Count > 0 ? dated[0].Days : int.MaxValue;
            person.Status = dated.Any(doc => doc.Severity == "Expired") ? "expired"
                : dated.Any(doc => doc.Severity is "Critical" or "Warning") ? "soon"
                : "ok";

            if (person.Status == "ok" && dated.Count > 0)
            {
                var next = dated[0];
                var date = DateParsingHelper.TryParseDate(next.Date);
                person.Chips.Add(new DocumentStatusChip
                {
                    Kind = "next",
                    Text = string.Format(Res("DashDocsNextFmt") ?? "{0} до {1}", next.Name, date?.ToString("dd.MM.yyyy") ?? next.Date)
                });
            }

            return person;
        }

        public string DocsOkPercentText => TotalEmployees <= 0
            ? "—"
            : $"{(int)Math.Round(100.0 * DocsOkEmployees / TotalEmployees)}%";

        public string DocsOkOfText => string.Format(Res("DashDocsOkOfFmt") ?? "{0} з {1}", DocsOkEmployees, TotalEmployees);

        public string DocsDetailText => string.Format(
            Res("DashDocsOkDetailFmt") ?? "{0} прострочено · {1} скоро",
            DocsExpiredCount,
            Math.Max(0, TotalProblems - DocsExpiredCount));

        private void NotifyDocsTile()
        {
            OnPropertyChanged(nameof(DocsNotOkEmployees));
            OnPropertyChanged(nameof(DocsOkPercentText));
            OnPropertyChanged(nameof(DocsOkOfText));
        }

        /// <summary>Same documents and rules as the expiring list: passport, visa (not EU), insurance, work permit (if needed).</summary>
        internal static bool AreEmployeeDocumentsOk(EmployeeSummary emp)
        {
            static bool IsProblem(string date) => DateParsingHelper.GetSeverity(date) is "Expired" or "Critical" or "Warning";

            if (IsProblem(emp.PassportExpiry) || IsProblem(emp.InsuranceExpiry))
                return false;
            if (emp.EmployeeType != "eu_citizen" && IsProblem(emp.VisaExpiry))
                return false;
            if (emp.EmployeeType == "work_permit" && IsProblem(emp.WorkPermitExpiry))
                return false;
            return true;
        }

        private int _totalTemplates;
        public int TotalTemplates
        {
            get => _totalTemplates;
            set => SetProperty(ref _totalTemplates, value);
        }

        private int _totalCompanies;
        public int TotalCompanies
        {
            get => _totalCompanies;
            set => SetProperty(ref _totalCompanies, value);
        }

        private int _monthlyEmployeesAddedCount;
        public int MonthlyEmployeesAddedCount
        {
            get => _monthlyEmployeesAddedCount;
            set
            {
                if (SetProperty(ref _monthlyEmployeesAddedCount, value))
                {
                    OnPropertyChanged(nameof(MonthlyMovementSummaryText));
                    OnPropertyChanged(nameof(MonthlyMovementMax));
                    OnPropertyChanged(nameof(MovementNetText));
                }
            }
        }

        private int _monthlyEmployeesArchivedCount;
        public int MonthlyEmployeesArchivedCount
        {
            get => _monthlyEmployeesArchivedCount;
            set
            {
                if (SetProperty(ref _monthlyEmployeesArchivedCount, value))
                {
                    OnPropertyChanged(nameof(MonthlyMovementSummaryText));
                    OnPropertyChanged(nameof(MonthlyMovementMax));
                    OnPropertyChanged(nameof(MovementNetText));
                }
            }
        }

        public string MonthlyMovementSummaryText => $"+{MonthlyEmployeesAddedCount} / -{MonthlyEmployeesArchivedCount}";
        public int MonthlyMovementMax => Math.Max(1, Math.Max(MonthlyEmployeesAddedCount, MonthlyEmployeesArchivedCount));

        private ObservableCollection<MonthlyMovementItem> _monthlyAddedEmployees = new();
        public ObservableCollection<MonthlyMovementItem> MonthlyAddedEmployees
        {
            get => _monthlyAddedEmployees;
            set
            {
                if (SetProperty(ref _monthlyAddedEmployees, value))
                    OnPropertyChanged(nameof(MovementVisibleEmployees));
            }
        }

        private ObservableCollection<MonthlyMovementItem> _monthlyArchivedEmployees = new();
        public ObservableCollection<MonthlyMovementItem> MonthlyArchivedEmployees
        {
            get => _monthlyArchivedEmployees;
            set
            {
                if (SetProperty(ref _monthlyArchivedEmployees, value))
                    OnPropertyChanged(nameof(MovementVisibleEmployees));
            }
        }

        private bool _reopenMovementAfterDetails;

        private bool _isMovementArchivedTab;
        /// <summary>false = "added" tab, true = "archived" tab in the movement dialog.</summary>
        public bool IsMovementArchivedTab
        {
            get => _isMovementArchivedTab;
            set
            {
                if (SetProperty(ref _isMovementArchivedTab, value))
                {
                    OnPropertyChanged(nameof(IsMovementAddedTab));
                    OnPropertyChanged(nameof(MovementVisibleEmployees));
                    OnPropertyChanged(nameof(MovementVisibleEmptyText));
                }
            }
        }

        public bool IsMovementAddedTab => !IsMovementArchivedTab;

        public ObservableCollection<MonthlyMovementItem> MovementVisibleEmployees
            => IsMovementArchivedTab ? MonthlyArchivedEmployees : MonthlyAddedEmployees;

        public string MovementVisibleEmptyText
            => IsMovementArchivedTab ? MovementArchivedEmptyText : MovementAddedEmptyText;

        public string MovementNetText
        {
            get
            {
                var net = MonthlyEmployeesAddedCount - MonthlyEmployeesArchivedCount;
                return net > 0 ? $"+{net}" : net < 0 ? $"−{-net}" : "0";
            }
        }

        public string MovementNetCaption => MovementMonthCount <= 1
            ? Res("DashMovementNetMonth") ?? "за місяць"
            : string.Format(Res("DashMovementNetPeriodFmt") ?? "за {0} міс.", MovementMonthCount);

        private string _movementTopAddedFirmText = string.Empty;
        public string MovementTopAddedFirmText
        {
            get => _movementTopAddedFirmText;
            private set => SetProperty(ref _movementTopAddedFirmText, value);
        }

        private string _movementTopArchivedFirmText = string.Empty;
        public string MovementTopArchivedFirmText
        {
            get => _movementTopArchivedFirmText;
            private set => SetProperty(ref _movementTopArchivedFirmText, value);
        }

        /// <summary>Firm with the most people in the list, e.g. "DCK Holoubkov +5"; empty for an empty list.</summary>
        internal static string BuildTopFirmText(IEnumerable<MonthlyMovementItem> items, string sign)
        {
            var top = items
                .Where(item => !string.IsNullOrWhiteSpace(item.FirmName))
                .GroupBy(item => item.FirmName, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Firm: group.First().FirmName, Count: group.Count()))
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.Firm, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
            return top.Count == 0 ? string.Empty : $"{top.Firm} {sign}{top.Count}";
        }

        private int _totalEmployeesAllTime;
        public int TotalEmployeesAllTime { get => _totalEmployeesAllTime; set => SetProperty(ref _totalEmployeesAllTime, value); }

        private int _generatedDocumentsCount;
        public int GeneratedDocumentsCount { get => _generatedDocumentsCount; set => SetProperty(ref _generatedDocumentsCount, value); }

        private int _programMinutes;
        public int ProgramMinutes
        {
            get => _programMinutes;
            set
            {
                if (SetProperty(ref _programMinutes, value))
                {
                    OnPropertyChanged(nameof(ProgramTimeText));
                    OnPropertyChanged(nameof(EfficiencyChartMaxMinutes));
                }
            }
        }

        private int _savedMinutes;
        public int SavedMinutes
        {
            get => _savedMinutes;
            set
            {
                if (SetProperty(ref _savedMinutes, value))
                {
                    OnPropertyChanged(nameof(SavedTimeText));
                    OnPropertyChanged(nameof(EfficiencyChartMaxMinutes));
                }
            }
        }

        public int EfficiencyChartMaxMinutes => Math.Max(1, Math.Max(ProgramMinutes, SavedMinutes));
        public string ProgramTimeText => FormatDuration(ProgramMinutes);
        public string SavedTimeText => FormatDuration(SavedMinutes);

        private string _employeeTrend = "";
        public string EmployeeTrend { get => _employeeTrend; set => SetProperty(ref _employeeTrend, value); }

        private string _problemTrend = "";
        public string ProblemTrend { get => _problemTrend; set => SetProperty(ref _problemTrend, value); }

        private string _templateTrend = "";
        public string TemplateTrend { get => _templateTrend; set => SetProperty(ref _templateTrend, value); }

        private ObservableCollection<DashboardItem> _expiringDocs = new();
        public ObservableCollection<DashboardItem> ExpiringDocs
        {
            get => _expiringDocs;
            set => SetProperty(ref _expiringDocs, value);
        }

        private ObservableCollection<SalaryMonthSummary> _salaryMonths = new();
        public ObservableCollection<SalaryMonthSummary> SalaryMonths
        {
            get => _salaryMonths;
            set => SetProperty(ref _salaryMonths, value);
        }

        private string _salaryTotalText = "";
        public string SalaryTotalText
        {
            get => _salaryTotalText;
            set => SetProperty(ref _salaryTotalText, value);
        }

        private ObservableCollection<CompanyStatItem> _companyStats = new();
        public ObservableCollection<CompanyStatItem> CompanyStats
        {
            get => _companyStats;
            set => SetProperty(ref _companyStats, value);
        }

        public DashboardViewModel(
            NavigationService navigationService,
            CompanyService companyService,
            EmployeeService employeeService,
            TemplateService templateService,
            FolderService folderService,
            FinanceService financeService,
            SalaryMonthDisplayService salaryMonthDisplayService,
            AppStatisticsService appStatisticsService,
            GeminiApiService geminiApiService,
            AppSettingsService appSettingsService,
            EmployeeDetailsViewModelFactory employeeDetailsViewModelFactory,
            MainModuleViewModelFactory mainModuleViewModelFactory)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _financeService = financeService ?? throw new ArgumentNullException(nameof(financeService));
            _salaryMonthDisplayService = salaryMonthDisplayService ?? throw new ArgumentNullException(nameof(salaryMonthDisplayService));
            _appStatisticsService = appStatisticsService ?? throw new ArgumentNullException(nameof(appStatisticsService));
            _geminiApiService = geminiApiService ?? throw new ArgumentNullException(nameof(geminiApiService));
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new ArgumentNullException(nameof(employeeDetailsViewModelFactory));
            _mainModuleViewModelFactory = mainModuleViewModelFactory ?? throw new ArgumentNullException(nameof(mainModuleViewModelFactory));

            GoBackCommand = new RelayCommand(_ => _navigationService.NavigateTo<MainViewModel>());
            OpenEmployeesCommand = new RelayCommand(_ =>
            {
                var company = _companyService.SelectedCompany;
                if (company != null)
                    _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateEmployees(company));
            });
            OpenAllEmployeesCommand = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateAllEmployees()));
            OpenProblemsCommand = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateProblems()));
            OpenTemplatesCommand = new RelayCommand(_ =>
            {
                var company = _companyService.SelectedCompany;
                if (company != null)
                    _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateTemplates(company));
            });
            OpenEmployeeDetailCommand = new RelayCommand(o =>
            {
                if (o is DashboardItem item && !string.IsNullOrEmpty(item.EmployeeFolder))
                {
                    if (EmployeeDetailsVm != null)
                    {
                        EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                        EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                        EmployeeDetailsVm.Cleanup();
                    }
                    _reopenMovementAfterDetails = false;
                    _reopenDocsAfterDetails = false;
                    EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(item.CompanyName, item.EmployeeFolder, employeeId: item.UniqueId);
                    EmployeeDetailsVm.RequestClose += OnDetailsClose;
                    EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
                    IsEmployeeDetailsOpen = true;
                }
            });
            InitDocsDialogCommands();
            OpenMonthlyMovementDetailsCommand = new RelayCommand(_ => IsMonthlyMovementDetailsOpen = true);
            SelectMovementTabCommand = new RelayCommand(o => IsMovementArchivedTab = string.Equals(o?.ToString(), "archived", StringComparison.OrdinalIgnoreCase));
            CloseMonthlyMovementDetailsCommand = new RelayCommand(_ =>
            {
                IsMonthlyMovementDetailsOpen = false;
                IsMovementPeriodMenuOpen = false;
            });
            ToggleMovementPeriodMenuCommand = new RelayCommand(_ => IsMovementPeriodMenuOpen = !IsMovementPeriodMenuOpen);
            SetMovementMonthCountCommand = new RelayCommand(o =>
            {
                if (o == null)
                    return;

                var raw = o.ToString();
                if (!int.TryParse(raw, out var count))
                    return;

                ApplyMovementMonthCount(count);
            });
            OpenMovementEmployeeDetailCommand = new RelayCommand(o =>
            {
                if (o is MonthlyMovementItem item && !string.IsNullOrEmpty(item.EmployeeFolder))
                {
                    if (EmployeeDetailsVm != null)
                    {
                        EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                        EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                        EmployeeDetailsVm.Cleanup();
                    }
                    EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(item.FirmName, item.EmployeeFolder, employeeId: item.UniqueId);
                    EmployeeDetailsVm.RequestClose += OnDetailsClose;
                    EmployeeDetailsVm.DataChanged += OnDetailsDataChanged;
                    _reopenMovementAfterDetails = true;
                    _reopenDocsAfterDetails = false;
                    IsMonthlyMovementDetailsOpen = false;
                    IsEmployeeDetailsOpen = true;
                }
            });
            OpenCompanyDetailsCommand = new RelayCommand(_ => IsCompanyDetailsOpen = true);
            CloseCompanyDetailsCommand = new RelayCommand(_ => IsCompanyDetailsOpen = false);
            OpenCompanyEmployeesCommand = new RelayCommand(o =>
            {
                if (o is not CompanyStatItem item || string.IsNullOrWhiteSpace(item.CompanyName))
                    return;

                var company = _companyService.VisibleCompanies
                    .FirstOrDefault(c => string.Equals(c.Name, item.CompanyName, StringComparison.OrdinalIgnoreCase));
                if (company == null)
                    return;

                _companyService.SelectedCompany = company;
                IsCompanyDetailsOpen = false;
                _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateEmployees(company));
            });

            RefreshCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            AIReportCommand = new RelayCommand(_ => GenerateAIReport(), _ => !IsAIReportLoading);
            CloseAIReportCommand = new RelayCommand(_ => IsAIReportOpen = false);
            SwapWidgetsCommand = new RelayCommand(o =>
            {
                if (o is string param)
                {
                    var parts = param.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int from) && int.TryParse(parts[1], out int to) && from != to)
                        SwapSlots(from, to);
                }
            });

            LoadLayout();
            UpdateMovementPeriodTexts();
            _ = LoadDataAsync();
        }

        private void ApplyMovementMonthCount(int count)
        {
            count = NormalizeMovementMonthCount(count);
            if (MovementMonthCount == count)
            {
                IsMovementPeriodMenuOpen = false;
                return;
            }

            MovementMonthCount = count;
            _appSettingsService.Settings.DashMovementMonthCount = count;
            _appSettingsService.SaveSettings();
            IsMovementPeriodMenuOpen = false;
            UpdateMovementPeriodTexts();
            _ = ReloadMovementAsync();
        }

        private int _movementReloadGeneration;

        /// <summary>Reloads after a period switch; the dialog ring stays at least a moment so it is clearly seen.</summary>
        private async Task ReloadMovementAsync()
        {
            var generation = ++_movementReloadGeneration;
            IsMovementLoading = true;
            try
            {
                await Task.WhenAll(LoadDataAsync(), Task.Delay(450));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DashboardViewModel.ReloadMovementAsync", ex.Message);
            }
            finally
            {
                if (generation == _movementReloadGeneration)
                    IsMovementLoading = false;
            }
        }

        private bool _isMovementLoading;
        public bool IsMovementLoading
        {
            get => _isMovementLoading;
            private set => SetProperty(ref _isMovementLoading, value);
        }

        private void UpdateMovementPeriodTexts()
        {
            MovementPeriodHint = BuildMovementPeriodHint(MovementMonthCount, DateTime.Today);
            if (MovementMonthCount <= 1)
            {
                MovementAddedColumnTitle = Res("DashMovementAddedTitle") ?? "Наступили цього місяця";
                MovementArchivedColumnTitle = Res("DashMovementArchivedTitle") ?? "Закінчили цього місяця";
                MovementAddedEmptyText = Res("DashMovementAddedEmpty") ?? string.Empty;
                MovementArchivedEmptyText = Res("DashMovementArchivedEmpty") ?? string.Empty;
            }
            else
            {
                MovementAddedColumnTitle = Res("DashMovementAddedTitlePeriod") ?? "Наступили за період";
                MovementArchivedColumnTitle = Res("DashMovementArchivedTitlePeriod") ?? "Закінчили за період";
                MovementAddedEmptyText = Res("DashMovementAddedEmptyPeriod") ?? string.Empty;
                MovementArchivedEmptyText = Res("DashMovementArchivedEmptyPeriod") ?? string.Empty;
            }
            OnPropertyChanged(nameof(MovementVisibleEmptyText));
        }

        private static int NormalizeMovementMonthCount(int count)
            => count < 1 ? 1 : count > 3 ? 3 : count;

        private static string BuildMovementPeriodHint(int monthCount, DateTime now)
        {
            monthCount = NormalizeMovementMonthCount(monthCount);
            var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(monthCount - 1));
            var end = new DateTime(now.Year, now.Month, 1);
            if (monthCount <= 1)
                return FormatMonthLabel(end.Year, end.Month);

            return $"{FormatMonthLabel(start.Year, start.Month)} – {FormatMonthLabel(end.Year, end.Month)}";
        }

        private async void GenerateAIReport()
        {
            if (!_geminiApiService.IsConfigured)
            {
                AIReportText = Res("AIChatNoModel");
                IsAIReportOpen = true;
                return;
            }

            IsAIReportLoading = true;
            AIReportText = Res("AIChatThinking");
            IsAIReportOpen = true;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Agency dashboard data:");
                sb.AppendLine($"- Total employees: {TotalEmployees}");
                sb.AppendLine($"- Total companies: {TotalCompanies}");
                sb.AppendLine($"- Total problems (expiring docs): {TotalProblems}");
                sb.AppendLine($"- Total templates: {TotalTemplates}");
                sb.AppendLine($"- Employee trend: {EmployeeTrend}");
                sb.AppendLine($"- Problem trend: {ProblemTrend}");

                if (ExpiringDocs.Count > 0)
                {
                    sb.AppendLine("\nExpiring/expired documents:");
                    foreach (var doc in ExpiringDocs.Take(15))
                        sb.AppendLine($"  - {doc.Title}: {doc.Subtitle} [{doc.Severity}] ({doc.SeverityLabel}) — {doc.CompanyName}");
                }

                if (CompanyStats.Count > 0)
                {
                    sb.AppendLine("\nCompany statistics:");
                    foreach (var cs in CompanyStats)
                        sb.AppendLine($"  - {cs.CompanyName}: {cs.EmployeeCount} employees, {cs.ProblemCount} problems, {cs.TemplateCount} templates");
                }

                if (SalaryMonths.Count > 0)
                {
                    sb.AppendLine($"\nSalary overview ({SalaryTotalText}):");
                    foreach (var sm in SalaryMonths)
                        sb.AppendLine($"  - {sm.MonthLabel}: Gross={sm.GrossText}, Net={sm.TotalNet:N0} CZK, Paid={sm.TotalPaid:N0}/{sm.TotalNet:N0} CZK, Workers={sm.TotalEntries}, Fully paid={sm.IsFullyPaid}");
                }

                var uiLang = (_appSettingsService.Settings.LanguageCode ?? "uk") switch
                {
                    "en" => "English",
                    "cs" => "Czech (čeština)",
                    "ru" => "Russian",
                    _ => "Ukrainian"
                };
                var systemPrompt = $@"You are an analytics expert for a Czech employment agency (agentura práce).
Based on the dashboard data, generate a concise analytical report in {uiLang}.

Include:
1. Brief overview of the current state
2. Key problems that need attention (expired documents, unpaid salaries)  
3. Specific recommendations with priorities
4. Risk assessment

Use text section headers like [OVERVIEW], [PROBLEMS], [RECOMMENDATIONS], [RISKS]. NEVER use emoji — they won't render in the app. Be concise but actionable. Max 15-20 lines.";

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                AIReportText = await _geminiApiService.ChatAsync(sb.ToString(), systemPrompt, cts.Token);
            }
            catch (Exception ex)
            {
                AIReportText = $"Error: {ex.Message}";
            }
            finally
            {
                IsAIReportLoading = false;
            }
        }

        private void OnDetailsClose()
        {
            IsEmployeeDetailsOpen = false;
            if (_reopenMovementAfterDetails)
            {
                _reopenMovementAfterDetails = false;
                IsMonthlyMovementDetailsOpen = true;
            }
            else if (_reopenDocsAfterDetails)
            {
                _reopenDocsAfterDetails = false;
                IsDocsDialogOpen = true;
            }
        }
        private void OnDetailsDataChanged(EmployeeDataChangedEventArgs e) => _ = LoadDataAsync();

        private string GetSlot(int index) => index switch { 0 => Slot0, 1 => Slot1, 2 => Slot2, _ => "" };
        private void SetSlot(int index, string value) { switch (index) { case 0: Slot0 = value; break; case 1: Slot1 = value; break; case 2: Slot2 = value; break; } }

        public void SwapSlots(int a, int b)
        {
            var temp = GetSlot(a);
            SetSlot(a, GetSlot(b));
            SetSlot(b, temp);
            SaveLayout();
        }

        private void LoadLayout()
        {
            var s = _appSettingsService.Settings;
            Slot0 = string.IsNullOrEmpty(s.DashSlot0) ? "expiring" : s.DashSlot0;
            Slot1 = string.IsNullOrEmpty(s.DashSlot1) ? "companies" : s.DashSlot1;
            Slot2 = string.IsNullOrEmpty(s.DashSlot2) ? "salary" : s.DashSlot2;
            ColumnRatio = s.DashColumnRatio > 0.1 ? s.DashColumnRatio : 1.0;
            RowRatio = s.DashRowRatio > 0.05 ? s.DashRowRatio : 0.4;
            MovementMonthCount = NormalizeMovementMonthCount(s.DashMovementMonthCount);
        }

        public void SaveLayout()
        {
            var s = _appSettingsService.Settings;
            s.DashSlot0 = Slot0;
            s.DashSlot1 = Slot1;
            s.DashSlot2 = Slot2;
            s.DashColumnRatio = ColumnRatio;
            s.DashRowRatio = RowRatio;
            _appSettingsService.SaveSettings();
        }

        private async Task LoadDataAsync()
        {
            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _loadCts, cts);
            previous?.Cancel();

            var loadTask = LoadDataCoreAsync(cts);
            _loadTask = loadTask;
            await loadTask;
        }

        private async Task LoadDataCoreAsync(CancellationTokenSource cts)
        {
            IsLoading = true;
            var token = cts.Token;
            try
            {
                var data = await Task.Run(GatherDashboardData, token);
                if (token.IsCancellationRequested)
                    return;

                TotalEmployees = data.TotalEmployees;
                TotalProblems = data.TotalProblems;
                DocsOkEmployees = data.DocsOkEmployees;
                DocsExpiredCount = data.DocsExpiredCount;
                ApplyDocsPeople(data.DocsPeople);
                TotalTemplates = data.TotalTemplates;
                TotalCompanies = data.TotalCompanies;
                TotalEmployeesAllTime = data.TotalEmployeesAllTime;
                GeneratedDocumentsCount = data.GeneratedDocumentsCount;
                ProgramMinutes = data.ProgramMinutes;
                SavedMinutes = data.SavedMinutes;
                MonthlyEmployeesAddedCount = data.MonthlyEmployeesAddedCount;
                MonthlyEmployeesArchivedCount = data.MonthlyEmployeesArchivedCount;
                MonthlyAddedEmployees = new ObservableCollection<MonthlyMovementItem>(data.MonthlyAddedEmployees);
                MonthlyArchivedEmployees = new ObservableCollection<MonthlyMovementItem>(data.MonthlyArchivedEmployees);
                MovementTopAddedFirmText = BuildTopFirmText(data.MonthlyAddedEmployees, "+");
                MovementTopArchivedFirmText = BuildTopFirmText(data.MonthlyArchivedEmployees, "−");
                EmployeeTrend = data.EmployeeTrend;
                ProblemTrend = data.ProblemTrend;
                TemplateTrend = data.TemplateTrend;
                ExpiringDocs = new ObservableCollection<DashboardItem>(data.ExpiringDocs);
                SalaryMonths = new ObservableCollection<SalaryMonthSummary>(data.SalaryMonths);
                SalaryTotalText = data.SalaryTotalText;
                CompanyStats = new ObservableCollection<CompanyStatItem>(data.CompanyStats);
            }
            catch (OperationCanceledException)
            {
                // A newer refresh superseded this one.
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DashboardViewModel.LoadData", ex);
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts))
                {
                    _loadCts = null;
                    IsLoading = false;
                }

                cts.Dispose();
            }
        }

        public void Cleanup()
        {
            var loadCts = Interlocked.Exchange(ref _loadCts, null);
            loadCts?.Cancel();

            var loadTask = Interlocked.Exchange(ref _loadTask, null);
            if (loadTask is { IsCompleted: false })
                _ = ObserveCancelledLoadAsync(loadTask);

            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
                EmployeeDetailsVm.DataChanged -= OnDetailsDataChanged;
                EmployeeDetailsVm.Cleanup();
                EmployeeDetailsVm = null;
            }
        }

        private static async Task ObserveCancelledLoadAsync(Task loadTask)
        {
            try
            {
                await loadTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DashboardViewModel.Cleanup.Load", ex);
            }
        }

        private DashboardData GatherDashboardData()
        {
            var result = new DashboardData();
            var companies = _companyService.VisibleCompanies.ToList();

            result.TotalCompanies = companies.Count;
            var now = DateTime.Today;
            var movementMonthCount = MovementMonthCount;
            var visibleFirmNames = new HashSet<string>(companies.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var addedFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTimeEmployeeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allTimeEmployeeFallbacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int expiredCount = 0;
            int criticalCount = 0;
            var archivedEmployees = new List<ArchivedEmployeeSummary>();

            foreach (var company in companies)
            {
                try
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name) ?? new List<EmployeeSummary>();
                    result.TotalEmployees += employees.Count;

                    var templates = _templateService.GetTemplates(company.Name) ?? new List<TemplateEntry>();
                    result.TotalTemplates += templates.Count;

                    int companyProblems = 0;

                    foreach (var emp in employees)
                    {
                        AddEmployeeIdentity(allTimeEmployeeIds, allTimeEmployeeFallbacks, emp.UniqueId, company.Name, emp.FullName, emp.StartDate);

                        if (IsDateInMovementPeriod(emp.StartDate, now, movementMonthCount)
                            && AddEmployeeIdentity(addedIds, addedFallbacks, emp.UniqueId, company.Name, emp.FullName, emp.StartDate))
                        {
                            result.MonthlyAddedEmployees.Add(CreateMovementItem(
                                emp.FullName,
                                company.Name,
                                emp.StartDate,
                                emp.UniqueId,
                                emp.EmployeeFolder,
                                Res("DashMovementStatusAdded"),
                                "#4CAF50"));
                        }

                        CheckExpiry(emp.PassportExpiry, emp.FullName,
                            Res("DetDocPassport"), company.Name, emp.EmployeeFolder, emp.UniqueId, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        if (emp.EmployeeType != "eu_citizen")
                            CheckExpiry(emp.VisaExpiry, emp.FullName,
                                Res("DetDocVisa"), company.Name, emp.EmployeeFolder, emp.UniqueId, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        CheckExpiry(emp.InsuranceExpiry, emp.FullName,
                            Res("DetDocInsurance"), company.Name, emp.EmployeeFolder, emp.UniqueId, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);
                        if (emp.EmployeeType == "work_permit")
                            CheckExpiry(emp.WorkPermitExpiry, emp.FullName,
                                Res("DetDocWorkPermit"), company.Name, emp.EmployeeFolder, emp.UniqueId, result.ExpiringDocs, ref companyProblems, ref expiredCount, ref criticalCount);

                        if (AreEmployeeDocumentsOk(emp))
                            result.DocsOkEmployees++;
                        result.DocsPeople.Add(BuildDocumentStatusPerson(emp, company.Name));
                    }

                    result.TotalProblems += companyProblems;
                    result.CompanyStats.Add(new CompanyStatItem
                    {
                        CompanyName = company.Name,
                        EmployeeCount = employees.Count,
                        ProblemCount = companyProblems,
                        TemplateCount = templates.Count,
                        TotalEmployees = 0
                    });
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("Dashboard.GatherData", ex);
                }
            }

            var archivedThisMonth = 0;
            try
            {
                archivedEmployees = _employeeService.GetArchivedEmployees();
                foreach (var archived in archivedEmployees)
                {
                    if (!string.IsNullOrWhiteSpace(archived.FirmName) && visibleFirmNames.Count > 0 && !visibleFirmNames.Contains(archived.FirmName))
                        continue;

                    AddEmployeeIdentity(allTimeEmployeeIds, allTimeEmployeeFallbacks, archived.UniqueId, archived.FirmName, archived.FullName, archived.StartDate);

                    if (IsDateInMovementPeriod(archived.StartDate, now, movementMonthCount)
                        && AddEmployeeIdentity(addedIds, addedFallbacks, archived.UniqueId, archived.FirmName, archived.FullName, archived.StartDate))
                    {
                        result.MonthlyAddedEmployees.Add(CreateMovementItem(
                            archived.FullName,
                            archived.FirmName,
                            archived.StartDate,
                            archived.UniqueId,
                            archived.EmployeeFolder,
                            Res("DashMovementStatusAdded"),
                            "#4CAF50"));
                    }

                    if (IsDateInMovementPeriod(archived.EndDate, now, movementMonthCount))
                    {
                        archivedThisMonth++;
                        var archivedItem = CreateMovementItem(
                            archived.FullName,
                            archived.FirmName,
                            archived.EndDate,
                            archived.UniqueId,
                            archived.EmployeeFolder,
                            Res("DashMovementStatusArchived"),
                            "#E53935");
                        archivedItem.DetailText = BuildWorkedText(archived.StartDate, archived.EndDate);
                        result.MonthlyArchivedEmployees.Add(archivedItem);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Dashboard.GatherMonthlyMovement", ex.Message);
            }

            foreach (var cs in result.CompanyStats)
                cs.TotalEmployees = result.TotalEmployees;

            result.MonthlyAddedEmployees = result.MonthlyAddedEmployees
                .OrderByDescending(item => item.SortDate ?? DateTime.MinValue)
                .ThenBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            result.MonthlyArchivedEmployees = result.MonthlyArchivedEmployees
                .OrderByDescending(item => item.SortDate ?? DateTime.MinValue)
                .ThenBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            result.MonthlyEmployeesAddedCount = addedIds.Count + addedFallbacks.Count;
            result.MonthlyEmployeesArchivedCount = archivedThisMonth;
            result.TotalEmployeesAllTime = allTimeEmployeeIds.Count + allTimeEmployeeFallbacks.Count;
            result.EmployeeTrend = result.MonthlyEmployeesAddedCount > 0 ? $"+{result.MonthlyEmployeesAddedCount} {Res("DashThisMonth")}" : Res("DashNoChange");
            result.ProblemTrend = expiredCount > 0 ? $"{expiredCount} {Res("DashExpired")}" : Res("DashAllGood");
            result.DocsExpiredCount = expiredCount;
            result.TemplateTrend = $"{result.TotalTemplates} {Res("DashAvailable")}";

            result.ExpiringDocs = result.ExpiringDocs
                .OrderBy(d => d.Severity == "Expired" ? 0 : d.Severity == "Critical" ? 1 : 2)
                .Take(15).ToList();

            try
            {
                var availableMonths = _financeService.GetAvailableSalaryMonths();

                var monthMap = new Dictionary<string, SalaryMonthSummary>();
                decimal grandGross = 0, grandNet = 0, grandPaid = 0;

                foreach (var (year, month) in availableMonths)
                {
                    // Same employee set as Finance salary table (not raw month DB rows).
                    var entries = _salaryMonthDisplayService.BuildDisplayedEntries(year, month);
                    if (entries.Count == 0) continue;

                    var (_, expenses) = _financeService.LoadAllFirmPayments(year, month);

                    var mk = $"{year:D4}-{month:D2}";
                    var summary = new SalaryMonthSummary { MonthKey = mk, MonthLabel = FormatMonthLabel(year, month) };

                    foreach (var entry in entries)
                    {
                        decimal net = entry.SavedNetSalary > 0 ? entry.SavedNetSalary : entry.GrossSalary - entry.Advance;
                        summary.TotalEntries++;
                        summary.TotalGross += entry.GrossSalary;
                        summary.TotalNet += net;
                        summary.TotalAdvances += entry.Advance;
                        if (entry.IsPaid)
                        {
                            summary.PaidEntries++;
                            summary.TotalPaid += net;
                        }
                    }

                    summary.TotalExpenses = expenses.Sum(e => e.Amount);
                    monthMap[mk] = summary;
                }

                foreach (var s in monthMap.Values)
                {
                    s.CountText = $"{s.PaidEntries}/{s.TotalEntries} {Res("DashSalaryWorkers")}";
                    s.NetText = $"{Res("DashSalaryNet")}: {s.TotalNet:N0} CZK";
                    s.ExpenseText = s.HasExpenses ? $"{Res("DashSalaryExpenses")}: {s.TotalExpenses:N0} CZK" : "";
                    s.GrandTotalText = $"{Res("DashSalaryGrandTotal")}: {s.GrandTotal:N0} CZK";
                    grandGross += s.TotalGross;
                    grandNet += s.TotalNet;
                    grandPaid += s.TotalPaid;
                }

                result.SalaryMonths = monthMap.Values
                    .OrderByDescending(s => s.MonthKey)
                    .ToList();

                result.SalaryTotalText = grandGross > 0
                    ? $"{Res("DashSalaryTotal")}: {grandGross:N0} CZK"
                    : "";
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Dashboard.GatherSalary", ex);
            }

            try
            {
                var statistics = _appStatisticsService.GetSnapshot();
                result.TotalEmployeesAllTime = Math.Max(result.TotalEmployeesAllTime, statistics.TotalEmployeesCreated);
                result.GeneratedDocumentsCount = statistics.GeneratedDocumentsCount;
                result.ProgramMinutes = statistics.TotalProgramRunMinutes;
                result.SavedMinutes = CalculateSavedMinutes(result.TotalEmployeesAllTime, result.GeneratedDocumentsCount);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("Dashboard.GatherEfficiency", ex.Message);
                result.SavedMinutes = CalculateSavedMinutes(result.TotalEmployeesAllTime, result.GeneratedDocumentsCount);
            }

            return result;
        }

        private static int CalculateSavedMinutes(int employeesAllTime, int generatedDocuments)
        {
            const int savedPerEmployeeMinutes = 15; // 20 min manually vs 5 min in the app.
            const int savedPerDocumentMinutes = 8;
            return employeesAllTime * savedPerEmployeeMinutes + generatedDocuments * savedPerDocumentMinutes;
        }

        private static string FormatDuration(int totalMinutes)
        {
            if (totalMinutes <= 0)
                return $"0 {Res("DashMinutesShort")}";

            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;

            if (hours <= 0)
                return $"{minutes} {Res("DashMinutesShort")}";

            return minutes > 0
                ? $"{hours} {Res("DashHoursShort")} {minutes} {Res("DashMinutesShort")}"
                : $"{hours} {Res("DashHoursShort")}";
        }

        private static string FormatMonthLabel(int year, int month)
        {
            try
            {
                var dt = new DateTime(year, month, 1);
                var culture = System.Threading.Thread.CurrentThread.CurrentUICulture;
                var name = dt.ToString("MMMM", culture);
                return char.ToUpper(name[0]) + name[1..] + " " + year;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("Dashboard.FormatMonthLabel", ex);
                return $"{month:D2}.{year}";
            }
        }

        private static bool IsDateInMovementPeriod(string dateText, DateTime now, int monthCount)
        {
            var date = DateParsingHelper.TryParseDate(dateText);
            if (date == null)
                return false;

            monthCount = NormalizeMovementMonthCount(monthCount);
            var periodStart = new DateTime(now.Year, now.Month, 1).AddMonths(-(monthCount - 1));
            var periodEnd = new DateTime(now.Year, now.Month, 1).AddMonths(1).AddDays(-1);
            var value = date.Value.Date;
            return value >= periodStart && value <= periodEnd;
        }

        private static bool AddEmployeeIdentity(HashSet<string> ids, HashSet<string> fallbacks, string uniqueId, string firmName, string fullName, string startDate)
        {
            if (!string.IsNullOrWhiteSpace(uniqueId))
            {
                return ids.Add(uniqueId);
            }

            return fallbacks.Add($"{firmName}|{fullName}|{startDate}");
        }

        private static MonthlyMovementItem CreateMovementItem(
            string fullName,
            string firmName,
            string dateText,
            string uniqueId,
            string employeeFolder,
            string statusText,
            string statusColor)
        {
            return new MonthlyMovementItem
            {
                FullName = fullName ?? string.Empty,
                FirmName = firmName ?? string.Empty,
                DateText = FormatMovementDate(dateText),
                SortDate = DateParsingHelper.TryParseDate(dateText),
                Initials = BuildInitials(fullName),
                UniqueId = uniqueId ?? string.Empty,
                EmployeeFolder = employeeFolder ?? string.Empty,
                StatusText = statusText,
                StatusColor = statusColor
            };
        }

        internal static string BuildInitials(string? fullName)
        {
            var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;
            var first = char.ToUpperInvariant(parts[0][0]).ToString();
            return parts.Length == 1 ? first : first + char.ToUpperInvariant(parts[^1][0]);
        }

        /// <summary>"Працював(ла) 4 міс." — how long the person worked; empty when dates are unknown.</summary>
        internal static string BuildWorkedText(string? startDate, string? endDate)
        {
            var start = DateParsingHelper.TryParseDate(startDate ?? string.Empty);
            var end = DateParsingHelper.TryParseDate(endDate ?? string.Empty);
            if (start == null || end == null || end.Value < start.Value)
                return string.Empty;

            var duration = FormatWorkedDuration(start.Value, end.Value,
                Res("DashMovementYearsShort") ?? "р.",
                Res("DashMovementMonthsShort") ?? "міс.",
                Res("DashMovementDaysShort") ?? "дн.");
            return string.Format(Res("DashMovementWorkedFmt") ?? "Працював(ла) {0}", duration);
        }

        /// <summary>"12 дн.", "4 міс.", "1 р. 2 міс."</summary>
        internal static string FormatWorkedDuration(DateTime start, DateTime end, string years, string months, string days)
        {
            var totalMonths = (end.Year - start.Year) * 12 + end.Month - start.Month;
            if (end.Day < start.Day)
                totalMonths--;

            if (totalMonths < 1)
                return $"{Math.Max(1, (end - start).Days + 1)} {days}";

            var y = totalMonths / 12;
            var m = totalMonths % 12;
            if (y == 0)
                return $"{m} {months}";
            return m == 0 ? $"{y} {years}" : $"{y} {years} {m} {months}";
        }

        private static string FormatMovementDate(string dateText)
        {
            var parsed = DateParsingHelper.TryParseDate(dateText);
            return parsed?.ToString("dd.MM.yyyy") ?? (dateText ?? string.Empty);
        }

        private static void CheckExpiry(string dateStr, string empName, string docType,
            string companyName, string folder, string uniqueId, List<DashboardItem> list, ref int problemCount,
            ref int expiredCount, ref int criticalCount)
        {
            var severity = DateParsingHelper.GetSeverity(dateStr);
            if (severity is not ("Expired" or "Critical" or "Warning")) return;
            problemCount++;

            int days = DateParsingHelper.GetDaysRemaining(dateStr);
            string severityLabel, severityColor;
            string icon;

            if (severity == "Expired")
            {
                expiredCount++;
                severityLabel = Res("DashSevExpired");
                severityColor = "#E53935";
                icon = "\uE783";
            }
            else if (severity == "Critical")
            {
                criticalCount++;
                severityLabel = $"{days} {Res("DashSevDays")}";
                severityColor = "#FF5722";
                icon = "\uE121";
            }
            else
            {
                severityLabel = $"{days} {Res("DashSevDays")}";
                severityColor = "#FF9800";
                icon = "\uE121";
            }

            list.Add(new DashboardItem
            {
                Title = empName,
                Subtitle = docType,
                Icon = icon,
                Severity = severity,
                SeverityColor = severityColor,
                SeverityLabel = severityLabel,
                DaysLeft = days,
                UniqueId = uniqueId,
                EmployeeFolder = folder,
                CompanyName = companyName
            });
        }

        private class DashboardData
        {
            public int TotalEmployees;
            public int TotalProblems;
            public int TotalTemplates;
            public int TotalCompanies;
            public int TotalEmployeesAllTime;
            public int GeneratedDocumentsCount;
            public int ProgramMinutes;
            public int SavedMinutes;
            public int MonthlyEmployeesAddedCount;
            public int MonthlyEmployeesArchivedCount;
            public string EmployeeTrend = "";
            public string ProblemTrend = "";
            public int DocsOkEmployees;
            public int DocsExpiredCount;
            public List<DocumentStatusPerson> DocsPeople = new();
            public string TemplateTrend = "";
            public string SalaryTotalText = "";
            public List<MonthlyMovementItem> MonthlyAddedEmployees = new();
            public List<MonthlyMovementItem> MonthlyArchivedEmployees = new();
            public List<DashboardItem> ExpiringDocs = new();
            public List<SalaryMonthSummary> SalaryMonths = new();
            public List<CompanyStatItem> CompanyStats = new();
        }
    }

    public class CompanyStatItem
    {
        public string CompanyName { get; set; } = "";
        public int EmployeeCount { get; set; }
        public int ProblemCount { get; set; }
        public int TemplateCount { get; set; }
        public int TotalEmployees { get; set; }
        public double EmployeeRatio => TotalEmployees > 0 ? (double)EmployeeCount / TotalEmployees : 0;
    }
}

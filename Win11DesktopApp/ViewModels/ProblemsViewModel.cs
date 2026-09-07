using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ProblemsViewModel : ViewModelBase, ICleanable
    {
        private const int LoadProblemsDebounceMs = 150;
        private readonly NavigationService _navigationService;
        private readonly EmployeeService _employeeService;
        private readonly CompanyService _companyService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly Action _onProbDetailsClose;
        private readonly Action<EmployeeDataChangedEventArgs> _onProbDetailsChanged;
        private int _loadProblemsVersion;
        private CancellationTokenSource? _loadProblemsDebounceCts;
        private CancellationTokenSource? _loadProblemsCts;

        public ICommand GoBackCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand IgnoreProblemCommand { get; }
        public ICommand CloseCustomDocumentValidityCommand { get; }
        public ICommand RestoreProblemCommand { get; }
        public ICommand ToggleIgnoredListCommand { get; }
        public ICommand ExportToPdfCommand { get; }
        public ICommand FilterAllCommand { get; }
        public ICommand FilterExpiredCommand { get; }
        public ICommand FilterWarningCommand { get; }
        public ICommand ClearFiltersCommand { get; }

        private List<EmployeeProblemGroup> _allGroups = new();

        private ObservableCollection<EmployeeProblemGroup> _problemGroups = new();
        public ObservableCollection<EmployeeProblemGroup> ProblemGroups
        {
            get => _problemGroups;
            set => SetProperty(ref _problemGroups, value);
        }

        private ObservableCollection<DocumentExpiryInfo> _ignoredProblems = new();
        public ObservableCollection<DocumentExpiryInfo> IgnoredProblems
        {
            get => _ignoredProblems;
            set => SetProperty(ref _ignoredProblems, value);
        }

        private bool _isIgnoredListVisible;
        public bool IsIgnoredListVisible
        {
            get => _isIgnoredListVisible;
            set => SetProperty(ref _isIgnoredListVisible, value);
        }

        private int _totalProblems;
        public int TotalProblems
        {
            get => _totalProblems;
            set { SetProperty(ref _totalProblems, value); OnPropertyChanged(nameof(TotalDisplay)); }
        }

        private int _totalPeople;
        public int TotalPeople
        {
            get => _totalPeople;
            set { SetProperty(ref _totalPeople, value); OnPropertyChanged(nameof(TotalDisplay)); }
        }

        public string TotalDisplay => $"{TotalPeople} / {TotalProblems}";

        private int _expiredCount;
        public int ExpiredCount
        {
            get => _expiredCount;
            set { SetProperty(ref _expiredCount, value); OnPropertyChanged(nameof(ExpiredDisplay)); }
        }

        private int _expiredPeople;
        public int ExpiredPeople
        {
            get => _expiredPeople;
            set { SetProperty(ref _expiredPeople, value); OnPropertyChanged(nameof(ExpiredDisplay)); }
        }

        public string ExpiredDisplay => $"{ExpiredPeople} / {ExpiredCount}";

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set { SetProperty(ref _warningCount, value); OnPropertyChanged(nameof(WarningDisplay)); }
        }

        private int _warningPeople;
        public int WarningPeople
        {
            get => _warningPeople;
            set { SetProperty(ref _warningPeople, value); OnPropertyChanged(nameof(WarningDisplay)); }
        }

        public string WarningDisplay => $"{WarningPeople} / {WarningCount}";

        private int _ignoredCount;
        public int IgnoredCount
        {
            get => _ignoredCount;
            set => SetProperty(ref _ignoredCount, value);
        }

        private bool _hasProblems;
        public bool HasProblems
        {
            get => _hasProblems;
            set => SetProperty(ref _hasProblems, value);
        }

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        private string _activeFilter = "All";
        public string ActiveFilter
        {
            get => _activeFilter;
            set
            {
                if (SetProperty(ref _activeFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterExpired));
                    OnPropertyChanged(nameof(IsFilterWarning));
                    ApplyFilter();
                }
            }
        }

        public bool IsFilterAll => ActiveFilter == "All";
        public bool IsFilterExpired => ActiveFilter == "Expired";
        public bool IsFilterWarning => ActiveFilter == "Warning";

        private string _allFirmsLabel = "";

        private ObservableCollection<string> _firmOptions = new();
        public ObservableCollection<string> FirmOptions
        {
            get => _firmOptions;
            set => SetProperty(ref _firmOptions, value);
        }

        private string _selectedFirm = "";
        public string SelectedFirm
        {
            get => _selectedFirm;
            set
            {
                if (SetProperty(ref _selectedFirm, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ApplyFilter();
                }
            }
        }

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    OnPropertyChanged(nameof(HasActiveFilters));
                    ApplyFilter();
                }
            }
        }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(_searchQuery)
            || (!string.IsNullOrEmpty(_selectedFirm) && _selectedFirm != _allFirmsLabel);

        private bool _isAllClear;
        public bool IsAllClear
        {
            get => _isAllClear;
            set => SetProperty(ref _isAllClear, value);
        }

        private bool _isFilteredEmpty;
        public bool IsFilteredEmpty
        {
            get => _isFilteredEmpty;
            set => SetProperty(ref _isFilteredEmpty, value);
        }

        public string Title => Res("ProbTitle") ?? "Problems — all companies";

        private static new string? Res(string key)
        {
            try { return Application.Current.FindResource(key) as string; }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.Res", ex); return null; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            return fmt != null ? string.Format(fmt, args) : string.Join(" ", args);
        }

        private string? DocRes(string key)
        {
            try { return _documentLocalizationService.Get(key) ?? Res(key); }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.DocRes", ex); return Res(key); }
        }

        private string DocResF(string key, params object[] args)
        {
            var fmt = DocRes(key);
            return fmt != null ? string.Format(fmt, args) : string.Join(" ", args);
        }

        internal const string DocKeyPassport = "Паспорт";
        internal const string DocKeyVisa = "Віза";
        internal const string DocKeyInsurance = "Страховка";
        internal const string DocKeyWorkPermit = "Дозвіл на роботу";

        internal static string LocalizeDocType(string internalKey) => internalKey switch
        {
            DocKeyPassport => Res("ProbDocPassport") ?? internalKey,
            DocKeyVisa => Res("ProbDocVisa") ?? internalKey,
            DocKeyInsurance => Res("ProbDocInsurance") ?? internalKey,
            DocKeyWorkPermit => Res("ProbDocWorkPermit") ?? internalKey,
            _ => internalKey
        };

        private string DocLocalizeDocType(string internalKey) => internalKey switch
        {
            DocKeyPassport => DocRes("ProbDocPassport") ?? internalKey,
            DocKeyVisa => DocRes("ProbDocVisa") ?? internalKey,
            DocKeyInsurance => DocRes("ProbDocInsurance") ?? internalKey,
            DocKeyWorkPermit => DocRes("ProbDocWorkPermit") ?? internalKey,
            _ => internalKey
        };

        public static string DaysRemainingText(int days)
        {
            if (days < 0)
                return ResF("ProbDaysExpired", Math.Abs(days));
            if (days == 0)
                return Res("ProbDaysToday") ?? "expires today";
            return ResF("ProbDaysLeft", days);
        }

        private string DocDaysRemainingText(int days)
        {
            if (days < 0)
                return DocResF("ProbDaysExpired", Math.Abs(days));
            if (days == 0)
                return DocRes("ProbDaysToday") ?? "expires today";
            return DocResF("ProbDaysLeft", days);
        }

        public ProblemsViewModel(
            NavigationService? navigationService = null,
            EmployeeService? employeeService = null,
            CompanyService? companyService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null,
            ActivityLogService? activityLogService = null,
            DocumentLocalizationService? documentLocalizationService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");
            _onProbDetailsClose = () => IsEmployeeDetailsOpen = false;
            _onProbDetailsChanged = _ => LoadProblems();

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());
            OpenEmployeeCommand = new RelayCommand(o =>
            {
                if (o is EmployeeProblemGroup group)
                {
                    if (EmployeeDetailsVm != null)
                    {
                        EmployeeDetailsVm.RequestClose -= _onProbDetailsClose;
                        EmployeeDetailsVm.DataChanged -= _onProbDetailsChanged;
                        EmployeeDetailsVm.Cleanup();
                    }
                    EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(group.FirmName, group.EmployeeFolder, employeeId: group.UniqueId);
                    EmployeeDetailsVm.RequestClose += _onProbDetailsClose;
                    EmployeeDetailsVm.DataChanged += _onProbDetailsChanged;
                    IsEmployeeDetailsOpen = true;
                }
            });

            FilterAllCommand = new RelayCommand(o => ActiveFilter = "All");
            FilterExpiredCommand = new RelayCommand(o => ActiveFilter = "Expired");
            FilterWarningCommand = new RelayCommand(o => ActiveFilter = "Warning");
            ClearFiltersCommand = new RelayCommand(o =>
            {
                _searchQuery = "";
                OnPropertyChanged(nameof(SearchQuery));
                _selectedFirm = _allFirmsLabel;
                OnPropertyChanged(nameof(SelectedFirm));
                OnPropertyChanged(nameof(HasActiveFilters));
                ActiveFilter = "All";
                ApplyFilter();
            });

            IgnoreProblemCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                    ShowIgnoreMenu(info);
            });

            CloseCustomDocumentValidityCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                    CloseCustomDocumentValidity(info);
            });

            RestoreProblemCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                {
                    _employeeService.ClearIgnoredDocument(info.EmployeeFolder, info.DocumentType);
                    LoadProblems();
                }
            });

            ToggleIgnoredListCommand = new RelayCommand(o =>
            {
                IsIgnoredListVisible = !IsIgnoredListVisible;
            });

            ExportToPdfCommand = new RelayCommand(o => ExportToPdf());

            LoadProblems();
        }

        private void ShowIgnoreMenu(DocumentExpiryInfo info)
        {
            var snoozeOptions = new[] { 7, 14, 30, 60, 90 };

            var contextMenu = new System.Windows.Controls.ContextMenu();
            foreach (var days in snoozeOptions)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = ResF("ProbIgnoreForDays", days),
                    Tag = days
                };
                item.Click += (s, e) =>
                {
                    var d = (int)((System.Windows.Controls.MenuItem)s!).Tag;
                    var untilDate = DateTime.Now.AddDays(d).ToString("yyyy-MM-dd");
                    _employeeService.SetIgnoredDocument(info.EmployeeFolder, info.DocumentType, untilDate);
                    LoadProblems();
                };
                contextMenu.Items.Add(item);
            }

            if (System.Windows.Input.Mouse.DirectlyOver is System.Windows.UIElement target)
                contextMenu.PlacementTarget = target;

            contextMenu.IsOpen = true;
        }

        private void CloseCustomDocumentValidity(DocumentExpiryInfo info)
        {
            if (!info.IsCustomDocument)
                return;

            if (!PolicyService.EnsureWriteAllowed("Завершити дійсність документа"))
                return;

            var closed = _employeeService.SetCustomDocumentValidityClosed(
                info.EmployeeFolder,
                info.CustomDocumentId,
                info.DocumentType,
                info.ExpiryDateStr,
                info.CustomDocumentFileName,
                isClosed: true);

            if (!closed)
            {
                LoggingService.LogWarning("ProblemsViewModel.CloseCustomDocumentValidity",
                    $"Custom document not found for {info.EmployeeFolder}: {info.DocumentType} / {info.ExpiryDateStr}");
                return;
            }

            var desc = string.IsNullOrWhiteSpace(info.ExpiryDateStr)
                ? info.DocumentType
                : $"{info.DocumentType} ({info.ExpiryDateStr})";
            _activityLogService.Log("CustomDocValidityClosed", "Document", info.FirmName, info.EmployeeName,
                desc, employeeFolder: info.EmployeeFolder);

            LoadProblems();
        }

        private void LoadProblems()
        {
            var debounceCts = new CancellationTokenSource();
            var previousDebounce = Interlocked.Exchange(ref _loadProblemsDebounceCts, debounceCts);
            previousDebounce?.Cancel();
            _ = DebounceLoadProblemsAsync(debounceCts);
        }

        private async Task DebounceLoadProblemsAsync(CancellationTokenSource debounceCts)
        {
            var token = debounceCts.Token;
            try
            {
                await Task.Delay(LoadProblemsDebounceMs, token);
                if (!ReferenceEquals(Interlocked.CompareExchange(ref _loadProblemsDebounceCts, null, debounceCts), debounceCts))
                    return;

                var loadCts = new CancellationTokenSource();
                var previousLoad = Interlocked.Exchange(ref _loadProblemsCts, loadCts);
                previousLoad?.Cancel();

                var version = Interlocked.Increment(ref _loadProblemsVersion);
                await LoadProblemsAsync(version, loadCts);
            }
            catch (OperationCanceledException)
            {
                // A newer debounce request superseded this one.
            }
            finally
            {
                if (ReferenceEquals(_loadProblemsDebounceCts, debounceCts))
                    _loadProblemsDebounceCts = null;

                debounceCts.Dispose();
            }
        }

        private async Task LoadProblemsAsync(int version, CancellationTokenSource loadCts)
        {
            var token = loadCts.Token;
            try
            {
                var cs = _companyService;
                var allCompanies = cs.Companies;
                if (token.IsCancellationRequested) return;

                var visibleCompanies = allCompanies.Where(c => cs!.IsCompanyVisible(c)).ToList();
                var snapshot = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var activeProblems = new List<DocumentExpiryInfo>();
                    var ignoredProblems = new List<DocumentExpiryInfo>();

                    foreach (var company in visibleCompanies)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var employees = _employeeService.GetEmployeesForFirm(company.Name);
                            foreach (var emp in employees)
                            {
                                token.ThrowIfCancellationRequested();

                                EmployeeExpiryDocuments? employeeData = null;
                                try
                                {
                                    employeeData = _employeeService.LoadEmployeeExpiryDocuments(emp.EmployeeFolder);
                                }
                                catch (Exception ex)
                                {
                                    LoggingService.LogError("ProblemsViewModel.LoadProblems.EmployeeData", ex);
                                }

                                var ignoredDocuments = employeeData?.IgnoredDocuments;
                                var needsPassport = IsProblematic(emp.PassportExpiry);
                                var needsVisa = emp.EmployeeType != "eu_citizen" && IsProblematic(emp.VisaExpiry);
                                var needsInsurance = IsProblematic(emp.InsuranceExpiry);
                                var needsWorkPermit = emp.EmployeeType == "work_permit" && IsProblematic(emp.WorkPermitExpiry);

                                if (needsPassport)
                                    CollectProblem(activeProblems, ignoredProblems, emp, DocKeyPassport, emp.PassportExpiry, ignoredDocuments);
                                if (needsVisa)
                                    CollectProblem(activeProblems, ignoredProblems, emp, DocKeyVisa, emp.VisaExpiry, ignoredDocuments);
                                if (needsInsurance)
                                    CollectProblem(activeProblems, ignoredProblems, emp, DocKeyInsurance, emp.InsuranceExpiry, ignoredDocuments);
                                if (needsWorkPermit)
                                    CollectProblem(activeProblems, ignoredProblems, emp, DocKeyWorkPermit, emp.WorkPermitExpiry, ignoredDocuments);

                                if (employeeData?.CustomDocuments != null)
                                {
                                    foreach (var cd in employeeData.CustomDocuments)
                                    {
                                        token.ThrowIfCancellationRequested();
                                        if (cd.IsHidden || cd.IsClosed || string.IsNullOrWhiteSpace(cd.ExpiryDate))
                                            continue;

                                        CollectProblem(activeProblems, ignoredProblems, emp, cd.Name, cd.ExpiryDate, ignoredDocuments,
                                            customDocumentId: cd.Id, customDocumentFileName: cd.FileName);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogError("ProblemsViewModel.LoadProblems", ex);
                        }
                    }

                    var groups = activeProblems
                        .GroupBy(p => p.EmployeeFolder)
                        .Select(g =>
                        {
                            var first = g.First();
                            return new EmployeeProblemGroup
                            {
                                UniqueId = first.UniqueId,
                                EmployeeName = first.EmployeeName,
                                EmployeeFolder = first.EmployeeFolder,
                                FirmName = first.FirmName,
                                Issues = new ObservableCollection<DocumentExpiryInfo>(
                                    g.OrderBy(x => x.DaysRemaining))
                            };
                        })
                        .OrderBy(g => g.Issues.Min(i => i.DaysRemaining))
                        .ToList();

                    return new ProblemsSnapshot
                    {
                        Groups = groups,
                        IgnoredProblems = ignoredProblems.OrderBy(p => p.EmployeeName).ToList(),
                        ActiveProblemCount = activeProblems.Count,
                        GroupCount = groups.Count,
                        ExpiredCount = activeProblems.Count(p => p.Severity == "Expired" || p.Severity == "Critical"),
                        ExpiredPeople = groups.Count(g => g.Issues.Any(i => i.Severity == "Expired" || i.Severity == "Critical")),
                        WarningCount = activeProblems.Count(p => p.Severity == "Warning"),
                        WarningPeople = groups.Count(g => g.Issues.Any(i => i.Severity == "Warning"))
                    };
                }, token);

                if (token.IsCancellationRequested || version != Volatile.Read(ref _loadProblemsVersion))
                    return;

                _allGroups = snapshot.Groups;
                RebuildFirmOptions();
                IgnoredProblems = new ObservableCollection<DocumentExpiryInfo>(snapshot.IgnoredProblems);
                TotalProblems = snapshot.ActiveProblemCount;
                TotalPeople = snapshot.GroupCount;
                ExpiredCount = snapshot.ExpiredCount;
                ExpiredPeople = snapshot.ExpiredPeople;
                WarningCount = snapshot.WarningCount;
                WarningPeople = snapshot.WarningPeople;
                IgnoredCount = snapshot.IgnoredProblems.Count;
                ApplyFilter();
            }
            catch (OperationCanceledException)
            {
                // A newer problems refresh superseded this one.
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ProblemsViewModel.LoadProblems", ex);
            }
            finally
            {
                if (ReferenceEquals(_loadProblemsCts, loadCts))
                    _loadProblemsCts = null;

                loadCts.Dispose();
            }
        }

        private void RebuildFirmOptions()
        {
            _allFirmsLabel = Res("ProbAllFirms") ?? "All companies";

            var firms = _allGroups
                .Select(g => g.FirmName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(f => f, StringComparer.CurrentCulture)
                .ToList();

            var options = new ObservableCollection<string> { _allFirmsLabel };
            foreach (var f in firms)
                options.Add(f);
            FirmOptions = options;

            if (string.IsNullOrEmpty(_selectedFirm) || !options.Contains(_selectedFirm))
            {
                _selectedFirm = _allFirmsLabel;
                OnPropertyChanged(nameof(SelectedFirm));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        private void ApplyFilter()
        {
            var query = (_searchQuery ?? string.Empty).Trim();
            var hasQuery = query.Length > 0;
            var firmActive = !string.IsNullOrEmpty(_selectedFirm) && _selectedFirm != _allFirmsLabel;

            IEnumerable<EmployeeProblemGroup> baseGroups = _allGroups;

            if (firmActive)
                baseGroups = baseGroups.Where(g => string.Equals(g.FirmName, _selectedFirm, StringComparison.Ordinal));

            if (hasQuery)
            {
                baseGroups = baseGroups.Where(g =>
                    (g.EmployeeName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (g.FirmName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    || g.Issues.Any(i => i.DocumentTypeDisplay?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            // Work out what should be on screen without building any group objects: the severity
            // filters only narrow each employee's issue list, so a source group plus the issues
            // that survive the filter is all the information the sync below needs.
            var targets = new List<(EmployeeProblemGroup Source, List<DocumentExpiryInfo> Issues)>();

            foreach (var g in baseGroups)
            {
                List<DocumentExpiryInfo> issues;

                if (_activeFilter == "Expired")
                    issues = g.Issues.Where(i => i.Severity == "Expired" || i.Severity == "Critical").ToList();
                else if (_activeFilter == "Warning")
                    issues = g.Issues.Where(i => i.Severity == "Warning").ToList();
                else
                    issues = g.Issues.ToList();

                if (issues.Count == 0) continue;

                targets.Add((g, issues));
            }

            SyncProblemGroups(targets);

            HasProblems = targets.Count > 0;
            IsAllClear = _allGroups.Count == 0;
            IsFilteredEmpty = _allGroups.Count > 0 && targets.Count == 0;
        }

        /// <summary>
        /// Brings ProblemGroups to match the target list by editing it in place. Handing the view a
        /// brand new collection would drop every container and send the list back to the top, which
        /// is what used to happen on each keystroke; here a card that did not change is left alone.
        /// Identity is EmployeeFolder because the groups are built by grouping on it, so it is unique
        /// by construction - UniqueId can repeat when it is missing from the source data.
        /// </summary>
        private void SyncProblemGroups(List<(EmployeeProblemGroup Source, List<DocumentExpiryInfo> Issues)> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var (source, issues) = targets[i];

                int existing = -1;
                for (int j = i; j < _problemGroups.Count; j++)
                {
                    if (string.Equals(_problemGroups[j].EmployeeFolder, source.EmployeeFolder, StringComparison.Ordinal))
                    {
                        existing = j;
                        break;
                    }
                }

                if (existing < 0)
                {
                    _problemGroups.Insert(i, new EmployeeProblemGroup
                    {
                        UniqueId = source.UniqueId,
                        EmployeeName = source.EmployeeName,
                        EmployeeFolder = source.EmployeeFolder,
                        FirmName = source.FirmName,
                        Issues = new ObservableCollection<DocumentExpiryInfo>(issues)
                    });
                    continue;
                }

                if (existing != i)
                    _problemGroups.Move(existing, i);

                UpdateDisplayGroup(_problemGroups[i], source, issues);
            }

            // Anything left past the end of the target list no longer passes the filter.
            while (_problemGroups.Count > targets.Count)
                _problemGroups.RemoveAt(_problemGroups.Count - 1);
        }

        private static void UpdateDisplayGroup(EmployeeProblemGroup display, EmployeeProblemGroup source,
            List<DocumentExpiryInfo> issues)
        {
            display.UniqueId = source.UniqueId;
            display.EmployeeName = source.EmployeeName;
            display.FirmName = source.FirmName;

            if (SameIssues(display.Issues, issues)) return;

            display.Issues.Clear();
            foreach (var issue in issues)
                display.Issues.Add(issue);

            // IssueCount and WorstSeverity are computed, so the collection's own notification
            // is not enough for them.
            display.Refresh();
        }

        private static bool SameIssues(IList<DocumentExpiryInfo> current, List<DocumentExpiryInfo> next)
        {
            if (current.Count != next.Count) return false;

            for (int i = 0; i < next.Count; i++)
            {
                if (!ReferenceEquals(current[i], next[i])) return false;
            }

            return true;
        }

        private static bool CollectProblem(List<DocumentExpiryInfo> activeList, List<DocumentExpiryInfo> ignoredList,
            EmployeeSummary emp, string docType, string expiryDate, IReadOnlyDictionary<string, string>? ignoredDocuments,
            string? customDocumentId = null, string? customDocumentFileName = null)
        {
            if (string.IsNullOrWhiteSpace(expiryDate)) return false;

            var severity = DateParsingHelper.GetSeverity(expiryDate);
            if (severity == "Ok" || severity == "Unknown") return false;

            var days = DateParsingHelper.GetDaysRemaining(expiryDate);
            var info = new DocumentExpiryInfo
            {
                UniqueId = emp.UniqueId,
                EmployeeName = emp.FullName,
                EmployeeFolder = emp.EmployeeFolder,
                FirmName = emp.FirmName,
                DocumentType = docType,
                DocumentTypeDisplay = LocalizeDocType(docType),
                ExpiryDateStr = expiryDate,
                ExpiryDate = DateParsingHelper.TryParseDate(expiryDate) ?? DateTime.MinValue,
                DaysRemaining = days,
                Severity = severity,
                CustomDocumentId = customDocumentId ?? string.Empty,
                CustomDocumentFileName = customDocumentFileName ?? string.Empty,
                IsCustomDocument = !string.IsNullOrWhiteSpace(customDocumentId) || !string.IsNullOrWhiteSpace(customDocumentFileName)
            };

            if (TryGetIgnoredUntil(ignoredDocuments, docType, out var untilStr))
            {
                info.IgnoredUntil = untilStr;
                ignoredList.Add(info);
            }
            else
            {
                activeList.Add(info);
            }

            return true;
        }

        /// <summary>
        /// Static helper to count problems across ALL companies (used by MainViewModel for badge).
        /// </summary>
        public static int CountAllProblems(CompanyService companyService, EmployeeService employeeService)
        {
            try
            {
                var cs2 = companyService;
                var companies = cs2.Companies;
                var empService = employeeService;

                var visibleCompanies = companies.Where(c => cs2!.IsCompanyVisible(c)).ToList();
                int count = 0;
                foreach (var company in visibleCompanies)
                {
                    try
                    {
                        var employees = empService.GetEmployeesForFirm(company.Name);
                        foreach (var emp in employees)
                        {
                            var needsPassport = IsProblematic(emp.PassportExpiry);
                            var needsVisa = emp.EmployeeType != "eu_citizen" && IsProblematic(emp.VisaExpiry);
                            var needsInsurance = IsProblematic(emp.InsuranceExpiry);
                            var needsWorkPermit = emp.EmployeeType == "work_permit" && IsProblematic(emp.WorkPermitExpiry);
                            if (!needsPassport && !needsVisa && !needsInsurance && !needsWorkPermit)
                                continue;

                            var ignoredDocuments = empService.LoadEmployeeExpiryDocuments(emp.EmployeeFolder)?.IgnoredDocuments;
                            if (needsPassport && !TryGetIgnoredUntil(ignoredDocuments, DocKeyPassport, out _)) count++;
                            if (needsVisa && !TryGetIgnoredUntil(ignoredDocuments, DocKeyVisa, out _)) count++;
                            if (needsInsurance && !TryGetIgnoredUntil(ignoredDocuments, DocKeyInsurance, out _)) count++;
                            if (needsWorkPermit && !TryGetIgnoredUntil(ignoredDocuments, DocKeyWorkPermit, out _)) count++;
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.CountAllProblems", ex); }
                }
                return count;
            }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.CountAllProblems", ex); return 0; }
        }

        public static int CountProblemsForCompany(EmployerCompany? company, EmployeeService employeeService)
        {
            if (company == null) return 0;

            try
            {
                var employees = employeeService.GetEmployeesForFirm(company.Name);
                var count = 0;

                foreach (var emp in employees)
                {
                    var needsPassport = IsProblematic(emp.PassportExpiry);
                    var needsVisa = emp.EmployeeType != "eu_citizen" && IsProblematic(emp.VisaExpiry);
                    var needsInsurance = IsProblematic(emp.InsuranceExpiry);
                    var needsWorkPermit = emp.EmployeeType == "work_permit" && IsProblematic(emp.WorkPermitExpiry);
                    if (!needsPassport && !needsVisa && !needsInsurance && !needsWorkPermit)
                        continue;

                    var ignoredDocuments = employeeService.LoadEmployeeExpiryDocuments(emp.EmployeeFolder)?.IgnoredDocuments;
                    if (needsPassport && !TryGetIgnoredUntil(ignoredDocuments, DocKeyPassport, out _)) count++;
                    if (needsVisa && !TryGetIgnoredUntil(ignoredDocuments, DocKeyVisa, out _)) count++;
                    if (needsInsurance && !TryGetIgnoredUntil(ignoredDocuments, DocKeyInsurance, out _)) count++;
                    if (needsWorkPermit && !TryGetIgnoredUntil(ignoredDocuments, DocKeyWorkPermit, out _)) count++;
                }

                return count;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ProblemsViewModel.CountProblemsForCompany", ex);
                return 0;
            }
        }

        private static bool IsProblematic(string dateStr)
        {
            var s = DateParsingHelper.GetSeverity(dateStr);
            return s == "Expired" || s == "Critical" || s == "Warning";
        }

        private static bool TryGetIgnoredUntil(IReadOnlyDictionary<string, string>? ignoredDocuments, string docType, out string untilStr)
        {
            untilStr = string.Empty;
            if (ignoredDocuments == null || !ignoredDocuments.TryGetValue(docType, out var candidate) || string.IsNullOrWhiteSpace(candidate))
                return false;

            if (!DateTime.TryParse(candidate, out var until) || DateTime.Now > until)
                return false;

            untilStr = candidate;
            return true;
        }

        public void Cleanup()
        {
            if (EmployeeDetailsVm != null)
            {
                EmployeeDetailsVm.RequestClose -= _onProbDetailsClose;
                EmployeeDetailsVm.DataChanged -= _onProbDetailsChanged;
                EmployeeDetailsVm.Cleanup();
                EmployeeDetailsVm = null;
            }

            var debounceCts = Interlocked.Exchange(ref _loadProblemsDebounceCts, null);
            debounceCts?.Cancel();

            var loadCts = Interlocked.Exchange(ref _loadProblemsCts, null);
            loadCts?.Cancel();
        }

        private sealed class ProblemsSnapshot
        {
            public List<EmployeeProblemGroup> Groups { get; init; } = new();
            public List<DocumentExpiryInfo> IgnoredProblems { get; init; } = new();
            public int ActiveProblemCount { get; init; }
            public int GroupCount { get; init; }
            public int ExpiredCount { get; init; }
            public int ExpiredPeople { get; init; }
            public int WarningCount { get; init; }
            public int WarningPeople { get; init; }
        }

        private bool EnsureExportPathReady(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                if (Directory.Exists(fullPath))
                    throw new IOException("Selected path is a directory.");

                var outputDirectory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                    throw new DirectoryNotFoundException("Export folder not found.");

                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProblemsViewModel.EnsureExportPathReady", ex.Message);
                MessageBox.Show(ResF("ProbPdfExportError", ex.Message), Res("ProbPdfError") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void ExportToPdf()
        {
            try
            {
                var exportFilterLabel = ActiveFilter switch
                {
                    "Expired" => DocRes("ProbPdfExpired") ?? "Expired",
                    "Warning" => DocRes("ProbPdfWarning") ?? "Warning",
                    _ => DocRes("ProbPdfTotal") ?? "Total"
                };

                var exportTitle = ActiveFilter switch
                {
                    "Expired" => $"{DocRes("ProbPdfTitle") ?? "Problems — report"} - {exportFilterLabel}",
                    "Warning" => $"{DocRes("ProbPdfTitle") ?? "Problems — report"} - {exportFilterLabel}",
                    _ => DocRes("ProbPdfTitle") ?? "Problems — report"
                };

                static bool IsExpiredSeverity(DocumentExpiryInfo issue) =>
                    issue.Severity == "Expired" || issue.Severity == "Critical";

                var exportGroups = ProblemGroups.ToList();
                var exportIgnored = ActiveFilter switch
                {
                    "Expired" => IgnoredProblems.Where(IsExpiredSeverity).ToList(),
                    "Warning" => IgnoredProblems.Where(i => i.Severity == "Warning").ToList(),
                    _ => IgnoredProblems.ToList()
                };

                var exportTotalPeople = exportGroups.Count;
                var exportTotalProblems = exportGroups.Sum(g => g.Issues.Count);
                var exportExpiredCount = exportGroups.Sum(g => g.Issues.Count(IsExpiredSeverity));
                var exportExpiredPeople = exportGroups.Count(g => g.Issues.Any(IsExpiredSeverity));
                var exportWarningCount = exportGroups.Sum(g => g.Issues.Count(i => i.Severity == "Warning"));
                var exportWarningPeople = exportGroups.Count(g => g.Issues.Any(i => i.Severity == "Warning"));
                var exportIgnoredCount = exportIgnored.Count;

                var dialog = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"{exportTitle}_{DateTime.Now:yyyy-MM-dd}.pdf",
                    Title = Res("ProbPdfSaveTitle") ?? "Save problems report"
                };

                if (dialog.ShowDialog() != true) return;
                if (!EnsureExportPathReady(dialog.FileName)) return;

                var doc = new PdfDocument();
                doc.Info.Title = exportTitle;

                var page = doc.AddPage();
                page.Width = XUnit.FromMillimeter(210);
                page.Height = XUnit.FromMillimeter(297);
                var gfx = XGraphics.FromPdfPage(page);

                var fontTitle = new XFont("Arial", 18, XFontStyleEx.Bold);
                var fontSubtitle = new XFont("Arial", 11);
                var fontEmployeeName = new XFont("Arial", 13, XFontStyleEx.Bold);
                var fontFirm = new XFont("Arial", 10);
                var fontIssue = new XFont("Arial", 10);
                var fontIssueBold = new XFont("Arial", 10, XFontStyleEx.Bold);
                var fontBadge = new XFont("Arial", 9, XFontStyleEx.Bold);

                var topLeftFormat = new XStringFormat
                {
                    Alignment = XStringAlignment.Near,
                    LineAlignment = XLineAlignment.Near
                };

                double marginLeft = 40;
                double marginRight = 40;
                double pageWidth = page.Width.Point;
                double pageHeight = page.Height.Point;
                double contentWidth = pageWidth - marginLeft - marginRight;
                double y = 40;

                var brushBlack = XBrushes.Black;
                var brushGray = new XSolidBrush(XColor.FromArgb(128, 128, 128));
                var brushRed = new XSolidBrush(XColor.FromArgb(198, 40, 40));
                var brushOrange = new XSolidBrush(XColor.FromArgb(239, 108, 0));
                var brushRedBg = new XSolidBrush(XColor.FromArgb(255, 235, 238));
                var brushOrangeBg = new XSolidBrush(XColor.FromArgb(255, 243, 224));
                var penBorder = new XPen(XColor.FromArgb(200, 200, 200), 0.5);

                // --- Title ---
                gfx.DrawString(exportTitle, fontTitle, brushBlack,
                    new XPoint(marginLeft, y), topLeftFormat);
                y += 26;

                gfx.DrawString(DocResF("ProbPdfDate", DateTime.Now.ToString("dd.MM.yyyy")), fontSubtitle, brushGray,
                    new XPoint(marginLeft, y), topLeftFormat);
                y += 20;

                // --- Stats bar ---
                var fontStat = new XFont("Arial", 11, XFontStyleEx.Bold);
                var fontStatLabel = new XFont("Arial", 9);
                double statBoxH = 38;
                double statBoxW = contentWidth / 4 - 4;
                double statY = y;
                var statPenBorder = new XPen(XColor.FromArgb(180, 180, 180), 0.8);

                var statItems = new[]
                {
                    (Label: DocRes("ProbPdfExpired") ?? "Expired", Value: $"{exportExpiredPeople} / {exportExpiredCount}", Bg: XColor.FromArgb(255, 235, 238), Fg: XColor.FromArgb(198, 40, 40)),
                    (Label: DocRes("ProbPdfWarning") ?? "Warning", Value: $"{exportWarningPeople} / {exportWarningCount}", Bg: XColor.FromArgb(255, 243, 224), Fg: XColor.FromArgb(239, 108, 0)),
                    (Label: DocRes("ProbPdfTotal") ?? "Total", Value: $"{exportTotalPeople} / {exportTotalProblems}", Bg: XColor.FromArgb(240, 240, 240), Fg: XColor.FromArgb(50, 50, 50)),
                    (Label: DocRes("ProbPdfIgnored") ?? "Ignored", Value: $"{exportIgnoredCount}", Bg: XColor.FromArgb(232, 245, 253), Fg: XColor.FromArgb(30, 90, 150)),
                };

                for (int si = 0; si < statItems.Length; si++)
                {
                    double sx = marginLeft + si * (statBoxW + 5);
                    var rect = new XRect(sx, statY, statBoxW, statBoxH);
                    gfx.DrawRoundedRectangle(statPenBorder, new XSolidBrush(statItems[si].Bg), rect, new XSize(4, 4));
                    var valBrush = new XSolidBrush(statItems[si].Fg);
                    gfx.DrawString(statItems[si].Value, fontStat, valBrush, new XPoint(sx + 8, statY + 10), topLeftFormat);
                    gfx.DrawString(statItems[si].Label, fontStatLabel, valBrush, new XPoint(sx + 8, statY + 24), topLeftFormat);
                }

                y = statY + statBoxH + 12;

                // --- Employee groups ---
                foreach (var group in exportGroups)
                {
                    double blockHeight = 24 + group.Issues.Count * 20 + 14;
                    if (y + blockHeight > pageHeight - 40)
                    {
                        page = doc.AddPage();
                        page.Width = XUnit.FromMillimeter(210);
                        page.Height = XUnit.FromMillimeter(297);
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                    }

                    // Employee name
                    gfx.DrawString(group.EmployeeName, fontEmployeeName, brushBlack,
                        new XPoint(marginLeft, y), topLeftFormat);
                    y += 17;

                    // Firm name
                    gfx.DrawString(group.FirmName, fontFirm, brushGray,
                        new XPoint(marginLeft + 12, y), topLeftFormat);
                    y += 16;

                    // Issues
                    foreach (var issue in group.Issues)
                    {
                        if (y > pageHeight - 40)
                        {
                            page = doc.AddPage();
                            page.Width = XUnit.FromMillimeter(210);
                            page.Height = XUnit.FromMillimeter(297);
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }

                        var isExpired = issue.Severity == "Expired" || issue.Severity == "Critical";
                        var severityBrush = isExpired ? brushRed : brushOrange;
                        var bgBrush = isExpired ? brushRedBg : brushOrangeBg;

                        // Background stripe
                        gfx.DrawRectangle(bgBrush, marginLeft + 8, y - 1, contentWidth - 8, 16);

                        // Severity dot
                        var dotBrush = isExpired ? brushRed : brushOrange;
                        gfx.DrawEllipse(dotBrush, marginLeft + 14, y + 2, 6, 6);

                        // Document type
                        gfx.DrawString(DocLocalizeDocType(issue.DocumentType), fontIssueBold, brushBlack,
                            new XPoint(marginLeft + 26, y), topLeftFormat);

                        // Expiry date
                        gfx.DrawString(issue.ExpiryDateStr, fontIssue, brushGray,
                            new XPoint(marginLeft + 120, y), topLeftFormat);

                        // Status text
                        var statusText = DocDaysRemainingText(issue.DaysRemaining);
                        gfx.DrawString(statusText, fontBadge, severityBrush,
                            new XPoint(marginLeft + 220, y), topLeftFormat);

                        y += 18;
                    }

                    // Separator after group
                    y += 4;
                    gfx.DrawLine(penBorder, marginLeft, y, marginLeft + contentWidth, y);
                    y += 10;
                }

                // --- Ignored section ---
                if (exportIgnored.Count > 0)
                {
                    if (y + 40 > pageHeight - 40)
                    {
                        page = doc.AddPage();
                        page.Width = XUnit.FromMillimeter(210);
                        page.Height = XUnit.FromMillimeter(297);
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                    }

                    var brushIndigo = new XSolidBrush(XColor.FromArgb(57, 73, 171));

                    y += 6;
                    gfx.DrawString(DocResF("ProbPdfIgnoredSection", exportIgnored.Count), fontEmployeeName, brushIndigo,
                        new XPoint(marginLeft, y), topLeftFormat);
                    y += 20;

                    foreach (var ign in exportIgnored)
                    {
                        if (y > pageHeight - 40)
                        {
                            page = doc.AddPage();
                            page.Width = XUnit.FromMillimeter(210);
                            page.Height = XUnit.FromMillimeter(297);
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }

                        gfx.DrawString($"{ign.EmployeeName} — {DocLocalizeDocType(ign.DocumentType)}", fontIssue, brushBlack,
                            new XPoint(marginLeft + 12, y), topLeftFormat);
                        var untilPrefix = DocRes("ProbUntil") ?? "until ";
                        gfx.DrawString($"{untilPrefix}{ign.IgnoredUntil}", fontFirm, brushIndigo,
                            new XPoint(marginLeft + 300, y), topLeftFormat);
                        y += 16;
                    }
                }

                doc.Save(dialog.FileName);
                _activityLogService.Log("ExportPdf", "Export", "", "",
                    $"Експортовано звіт проблем → PDF",
                    details: BuildProblemsExportDetails(dialog.FileName, exportFilterLabel, exportTotalPeople, exportTotalProblems, exportIgnoredCount));
                DocumentGenerationService.OpenFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ResF("ProbPdfExportError", ex.Message), Res("ProbPdfError") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildProblemsExportDetails(string outputPath, string filterLabel, int peopleCount, int problemCount, int ignoredCount)
        {
            return $"Фільтр: {filterLabel}; Людей: {peopleCount}; Проблем: {problemCount}; " +
                   $"Ігнорованих: {ignoredCount}; Файл: {Path.GetFileName(outputPath)}";
        }
    }
}

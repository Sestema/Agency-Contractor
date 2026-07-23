using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Win11DesktopApp.Invoices.Services;
using System.Collections.ObjectModel;
using Win11DesktopApp.Invoices.ViewModels;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using System.Linq;
using System.Globalization;

namespace Win11DesktopApp.ViewModels
{
    public enum CompanySortMode
    {
        Default,
        Name,
        Agency,
        EmployeeCount
    }

    public class SearchResultItem
    {
        public string Category { get; set; } = "";
        public string CategoryIcon { get; set; } = "\uE721";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string EmployeeFolder { get; set; } = "";
        public string CategoryColor { get; set; } = "#2196F3";
    }

    public class MenuCardItem : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string TitleKey { get; set; } = "";
        public string Title => Res(TitleKey);
        public string IconKey { get; set; } = "";
        public string GradientStart { get; set; } = "#667EEA";
        public string GradientEnd { get; set; } = "#764BA2";
        public ICommand? Command { get; set; }

        private int _badgeCount;
        public int BadgeCount
        {
            get => _badgeCount;
            set => SetProperty(ref _badgeCount, value);
        }
    }

    public class MainViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly CompanyService _companyService;
        private readonly EmployeeService _employeeService;
        private readonly TemplateService _templateService;
        private readonly CandidateService _candidateService;
        private readonly GeminiApiService _geminiApiService;
        private readonly AppSettingsService _appSettingsService;
        private readonly InvoiceViewModelFactory _invoiceViewModelFactory;
        private readonly MainModuleViewModelFactory _mainModuleViewModelFactory;
        private readonly AddCompanyViewModelFactory _addCompanyViewModelFactory;
        private readonly AppNotificationService _notificationService;
        private readonly WeatherService _weatherService;
        private readonly DispatcherTimer _clockTimer;

        public ICommand GoToSettingsCommand { get; }
        public ICommand ToggleNotificationsCommand { get; }
        public ICommand MarkNotificationsReadCommand { get; }
        public ICommand ClearNotificationsCommand { get; }
        public ICommand ToggleDrawerCommand { get; }
        public ICommand OpenAddCompanyDialogCommand { get; }
        public ICommand CloseAddCompanyDialogCommand { get; }
        public ICommand EditCompanyCommand { get; }
        public ICommand MoveCompanyUpCommand { get; }
        public ICommand MoveCompanyDownCommand { get; }
        public ICommand ButtonCommand { get; }
        public ICommand NavigateToSearchResultCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand AISearchCommand { get; }
        public ICommand SetCompanySortCommand { get; }
        public ICommand ToggleCompanySortMenuCommand { get; }

        private ObservableCollection<MenuCardItem> _menuCards = new();
        public ObservableCollection<MenuCardItem> MenuCards
        {
            get => _menuCards;
            set => SetProperty(ref _menuCards, value);
        }

        private int _problemsCount;
        public int ProblemsCount
        {
            get => _problemsCount;
            set
            {
                if (SetProperty(ref _problemsCount, value))
                {
                    var card = _menuCards.FirstOrDefault(c => c.Id == "problems");
                    if (card != null) card.BadgeCount = value;
                }
            }
        }

        private string _greetingText = string.Empty;
        public string GreetingText
        {
            get => _greetingText;
            set => SetProperty(ref _greetingText, value);
        }

        private string _greetingGlyph = "\uE706";
        public string GreetingGlyph
        {
            get => _greetingGlyph;
            set => SetProperty(ref _greetingGlyph, value);
        }

        private bool _hasWeather;
        public bool HasWeather
        {
            get => _hasWeather;
            set => SetProperty(ref _hasWeather, value);
        }

        private string _weatherTempText = string.Empty;
        public string WeatherTempText
        {
            get => _weatherTempText;
            set => SetProperty(ref _weatherTempText, value);
        }

        private string _weatherDescription = string.Empty;
        public string WeatherDescription
        {
            get => _weatherDescription;
            set => SetProperty(ref _weatherDescription, value);
        }

        private string _weatherIconKey = "IconWeatherCloud";
        public string WeatherIconKey
        {
            get => _weatherIconKey;
            set => SetProperty(ref _weatherIconKey, value);
        }

        private string _weatherCity = string.Empty;
        public string WeatherCity
        {
            get => _weatherCity;
            set => SetProperty(ref _weatherCity, value);
        }

        private string _currentDateText = string.Empty;
        public string CurrentDateText
        {
            get => _currentDateText;
            set => SetProperty(ref _currentDateText, value);
        }

        private string _currentTimeText = string.Empty;
        public string CurrentTimeText
        {
            get => _currentTimeText;
            set => SetProperty(ref _currentTimeText, value);
        }

        private string _currentTimeZoneText = string.Empty;
        public string CurrentTimeZoneText
        {
            get => _currentTimeZoneText;
            set => SetProperty(ref _currentTimeZoneText, value);
        }

        private int _visibleCompaniesCount;
        public int VisibleCompaniesCount
        {
            get => _visibleCompaniesCount;
            set => SetProperty(ref _visibleCompaniesCount, value);
        }

        private int _selectedCompanyEmployeesCount;
        public int SelectedCompanyEmployeesCount
        {
            get => _selectedCompanyEmployeesCount;
            set => SetProperty(ref _selectedCompanyEmployeesCount, value);
        }

        private int _selectedCompanyTemplatesCount;
        public int SelectedCompanyTemplatesCount
        {
            get => _selectedCompanyTemplatesCount;
            set => SetProperty(ref _selectedCompanyTemplatesCount, value);
        }

        private int _selectedCompanyProblemsCount;
        public int SelectedCompanyProblemsCount
        {
            get => _selectedCompanyProblemsCount;
            set => SetProperty(ref _selectedCompanyProblemsCount, value);
        }

        public string SelectedCompanyDisplayName => SelectedCompany?.Name ?? Res("MainNoCompanySelected");
        public string SelectedCompanyIcoText => string.IsNullOrWhiteSpace(SelectedCompany?.ICO)
            ? Res("MainNoCompanyMeta")
            : $"ICO {SelectedCompany!.ICO}";
        public string SelectedCompanyAgencyText =>
            string.IsNullOrWhiteSpace(SelectedCompany?.Agency?.Name)
                ? Res("MainNoCompanyMeta")
                : SelectedCompany!.Agency.Name;
        public string SelectedCompanySummaryText => HasSelectedCompany
            ? string.Format(
                Res("MainCompanySummaryFormat"),
                SelectedCompanyEmployeesCount,
                SelectedCompanyTemplatesCount)
            : Res("MainSelectCompany");

        private string _searchQuery = "";
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                    DebounceSearch();
            }
        }

        private ObservableCollection<SearchResultItem> _searchResults = new();
        public ObservableCollection<SearchResultItem> SearchResults
        {
            get => _searchResults;
            set => SetProperty(ref _searchResults, value);
        }

        private bool _isSearchOpen;
        public bool IsSearchOpen
        {
            get => _isSearchOpen;
            set => SetProperty(ref _isSearchOpen, value);
        }

        private bool _hasNoSearchResults;
        public bool HasNoSearchResults
        {
            get => _hasNoSearchResults;
            set => SetProperty(ref _hasNoSearchResults, value);
        }

        private bool _isAISearching;
        public bool IsAISearching
        {
            get => _isAISearching;
            set => SetProperty(ref _isAISearching, value);
        }

        private bool _isNotificationCenterOpen;
        public bool IsNotificationCenterOpen
        {
            get => _isNotificationCenterOpen;
            set => SetProperty(ref _isNotificationCenterOpen, value);
        }

        public ObservableCollection<AppNotificationItem> Notifications => _notificationService.Notifications;
        public int UnreadNotificationsCount => _notificationService.UnreadCount;
        public bool HasUnreadNotifications => _notificationService.HasUnread;
        public bool HasNotifications => Notifications.Count > 0;

        private CancellationTokenSource? _searchCts;
        private Timer? _searchDebounce;

        private bool _isDrawerOpen;
        public bool IsDrawerOpen
        {
            get => _isDrawerOpen;
            set => SetProperty(ref _isDrawerOpen, value);
        }

        private bool _isAddCompanyDialogOpen;
        public bool IsAddCompanyDialogOpen
        {
            get => _isAddCompanyDialogOpen;
            set => SetProperty(ref _isAddCompanyDialogOpen, value);
        }

        private AddCompanyViewModel? _addCompanyVm;
        public AddCompanyViewModel? AddCompanyVm
        {
            get => _addCompanyVm;
            set => SetProperty(ref _addCompanyVm, value);
        }

        public ObservableCollection<EmployerCompany> Companies => _companyService.Companies;

        private ObservableCollection<EmployerCompany> _visibleCompanies = new();
        public ObservableCollection<EmployerCompany> VisibleCompanies => _visibleCompanies;

        private string _companySearchQuery = string.Empty;
        public string CompanySearchQuery
        {
            get => _companySearchQuery;
            set
            {
                if (SetProperty(ref _companySearchQuery, value))
                    RefreshVisibleCompanies();
            }
        }

        private CompanySortMode _companySortMode = CompanySortMode.Default;
        public CompanySortMode CompanySortMode
        {
            get => _companySortMode;
            set
            {
                if (SetProperty(ref _companySortMode, value))
                    RefreshVisibleCompanies();
            }
        }

        private bool _isCompanySortMenuOpen;
        public bool IsCompanySortMenuOpen
        {
            get => _isCompanySortMenuOpen;
            set => SetProperty(ref _isCompanySortMenuOpen, value);
        }

        // Employee counts per firm, used only for CompanySortMode.EmployeeCount.
        // Populated in the background so sorting never has to hit the DB/disk on the UI thread.
        private readonly Dictionary<string, int> _companyEmployeeCounts = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _visibleCompaniesCts;
        private CancellationTokenSource? _employeeCountsCts;

        // Used once, synchronously, right in the constructor - before the View binds to
        // VisibleCompanies/SelectedCompany. If this were async (like the regular refresh below),
        // the freshly-created ListBox would briefly see an empty ItemsSource, fail to match the
        // already-selected company for its two-way SelectedItem binding, and push null back into
        // SelectedCompany - wiping out the user's selection every time MainViewModel is recreated
        // (e.g. every time you navigate back from another module, since it's registered AddTransient).
        private void RefreshVisibleCompaniesSync()
        {
            var cs = _companyService;
            if (cs == null) return;

            _visibleCompaniesCts?.Cancel();

            var companiesSnapshot = cs.Companies.ToList();
            var query = CompanySearchQuery?.Trim() ?? string.Empty;
            Dictionary<string, int> countsSnapshot;
            lock (_companyEmployeeCounts)
                countsSnapshot = new Dictionary<string, int>(_companyEmployeeCounts, StringComparer.OrdinalIgnoreCase);

            var ordered = ComputeVisibleCompanies(companiesSnapshot, cs, query, CompanySortMode, countsSnapshot);

            // Capture the selection BEFORE mutating the collection: if the ListBox is already
            // bound (live), Clear() can momentarily push a null SelectedItem back through the
            // two-way binding, wiping SelectedCompany before we get to check it below.
            var previouslySelected = SelectedCompany;

            _visibleCompanies.Clear();
            foreach (var company in ordered)
                _visibleCompanies.Add(company);

            VisibleCompaniesCount = _visibleCompanies.Count;

            RestoreSelection(previouslySelected);
        }

        // Regular refresh path (search typing, sort change, visibility change, etc.) - computed off
        // the UI thread so it never blocks on a slow per-firm DB lookup while the View is already live.
        private void RefreshVisibleCompanies()
        {
            var cs = _companyService;
            if (cs == null) return;

            _visibleCompaniesCts?.Cancel();
            var cts = new CancellationTokenSource();
            _visibleCompaniesCts = cts;
            var token = cts.Token;

            var companiesSnapshot = cs.Companies.ToList();
            var query = CompanySearchQuery?.Trim() ?? string.Empty;
            var sortMode = CompanySortMode;
            Dictionary<string, int> countsSnapshot;
            lock (_companyEmployeeCounts)
                countsSnapshot = new Dictionary<string, int>(_companyEmployeeCounts, StringComparer.OrdinalIgnoreCase);

            _ = Task.Run(() =>
            {
                try
                {
                    var ordered = ComputeVisibleCompanies(companiesSnapshot, cs, query, sortMode, countsSnapshot);

                    if (token.IsCancellationRequested) return;

                    _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        // Same reasoning as in RefreshVisibleCompaniesSync: capture before Clear(),
                        // since the live ListBox's two-way SelectedItem binding can null out
                        // SelectedCompany the instant the collection is cleared.
                        var previouslySelected = SelectedCompany;

                        _visibleCompanies.Clear();
                        foreach (var company in ordered)
                            _visibleCompanies.Add(company);

                        VisibleCompaniesCount = _visibleCompanies.Count;

                        RestoreSelection(previouslySelected);
                    }));
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("MainViewModel.RefreshVisibleCompanies", ex);
                }
            }, token);
        }

        // Re-asserts the selection after VisibleCompanies has been repopulated. Always writes
        // through the setter (even if it looks unchanged) so it overwrites any null the
        // ListBox's two-way SelectedItem binding may have pushed in during Clear().
        private void RestoreSelection(EmployerCompany? previouslySelected)
        {
            if (previouslySelected == null)
                return;

            if (_visibleCompanies.Contains(previouslySelected))
                SelectedCompany = previouslySelected;
            else
                SelectedCompany = _visibleCompanies.FirstOrDefault();
        }

        private List<EmployerCompany> ComputeVisibleCompanies(
            List<EmployerCompany> companiesSnapshot, CompanyService cs, string query,
            CompanySortMode sortMode, Dictionary<string, int> employeeCounts)
        {
            IEnumerable<EmployerCompany> companies = companiesSnapshot
                .Where(c => cs.IsCompanyVisible(c) && PolicyService.CanAccessCompany(c));

            if (!string.IsNullOrEmpty(query))
            {
                companies = companies.Where(c =>
                    (!string.IsNullOrEmpty(c.Name) && c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(c.ICO) && c.ICO.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            return ApplyCompanySort(companies, sortMode, employeeCounts).ToList();
        }

        private IEnumerable<EmployerCompany> ApplyCompanySort(
            IEnumerable<EmployerCompany> companies, CompanySortMode sortMode, Dictionary<string, int> employeeCounts)
        {
            return sortMode switch
            {
                CompanySortMode.Name => companies
                    .OrderBy(c => c.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                CompanySortMode.Agency => companies
                    .OrderBy(c => c.Agency?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(c => c.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                CompanySortMode.EmployeeCount => companies
                    .OrderByDescending(c => employeeCounts.TryGetValue(c.Name ?? string.Empty, out var count) ? count : 0)
                    .ThenBy(c => c.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                _ => companies
            };
        }

        // Recomputes per-firm employee counts off the UI thread, then re-sorts if needed.
        // Overlapping calls cancel the previous scan (fast drawer open + sort changes).
        private async void RefreshCompanyEmployeeCountsAsync()
        {
            _employeeCountsCts?.Cancel();
            _employeeCountsCts?.Dispose();
            var cts = new CancellationTokenSource();
            _employeeCountsCts = cts;
            var token = cts.Token;

            try
            {
                var companiesSnapshot = _companyService.Companies.ToList();
                var counts = await Task.Run(() =>
                {
                    var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var company in companiesSnapshot)
                    {
                        token.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(company.Name)) continue;
                        try
                        {
                            dict[company.Name] = _employeeService.GetEmployeesForFirm(company.Name).Count;
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogError("MainViewModel.RefreshCompanyEmployeeCountsAsync", ex);
                        }
                    }
                    return dict;
                }, token).ConfigureAwait(true);

                if (token.IsCancellationRequested)
                    return;

                lock (_companyEmployeeCounts)
                {
                    foreach (var kv in counts)
                        _companyEmployeeCounts[kv.Key] = kv.Value;
                }

                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                {
                    if (token.IsCancellationRequested)
                        return;
                    if (CompanySortMode == CompanySortMode.EmployeeCount)
                        RefreshVisibleCompanies();
                }));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RefreshCompanyEmployeeCountsAsync", ex);
            }
        }

        public EmployerCompany? SelectedCompany
        {
            get => _companyService.SelectedCompany;
            set
            {
                if (value != null && !PolicyService.CanAccessCompany(value))
                    return;

                _companyService.SelectedCompany = value;
                OnPropertyChanged(nameof(SelectedCompany));
                OnPropertyChanged(nameof(HasSelectedCompany));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelectedCompany => SelectedCompany != null;

        public MainViewModel(
            NavigationService navigationService,
            CompanyService companyService,
            EmployeeService employeeService,
            TemplateService templateService,
            CandidateService candidateService,
            GeminiApiService geminiApiService,
            AppSettingsService appSettingsService,
            InvoiceViewModelFactory invoiceViewModelFactory,
            MainModuleViewModelFactory mainModuleViewModelFactory,
            AddCompanyViewModelFactory addCompanyViewModelFactory,
            AppNotificationService notificationService,
            WeatherService weatherService)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _candidateService = candidateService ?? throw new ArgumentNullException(nameof(candidateService));
            _geminiApiService = geminiApiService ?? throw new ArgumentNullException(nameof(geminiApiService));
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _invoiceViewModelFactory = invoiceViewModelFactory ?? throw new ArgumentNullException(nameof(invoiceViewModelFactory));
            _mainModuleViewModelFactory = mainModuleViewModelFactory ?? throw new ArgumentNullException(nameof(mainModuleViewModelFactory));
            _addCompanyViewModelFactory = addCompanyViewModelFactory ?? throw new ArgumentNullException(nameof(addCompanyViewModelFactory));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));

            GoToSettingsCommand = new RelayCommand(o => _navigationService.NavigateTo<SettingsViewModel>());
            ToggleNotificationsCommand = new RelayCommand(o => ToggleNotifications());
            MarkNotificationsReadCommand = new RelayCommand(o => _notificationService.MarkAllRead());
            ClearNotificationsCommand = new RelayCommand(o => _notificationService.ClearAll());
            ButtonCommand = new RelayCommand(o => { });
            ToggleDrawerCommand = new RelayCommand(o =>
            {
                var opening = !IsDrawerOpen;
                IsDrawerOpen = opening;
                // Counts are only used for EmployeeCount sort — skip scan for Name/Agency/Default.
                if (opening && CompanySortMode == CompanySortMode.EmployeeCount)
                    RefreshCompanyEmployeeCountsAsync();
            });

            if (Enum.TryParse<CompanySortMode>(
                    _appSettingsService.Settings.CompanySortMode, ignoreCase: true, out var savedCompanySortMode))
                _companySortMode = savedCompanySortMode;

            // Do not scan all firms for employee counts here — counts are only needed for
            // EmployeeCount sort (drawer open, sort change, clock tick, or visibility change).
            RefreshVisibleCompaniesSync();
            _companyService.VisibilityChanged += OnVisibilityChanged;

            BuildMenuCards();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _clockTimer.Tick += (_, _) =>
            {
                RefreshClock();
                if (IsDrawerOpen && CompanySortMode == CompanySortMode.EmployeeCount)
                    RefreshCompanyEmployeeCountsAsync();
            };
            RefreshClock();
            _clockTimer.Start();

            _ = LoadWeatherAsync();

            OpenAddCompanyDialogCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed(Res("CompanyDialogTitleAdd") ?? "Додати фірму"))
                    return;

                CleanupAddCompanyVm();
                AddCompanyVm = _addCompanyViewModelFactory.CreateAdd();
                AddCompanyVm.RequestClose += OnAddCompanyClose;
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            });

            EditCompanyCommand = new RelayCommand(o =>
            {
                var company = o as EmployerCompany ?? SelectedCompany;
                if (company == null) return;
                if (!PolicyService.CanAccessCompany(company)) return;
                if (!PolicyService.RequireCanEditCompany(company, Res("CompanyDialogTitleEdit") ?? "Редагувати фірму"))
                    return;
                if (!PolicyService.EnsureWriteAllowed(Res("CompanyDialogTitleEdit") ?? "Редагувати фірму"))
                    return;

                CleanupAddCompanyVm();
                AddCompanyVm = _addCompanyViewModelFactory.CreateEdit(company);
                AddCompanyVm.RequestClose += OnEditCompanyClose;
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            }, o => true);

            MoveCompanyUpCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Змінити порядок фірм"))
                    return;
                if (o is EmployerCompany c && PolicyService.CanAccessCompany(c) && PolicyService.RequireCanEditCompany(c, "Змінити порядок фірм"))
                {
                    _companyService.MoveCompanyUp(c);
                    RefreshVisibleCompanies();
                }
            });
            MoveCompanyDownCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Змінити порядок фірм"))
                    return;
                if (o is EmployerCompany c && PolicyService.CanAccessCompany(c) && PolicyService.RequireCanEditCompany(c, "Змінити порядок фірм"))
                {
                    _companyService.MoveCompanyDown(c);
                    RefreshVisibleCompanies();
                }
            });

            ToggleCompanySortMenuCommand = new RelayCommand(_ => IsCompanySortMenuOpen = !IsCompanySortMenuOpen);
            SetCompanySortCommand = new RelayCommand(o =>
            {
                if (o is not string sortKey)
                    return;

                if (Enum.TryParse<CompanySortMode>(sortKey, ignoreCase: true, out var mode))
                {
                    CompanySortMode = mode;
                    _appSettingsService.Settings.CompanySortMode = mode.ToString();
                    _appSettingsService.SaveSettings();

                    if (mode == CompanySortMode.EmployeeCount)
                        RefreshCompanyEmployeeCountsAsync();
                }

                IsCompanySortMenuOpen = false;
            });

            CloseAddCompanyDialogCommand = new RelayCommand(o => IsAddCompanyDialogOpen = false);

            ClearSearchCommand = new RelayCommand(o =>
            {
                SearchQuery = "";
                SearchResults.Clear();
                IsSearchOpen = false;
                HasNoSearchResults = false;
            });

            AISearchCommand = new RelayCommand(o => RunAISearch(), o =>
                !PolicyService.IsAIDisabled &&
                !IsAISearching &&
                !string.IsNullOrWhiteSpace(SearchQuery));

            NavigateToSearchResultCommand = new RelayCommand(o =>
            {
                if (o is not SearchResultItem item) return;
                SearchQuery = "";
                SearchResults.Clear();
                IsSearchOpen = false;
                HasNoSearchResults = false;
                NavigateToResult(item);
            });

            _companyService.SelectedCompanyChanged += OnSelectedCompanyChanged;

            RefreshProblemsCount();
            RefreshOverviewStats();
            _notificationService.PropertyChanged += OnNotificationServicePropertyChanged;
            Notifications.CollectionChanged += OnNotificationsCollectionChanged;
        }

        private void OnNotificationsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasNotifications));
        }

        private void ToggleNotifications()
        {
            IsNotificationCenterOpen = !IsNotificationCenterOpen;
            if (IsNotificationCenterOpen)
                _notificationService.MarkAllRead();
        }

        private void OnNotificationServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppNotificationService.UnreadCount))
                OnPropertyChanged(nameof(UnreadNotificationsCount));
            if (e.PropertyName == nameof(AppNotificationService.HasUnread))
                OnPropertyChanged(nameof(HasUnreadNotifications));
        }

        private void OnSelectedCompanyChanged(EmployerCompany? _)
        {
            OnPropertyChanged(nameof(SelectedCompany));
            OnPropertyChanged(nameof(HasSelectedCompany));
            OnPropertyChanged(nameof(SelectedCompanyDisplayName));
            OnPropertyChanged(nameof(SelectedCompanyIcoText));
            OnPropertyChanged(nameof(SelectedCompanyAgencyText));
            OnPropertyChanged(nameof(SelectedCompanySummaryText));
            RefreshProblemsCount();
            RefreshOverviewStats();
        }

        private void OnVisibilityChanged()
        {
            _ = App.Current?.Dispatcher?.BeginInvoke((Action)(() =>
            {
                RefreshVisibleCompanies();
                RefreshOverviewStats();
                if (IsDrawerOpen && CompanySortMode == CompanySortMode.EmployeeCount)
                    RefreshCompanyEmployeeCountsAsync();
            }));
        }

        public void Cleanup()
        {
            _companyService.SelectedCompanyChanged -= OnSelectedCompanyChanged;
            _companyService.VisibilityChanged -= OnVisibilityChanged;
            _notificationService.PropertyChanged -= OnNotificationServicePropertyChanged;
            Notifications.CollectionChanged -= OnNotificationsCollectionChanged;
            _searchDebounce?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _visibleCompaniesCts?.Cancel();
            _visibleCompaniesCts?.Dispose();
            _employeeCountsCts?.Cancel();
            _employeeCountsCts?.Dispose();
            _overviewStatsCts?.Cancel();
            _overviewStatsCts?.Dispose();
            _clockTimer.Stop();
        }

        private void BuildMenuCards()
        {
            var allCards = new List<MenuCardItem>
            {
                new() { Id = "dashboard", TitleKey = "DashboardTitle", IconKey = "IconDashboard",
                    GradientStart = "#00C9FF", GradientEnd = "#92FE9D",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<DashboardViewModel>()) },
                new() { Id = "employees", TitleKey = "BtnEmployees", IconKey = "IconPeople",
                    GradientStart = "#667EEA", GradientEnd = "#764BA2",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateEmployees(SelectedCompany))) },
                new() { Id = "templates", TitleKey = "BtnTemplates", IconKey = "IconTemplates",
                    GradientStart = "#11998E", GradientEnd = "#38EF7D",
                    Command = new RelayCommand(_ => { if (SelectedCompany != null) _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateTemplates(SelectedCompany)); }, _ => SelectedCompany != null) },
                new() { Id = "problems", TitleKey = "BtnProblems", IconKey = "IconProblems",
                    GradientStart = "#FF512F", GradientEnd = "#F09819",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateProblems())), BadgeCount = _problemsCount },
                new() { Id = "report", TitleKey = "BtnReport", IconKey = "IconReport",
                    GradientStart = "#4FACFE", GradientEnd = "#00F2FE",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<ReportViewModel>()) },
                new() { Id = "finances", TitleKey = "BtnFinances", IconKey = "IconFinances",
                    GradientStart = "#A18CD1", GradientEnd = "#FBC2EB",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<FinanceTablesViewModel>()) },
                new() { Id = "invoices", TitleKey = "BtnInvoices", IconKey = "IconInvoices",
                    GradientStart = "#26A69A", GradientEnd = "#66BB6A",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_invoiceViewModelFactory.CreateInvoices())) },
                new() { Id = "archive", TitleKey = "BtnArchive", IconKey = "IconArchive",
                    GradientStart = "#89F7FE", GradientEnd = "#66A6FF",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateArchive())) },
                new() { Id = "recentlydeleted", TitleKey = "BtnRecentlyDeleted", IconKey = "IconRecentlyDeleted",
                    GradientStart = "#FF9A9E", GradientEnd = "#FAD0C4",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateRecentlyDeleted())) },
                new() { Id = "activitylog", TitleKey = "BtnActivityLog", IconKey = "IconActivityLog",
                    GradientStart = "#FFD54F", GradientEnd = "#FF8A65",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<ActivityLogViewModel>()) },
                new() { Id = "candidates", TitleKey = "BtnCandidates", IconKey = "IconCandidates",
                    GradientStart = "#FF9800", GradientEnd = "#F57C00",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<CandidatesViewModel>()) },
                new() { Id = "news", TitleKey = "BtnNews", IconKey = "IconNews",
                    GradientStart = "#36D1DC", GradientEnd = "#5B86E5",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo<NewsViewModel>()) },
                new() { Id = "aichat", TitleKey = "BtnAIAssistant", IconKey = "IconAIAssistant",
                    GradientStart = "#7C4DFF", GradientEnd = "#448AFF",
                    Command = new RelayCommand(_ => _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateAiChat())) },
            };

            var savedOrder = _appSettingsService.Settings.MenuCardOrder;
            if (savedOrder != null && savedOrder.Count > 0)
            {
                var ordered = new List<MenuCardItem>();
                foreach (var id in savedOrder)
                {
                    var card = allCards.FirstOrDefault(c => c.Id == id);
                    if (card != null) ordered.Add(card);
                }
                foreach (var card in allCards)
                {
                    if (!ordered.Contains(card)) ordered.Add(card);
                }
                allCards = ordered;
            }

            allCards = allCards
                .Where(card => PolicyService.IsFeatureVisible(card.Id))
                .ToList();

            MenuCards = new ObservableCollection<MenuCardItem>(allCards);
        }

        public void MoveCard(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= MenuCards.Count || toIndex < 0 || toIndex >= MenuCards.Count || fromIndex == toIndex)
                return;
            MenuCards.Move(fromIndex, toIndex);
            SaveCardOrder();
        }

        private async void SaveCardOrder()
        {
            try
            {
                _appSettingsService.Settings.MenuCardOrder = MenuCards.Select(c => c.Id).ToList();
                await _appSettingsService.SaveSettingsImmediate();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.SaveCardOrder", ex);
            }
        }

        private void CleanupAddCompanyVm()
        {
            if (AddCompanyVm != null)
            {
                AddCompanyVm.RequestClose -= OnAddCompanyClose;
                AddCompanyVm.RequestClose -= OnEditCompanyClose;
            }
        }

        private void OnAddCompanyClose()
        {
            IsAddCompanyDialogOpen = false;
            RefreshVisibleCompanies();
            if (SelectedCompany == null && VisibleCompanies.Any())
                SelectedCompany = VisibleCompanies.Last();
            OnPropertyChanged(nameof(SelectedCompany));
            OnPropertyChanged(nameof(HasSelectedCompany));
            OnPropertyChanged(nameof(SelectedCompanyDisplayName));
            OnPropertyChanged(nameof(SelectedCompanyIcoText));
            OnPropertyChanged(nameof(SelectedCompanyAgencyText));
            OnPropertyChanged(nameof(SelectedCompanySummaryText));
            RefreshOverviewStats();
        }

        private void OnEditCompanyClose()
        {
            IsAddCompanyDialogOpen = false;
            RefreshVisibleCompanies();
            OnPropertyChanged(nameof(SelectedCompany));
            OnPropertyChanged(nameof(HasSelectedCompany));
            OnPropertyChanged(nameof(SelectedCompanyDisplayName));
            OnPropertyChanged(nameof(SelectedCompanyIcoText));
            OnPropertyChanged(nameof(SelectedCompanyAgencyText));
            OnPropertyChanged(nameof(SelectedCompanySummaryText));
            RefreshOverviewStats();
        }

        private async void RefreshProblemsCount()
        {
            try
            {
                var count = await Task.Run(() => ProblemsViewModel.CountAllProblems(_companyService, _employeeService));
                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() => ProblemsCount = count));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RefreshProblemsCount", ex);
                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() => ProblemsCount = 0));
            }
        }

        private void RefreshClock()
        {
            var now = DateTime.Now;
            GreetingText = GetGreeting(now);
            GreetingGlyph = GetGreetingGlyph(now);
            CurrentTimeText = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            CurrentDateText = now.ToString("dddd, dd.MM.yyyy", GetAppCulture());
            var utcOffset = TimeZoneInfo.Local.GetUtcOffset(now);
            var offsetSign = utcOffset >= TimeSpan.Zero ? "+" : "-";
            var offsetHours = Math.Abs(utcOffset.Hours);
            CurrentTimeZoneText = utcOffset.Minutes == 0
                ? $"UTC {offsetSign}{offsetHours}"
                : $"UTC {offsetSign}{utcOffset.Hours}:{Math.Abs(utcOffset.Minutes):00}";
        }

        private CancellationTokenSource? _overviewStatsCts;

        // Employee/template/problem counts for the selected company used to be computed
        // synchronously here - GetEmployeesForFirm + CountProblemsForCompany can hit the DB/disk
        // per employee, which blocked the UI thread every time this ran (on load, on company
        // switch, on visibility change). Now the counts are computed on a background thread and
        // marshalled back; the header text/name updates immediately, counts fill in right after.
        private void RefreshOverviewStats()
        {
            try
            {
                VisibleCompaniesCount = _visibleCompanies.Count;

                OnPropertyChanged(nameof(SelectedCompanyDisplayName));
                OnPropertyChanged(nameof(SelectedCompanyIcoText));
                OnPropertyChanged(nameof(SelectedCompanyAgencyText));

                _overviewStatsCts?.Cancel();
                var company = SelectedCompany;

                if (company == null)
                {
                    SelectedCompanyEmployeesCount = 0;
                    SelectedCompanyTemplatesCount = 0;
                    SelectedCompanyProblemsCount = 0;
                    OnPropertyChanged(nameof(SelectedCompanySummaryText));
                    return;
                }

                var cts = new CancellationTokenSource();
                _overviewStatsCts = cts;
                var token = cts.Token;

                _ = Task.Run(() =>
                {
                    try
                    {
                        var employeesCount = _employeeService.GetEmployeesForFirm(company.Name).Count;
                        var templatesCount = _templateService.GetTemplates(company.Name).Count;
                        var problemsCount = ProblemsViewModel.CountProblemsForCompany(company, _employeeService);

                        if (token.IsCancellationRequested) return;

                        _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                        {
                            if (token.IsCancellationRequested || !ReferenceEquals(SelectedCompany, company))
                                return;

                            SelectedCompanyEmployeesCount = employeesCount;
                            SelectedCompanyTemplatesCount = templatesCount;
                            SelectedCompanyProblemsCount = problemsCount;
                            OnPropertyChanged(nameof(SelectedCompanySummaryText));
                        }));
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("MainViewModel.RefreshOverviewStats", ex);
                    }
                }, token);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RefreshOverviewStats", ex);
            }
        }

        private async Task LoadWeatherAsync()
        {
            try
            {
                var info = await _weatherService.GetWeatherAsync().ConfigureAwait(true);
                if (info == null)
                {
                    HasWeather = false;
                    return;
                }

                WeatherTempText = string.Format(
                    CultureInfo.InvariantCulture, "{0:0}°", info.TemperatureC);
                WeatherDescription = Res(info.DescriptionKey);
                WeatherIconKey = info.IconKey;
                WeatherCity = info.City;
                HasWeather = true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("MainViewModel.LoadWeatherAsync", ex.Message);
                HasWeather = false;
            }
        }

        private string GetGreeting(DateTime now)
        {
            if (now.Hour < 12)
                return Res("MainGreetingMorning");
            if (now.Hour < 18)
                return Res("MainGreetingAfternoon");
            return Res("MainGreetingEvening");
        }

        private static string GetGreetingGlyph(DateTime now)
        {
            // Segoe MDL2 Assets: sunny by day, moon in the evening/night.
            if (now.Hour < 6 || now.Hour >= 21)
                return "\uE708"; // Quiet hours / moon
            if (now.Hour < 18)
                return "\uE706"; // Brightness / sun
            return "\uE708";
        }

        private CultureInfo GetAppCulture()
        {
            return (_appSettingsService.Settings.LanguageCode ?? "uk") switch
            {
                "en" => new CultureInfo("en-US"),
                "cs" => new CultureInfo("cs-CZ"),
                "ru" => new CultureInfo("ru-RU"),
                _ => new CultureInfo("uk-UA")
            };
        }

        private void DebounceSearch()
        {
            _searchDebounce?.Dispose();
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                SearchResults.Clear();
                IsSearchOpen = false;
                HasNoSearchResults = false;
                return;
            }
            _searchDebounce = new Timer(_ => _ = Application.Current?.Dispatcher?.BeginInvoke((Action)RunSearch), null, 300, Timeout.Infinite);
        }

        private async void RunSearch()
        {
            try
            {
                var oldCts = _searchCts;
                _searchCts = new CancellationTokenSource();
                var ct = _searchCts.Token;
                oldCts?.Cancel();
                oldCts?.Dispose();
                var query = _searchQuery.Trim();
                if (query.Length < 2) { IsSearchOpen = false; return; }

                var results = await Task.Run(() => PerformSearch(query, ct), ct);
                if (ct.IsCancellationRequested) return;
                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(results);
                    HasNoSearchResults = results.Count == 0;
                    IsSearchOpen = true;
                }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggingService.LogError("MainViewModel.RunSearch", ex); }
        }

        private List<SearchResultItem> PerformSearch(string query, CancellationToken ct)
        {
            var results = new List<SearchResultItem>();
            var q = query;
            var companies = _companyService.Companies
                .Where(PolicyService.CanAccessCompany)
                .ToList();

            foreach (var company in companies)
            {
                if (ct.IsCancellationRequested) return results;
                if (company.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new SearchResultItem
                    {
                        Category = Res("SearchCatCompanies"), CategoryIcon = "\uE80F", CategoryColor = "#9C27B0",
                        Title = company.Name, Subtitle = $"{company.Positions.Count} pos."
                    });
                }
            }

            foreach (var company in companies)
            {
                if (ct.IsCancellationRequested) return results;
                try
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                    foreach (var emp in employees)
                    {
                        if (ct.IsCancellationRequested) return results;
                        if ((emp.FullName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                            || (emp.PassportNumber?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                            || (emp.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                        {
                            results.Add(new SearchResultItem
                            {
                                Category = Res("SearchCatEmployees"), CategoryIcon = "\uE77B", CategoryColor = "#4CAF50",
                                Title = emp.FullName ?? string.Empty,
                                Subtitle = company.Name,
                                CompanyName = company.Name,
                                EmployeeFolder = emp.EmployeeFolder
                            });
                        }
                        if (results.Count >= 30) return results;
                    }
                }
                catch (Exception ex) { LoggingService.LogError("MainViewModel.PerformSearch.Employees", ex); }
            }

            foreach (var company in companies)
            {
                if (ct.IsCancellationRequested) return results;
                try
                {
                    var templates = _templateService.GetTemplates(company.Name);
                    foreach (var t in templates)
                    {
                        if (t.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new SearchResultItem
                            {
                                Category = Res("SearchCatTemplates"), CategoryIcon = "\uE8A5", CategoryColor = "#FF9800",
                                Title = t.Name, Subtitle = $"{company.Name} — {t.Format}",
                                CompanyName = company.Name
                            });
                        }
                        if (results.Count >= 30) return results;
                    }
                }
                catch (Exception ex) { LoggingService.LogError("MainViewModel.PerformSearch.Templates", ex); }
            }

            try
            {
                var archived = _employeeService.GetArchivedEmployees();
                if (archived != null)
                {
                    foreach (var a in archived)
                    {
                        if (ct.IsCancellationRequested) return results;
                        if (a.FullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(new SearchResultItem
                            {
                                Category = Res("SearchCatArchive"), CategoryIcon = "\uE7B8", CategoryColor = "#607D8B",
                                Title = a.FullName, Subtitle = a.FirmName,
                                EmployeeFolder = a.EmployeeFolder
                            });
                        }
                        if (results.Count >= 30) return results;
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("MainViewModel.PerformSearch.Archive", ex); }

            try
            {
                var candidates = _candidateService.GetAll();
                if (candidates != null)
                {
                    foreach (var c in candidates)
                    {
                        if (ct.IsCancellationRequested) return results;
                        if ((c.FullName?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                            || (c.Phone?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                        {
                            results.Add(new SearchResultItem
                            {
                                Category = Res("SearchCatCandidates"), CategoryIcon = "\uE716", CategoryColor = "#FF5722",
                                Title = c.FullName ?? string.Empty,
                                Subtitle = c.DesiredPosition,
                                EmployeeFolder = c.CandidateFolder
                            });
                        }
                        if (results.Count >= 30) return results;
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("MainViewModel.PerformSearch.Candidates", ex); }

            return results;
        }

        private async void RunAISearch()
        {
            try
            {
                if (!_geminiApiService.IsConfigured)
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(new[]
                    {
                        new SearchResultItem { Category = "AI", CategoryIcon = "\uE9D9", CategoryColor = "#7B1FA2",
                            Title = Res("AIChatNoModel"), Subtitle = "" }
                    });
                    IsSearchOpen = true;
                    return;
                }

                var query = SearchQuery.Trim();
                if (string.IsNullOrWhiteSpace(query)) return;

                IsAISearching = true;
                SearchResults = new ObservableCollection<SearchResultItem>(new[]
                {
                    new SearchResultItem { Category = "AI", CategoryIcon = "\uE9D9", CategoryColor = "#7B1FA2",
                        Title = Res("AIChatThinking"), Subtitle = "" }
                });
                IsSearchOpen = true;

                var index = await Task.Run(() => BuildEmployeeIndex());

                var systemPrompt = @"You are a search assistant for a Czech employment agency app. 
The user asks a question in natural language. You have access to the employee database.
Analyze the query and return ONLY a JSON array of matching employee indices (0-based).
Format: [0, 5, 12] — just the indices, nothing else.
If no employees match, return [].
Consider: names, companies, document expiry, salary, nationality, dates, status.";

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                var response = await _geminiApiService.ChatAsync(
                    $"Employee database:\n{index.data}\n\nUser query: {query}", systemPrompt, cts.Token);

                var indices = ParseIndices(response);
                var results = new List<SearchResultItem>();

                foreach (var idx in indices)
                {
                    if (idx >= 0 && idx < index.employees.Count)
                    {
                        var emp = index.employees[idx];
                        results.Add(new SearchResultItem
                        {
                            Category = Res("SearchCatEmployees"),
                            CategoryIcon = "\uE77B",
                            CategoryColor = "#7B1FA2",
                            Title = emp.FullName,
                            Subtitle = emp.FirmName,
                            CompanyName = emp.FirmName,
                            EmployeeFolder = emp.EmployeeFolder
                        });
                    }
                }

                if (results.Count == 0)
                {
                    results.Add(new SearchResultItem
                    {
                        Category = "AI", CategoryIcon = "\uE9D9", CategoryColor = "#7B1FA2",
                        Title = Res("GlobalSearchNoResults"), Subtitle = response.Length > 100 ? response[..100] : response
                    });
                }

                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(results);
                    HasNoSearchResults = results.Count == 0;
                }));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RunAISearch", ex);
                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(new[]
                    {
                        new SearchResultItem { Category = "AI", CategoryIcon = "\uE9D9", CategoryColor = "#E53935",
                            Title = Res("TitleError"), Subtitle = ex.Message }
                    });
                }));
            }
            finally
            {
                _ = Application.Current?.Dispatcher?.BeginInvoke((Action)(() => IsAISearching = false));
            }
        }

        private (string data, List<EmployeeSummary> employees) BuildEmployeeIndex()
        {
            var all = new List<EmployeeSummary>();
            var sb = new StringBuilder();
            int idx = 0;
            var companies = _companyService.Companies
                .Where(PolicyService.CanAccessCompany)
                .ToList();

            foreach (var company in companies)
            {
                try
                {
                    var employees = _employeeService.GetEmployeesForFirm(company.Name);
                    foreach (var emp in employees)
                    {
                        all.Add(emp);
                        sb.AppendLine($"[{idx}] {emp.FullName} | {company.Name} | {emp.PositionTitle} | Pass:{emp.PassportExpiry} | Visa:{emp.VisaExpiry} | Ins:{emp.InsuranceExpiry} | Status:{emp.Status} | Type:{emp.EmployeeType} | Phone:{emp.Phone} | Start:{emp.StartDate}");
                        idx++;
                        if (idx >= 200) break;
                    }
                }
                catch (Exception ex) { LoggingService.LogError("MainViewModel.BuildEmployeeIndex", ex); }
                if (idx >= 200) break;
            }

            return (sb.ToString(), all);
        }

        private static List<int> ParseIndices(string response)
        {
            var result = new List<int>();
            try
            {
                var start = response.IndexOf('[');
                var end = response.LastIndexOf(']');
                if (start >= 0 && end > start)
                {
                    var arr = response[(start + 1)..end];
                    foreach (var part in arr.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(part.Trim(), out var i))
                            result.Add(i);
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("MainViewModel.ParseIndices", ex); }
            return result;
        }

        private void NavigateToResult(SearchResultItem item)
        {
            var companies = _companyService.Companies
                .Where(PolicyService.CanAccessCompany)
                .ToList();
            switch (item.Category)
            {
                case var c when c == Res("SearchCatEmployees"):
                    var company = companies?.FirstOrDefault(co => co.Name == item.CompanyName);
                    if (company != null)
                    {
                        SelectedCompany = company;
                        _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateEmployees(company));
                    }
                    break;
                case var c when c == Res("SearchCatTemplates"):
                    var co2 = companies?.FirstOrDefault(co => co.Name == item.CompanyName);
                    if (co2 != null)
                    {
                        SelectedCompany = co2;
                        _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateTemplates(co2));
                    }
                    break;
                case var c when c == Res("SearchCatArchive"):
                    _navigationService.NavigateTo(_mainModuleViewModelFactory.CreateArchive());
                    break;
                case var c when c == Res("SearchCatCandidates"):
                    _navigationService.NavigateTo<CandidatesViewModel>();
                    break;
                case var c when c == Res("SearchCatCompanies"):
                    var co3 = companies?.FirstOrDefault(co => co.Name == item.Title);
                    if (co3 != null) SelectedCompany = co3;
                    break;
            }
        }
    }
}

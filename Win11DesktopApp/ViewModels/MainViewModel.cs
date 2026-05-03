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

        private void RefreshVisibleCompanies()
        {
            _visibleCompanies.Clear();
            var cs = _companyService;
            if (cs == null) return;
            foreach (var c in cs.Companies)
                if (cs.IsCompanyVisible(c))
                    _visibleCompanies.Add(c);
        }

        public EmployerCompany? SelectedCompany
        {
            get => _companyService.SelectedCompany;
            set
            {
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
            AppNotificationService notificationService)
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

            GoToSettingsCommand = new RelayCommand(o => _navigationService.NavigateTo<SettingsViewModel>());
            ToggleNotificationsCommand = new RelayCommand(o => ToggleNotifications());
            MarkNotificationsReadCommand = new RelayCommand(o => _notificationService.MarkAllRead());
            ClearNotificationsCommand = new RelayCommand(o => _notificationService.ClearAll());
            ButtonCommand = new RelayCommand(o => { });
            ToggleDrawerCommand = new RelayCommand(o => IsDrawerOpen = !IsDrawerOpen);

            RefreshVisibleCompanies();
            _companyService.VisibilityChanged += OnVisibilityChanged;

            BuildMenuCards();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _clockTimer.Tick += (_, _) => RefreshClock();
            RefreshClock();
            _clockTimer.Start();

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
                if (o is EmployerCompany c) _companyService.MoveCompanyUp(c);
            });
            MoveCompanyDownCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Змінити порядок фірм"))
                    return;
                if (o is EmployerCompany c) _companyService.MoveCompanyDown(c);
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
            Notifications.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNotifications));
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
            }));
        }

        public void Cleanup()
        {
            _companyService.SelectedCompanyChanged -= OnSelectedCompanyChanged;
            _companyService.VisibilityChanged -= OnVisibilityChanged;
            _notificationService.PropertyChanged -= OnNotificationServicePropertyChanged;
            _searchDebounce?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
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
            _appSettingsService.Settings.MenuCardOrder = MenuCards.Select(c => c.Id).ToList();
            await _appSettingsService.SaveSettingsImmediate();
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
            if (SelectedCompany == null && Companies.Any())
                SelectedCompany = Companies.Last();
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
            CurrentTimeText = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            CurrentDateText = now.ToString("dddd, dd.MM.yyyy", GetAppCulture());
        }

        private void RefreshOverviewStats()
        {
            try
            {
                VisibleCompaniesCount = _visibleCompanies.Count;

                if (SelectedCompany == null)
                {
                    SelectedCompanyEmployeesCount = 0;
                    SelectedCompanyTemplatesCount = 0;
                    SelectedCompanyProblemsCount = 0;
                }
                else
                {
                    SelectedCompanyEmployeesCount = _employeeService.GetEmployeesForFirm(SelectedCompany.Name).Count;
                    SelectedCompanyTemplatesCount = _templateService.GetTemplates(SelectedCompany.Name).Count;
                    SelectedCompanyProblemsCount = ProblemsViewModel.CountProblemsForCompany(SelectedCompany, _employeeService);
                }

                OnPropertyChanged(nameof(SelectedCompanyDisplayName));
                OnPropertyChanged(nameof(SelectedCompanyIcoText));
                OnPropertyChanged(nameof(SelectedCompanyAgencyText));
                OnPropertyChanged(nameof(SelectedCompanySummaryText));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RefreshOverviewStats", ex);
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
            var companies = _companyService.Companies.ToList();

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
            var companies = _companyService.Companies.ToList();

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
            var companies = _companyService.Companies;
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

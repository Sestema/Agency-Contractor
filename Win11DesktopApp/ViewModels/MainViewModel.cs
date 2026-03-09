using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Collections.ObjectModel;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using System.Linq;

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
        public ICommand GoToSettingsCommand { get; }
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

        public ObservableCollection<EmployerCompany> Companies =>
            App.CompanyService?.Companies ?? new ObservableCollection<EmployerCompany>();

        private ObservableCollection<EmployerCompany> _visibleCompanies = new();
        public ObservableCollection<EmployerCompany> VisibleCompanies => _visibleCompanies;

        private void RefreshVisibleCompanies()
        {
            _visibleCompanies.Clear();
            var cs = App.CompanyService;
            if (cs == null) return;
            foreach (var c in cs.Companies)
                if (cs.IsCompanyVisible(c))
                    _visibleCompanies.Add(c);
        }

        public EmployerCompany? SelectedCompany
        {
            get => App.CompanyService?.SelectedCompany;
            set
            {
                if (App.CompanyService != null)
                    App.CompanyService.SelectedCompany = value;
                OnPropertyChanged(nameof(SelectedCompany));
                OnPropertyChanged(nameof(HasSelectedCompany));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelectedCompany => SelectedCompany != null;

        public MainViewModel()
        {
            GoToSettingsCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new SettingsViewModel()));
            ButtonCommand = new RelayCommand(o => { });
            ToggleDrawerCommand = new RelayCommand(o => IsDrawerOpen = !IsDrawerOpen);

            RefreshVisibleCompanies();
            if (App.CompanyService != null)
                App.CompanyService.VisibilityChanged += OnVisibilityChanged;

            BuildMenuCards();

            OpenAddCompanyDialogCommand = new RelayCommand(o =>
            {
                CleanupAddCompanyVm();
                AddCompanyVm = new AddCompanyViewModel();
                AddCompanyVm.RequestClose += OnAddCompanyClose;
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            });

            EditCompanyCommand = new RelayCommand(o =>
            {
                var company = o as EmployerCompany ?? SelectedCompany;
                if (company == null) return;
                CleanupAddCompanyVm();
                AddCompanyVm = new AddCompanyViewModel(company);
                AddCompanyVm.RequestClose += OnEditCompanyClose;
                IsAddCompanyDialogOpen = true;
                IsDrawerOpen = false;
            }, o => true);

            MoveCompanyUpCommand = new RelayCommand(o =>
            {
                if (o is EmployerCompany c) App.CompanyService?.MoveCompanyUp(c);
            });
            MoveCompanyDownCommand = new RelayCommand(o =>
            {
                if (o is EmployerCompany c) App.CompanyService?.MoveCompanyDown(c);
            });

            CloseAddCompanyDialogCommand = new RelayCommand(o => IsAddCompanyDialogOpen = false);

            ClearSearchCommand = new RelayCommand(o =>
            {
                SearchQuery = "";
                SearchResults.Clear();
                IsSearchOpen = false;
                HasNoSearchResults = false;
            });

            AISearchCommand = new RelayCommand(o => RunAISearch(), o => !IsAISearching && !string.IsNullOrWhiteSpace(SearchQuery));

            NavigateToSearchResultCommand = new RelayCommand(o =>
            {
                if (o is not SearchResultItem item) return;
                SearchQuery = "";
                SearchResults.Clear();
                IsSearchOpen = false;
                HasNoSearchResults = false;
                NavigateToResult(item);
            });

            if (App.CompanyService != null)
                App.CompanyService.SelectedCompanyChanged += OnSelectedCompanyChanged;

            RefreshProblemsCount();
        }

        private void OnSelectedCompanyChanged(EmployerCompany? _)
        {
            OnPropertyChanged(nameof(SelectedCompany));
            OnPropertyChanged(nameof(HasSelectedCompany));
            RefreshProblemsCount();
        }

        private void OnVisibilityChanged()
        {
            App.Current?.Dispatcher?.Invoke(RefreshVisibleCompanies);
        }

        public void Cleanup()
        {
            if (App.CompanyService != null)
            {
                App.CompanyService.SelectedCompanyChanged -= OnSelectedCompanyChanged;
                App.CompanyService.VisibilityChanged -= OnVisibilityChanged;
            }
            _searchDebounce?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
        }

        private void BuildMenuCards()
        {
            var allCards = new List<MenuCardItem>
            {
                new() { Id = "dashboard", TitleKey = "DashboardTitle", IconKey = "IconDashboard",
                    GradientStart = "#00C9FF", GradientEnd = "#92FE9D",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new DashboardViewModel())) },
                new() { Id = "employees", TitleKey = "BtnEmployees", IconKey = "IconPeople",
                    GradientStart = "#667EEA", GradientEnd = "#764BA2",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new EmployeesViewModel(SelectedCompany))) },
                new() { Id = "templates", TitleKey = "BtnTemplates", IconKey = "IconTemplates",
                    GradientStart = "#11998E", GradientEnd = "#38EF7D",
                    Command = new RelayCommand(_ => { if (SelectedCompany != null) App.NavigationService?.NavigateTo(new TemplatesViewModel(SelectedCompany)); }, _ => SelectedCompany != null) },
                new() { Id = "problems", TitleKey = "BtnProblems", IconKey = "IconProblems",
                    GradientStart = "#FF512F", GradientEnd = "#F09819",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new ProblemsViewModel())), BadgeCount = _problemsCount },
                new() { Id = "report", TitleKey = "BtnReport", IconKey = "IconReport",
                    GradientStart = "#4FACFE", GradientEnd = "#00F2FE",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new ReportViewModel())) },
                new() { Id = "finances", TitleKey = "BtnFinances", IconKey = "IconFinances",
                    GradientStart = "#A18CD1", GradientEnd = "#FBC2EB",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new FinanceTablesViewModel())) },
                new() { Id = "archive", TitleKey = "BtnArchive", IconKey = "IconArchive",
                    GradientStart = "#89F7FE", GradientEnd = "#66A6FF",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new ArchiveViewModel())) },
                new() { Id = "activitylog", TitleKey = "BtnActivityLog", IconKey = "IconActivityLog",
                    GradientStart = "#FFD54F", GradientEnd = "#FF8A65",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new ActivityLogViewModel())) },
                new() { Id = "candidates", TitleKey = "BtnCandidates", IconKey = "IconCandidates",
                    GradientStart = "#FF9800", GradientEnd = "#F57C00",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new CandidatesViewModel())) },
                new() { Id = "aichat", TitleKey = "BtnAIAssistant", IconKey = "IconAIAssistant",
                    GradientStart = "#7C4DFF", GradientEnd = "#448AFF",
                    Command = new RelayCommand(_ => App.NavigationService?.NavigateTo(new AIChatViewModel())) },
            };

            var savedOrder = App.AppSettingsService?.Settings?.MenuCardOrder;
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
            if (App.AppSettingsService?.Settings != null)
                App.AppSettingsService.Settings.MenuCardOrder = MenuCards.Select(c => c.Id).ToList();
            if (App.AppSettingsService != null)
                await App.AppSettingsService.SaveSettingsImmediate();
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
        }

        private void OnEditCompanyClose()
        {
            IsAddCompanyDialogOpen = false;
            OnPropertyChanged(nameof(SelectedCompany));
            OnPropertyChanged(nameof(HasSelectedCompany));
        }

        private async void RefreshProblemsCount()
        {
            try
            {
                var count = await Task.Run(() => ProblemsViewModel.CountAllProblems());
                Application.Current?.Dispatcher?.Invoke(() => ProblemsCount = count);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RefreshProblemsCount", ex);
                Application.Current?.Dispatcher?.Invoke(() => ProblemsCount = 0);
            }
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
            _searchDebounce = new Timer(_ => Application.Current?.Dispatcher?.Invoke(() => RunSearch()), null, 300, Timeout.Infinite);
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
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(results);
                    HasNoSearchResults = results.Count == 0;
                    IsSearchOpen = true;
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggingService.LogError("MainViewModel.RunSearch", ex); }
        }

        private static List<SearchResultItem> PerformSearch(string query, CancellationToken ct)
        {
            var results = new List<SearchResultItem>();
            var q = query;
            var companies = App.CompanyService?.Companies;
            if (companies == null) return results;

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
                    var employees = App.EmployeeService?.GetEmployeesForFirm(company.Name);
                    if (employees == null) continue;
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
                    var templates = App.TemplateService?.GetTemplates(company.Name);
                    if (templates == null) continue;
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
                var archived = App.EmployeeService?.GetArchivedEmployees();
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
                var candidates = App.CandidateService?.GetAll();
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
                if (App.GeminiApiService?.IsConfigured != true)
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
                var response = await App.GeminiApiService.ChatAsync(
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

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(results);
                    HasNoSearchResults = results.Count == 0;
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError("MainViewModel.RunAISearch", ex);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SearchResults = new ObservableCollection<SearchResultItem>(new[]
                    {
                        new SearchResultItem { Category = "AI", CategoryIcon = "\uE9D9", CategoryColor = "#E53935",
                            Title = Res("TitleError"), Subtitle = ex.Message }
                    });
                });
            }
            finally
            {
                Application.Current?.Dispatcher?.Invoke(() => IsAISearching = false);
            }
        }

        private static (string data, List<EmployeeSummary> employees) BuildEmployeeIndex()
        {
            var all = new List<EmployeeSummary>();
            var sb = new StringBuilder();
            int idx = 0;
            var companies = App.CompanyService?.Companies;
            if (companies == null) return (sb.ToString(), all);

            foreach (var company in companies)
            {
                try
                {
                    var employees = App.EmployeeService?.GetEmployeesForFirm(company.Name);
                    if (employees == null) continue;
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
            var companies = App.CompanyService?.Companies;
            switch (item.Category)
            {
                case var c when c == Res("SearchCatEmployees"):
                    var company = companies?.FirstOrDefault(co => co.Name == item.CompanyName);
                    if (company != null)
                    {
                        SelectedCompany = company;
                        App.NavigationService?.NavigateTo(new EmployeesViewModel(company));
                    }
                    break;
                case var c when c == Res("SearchCatTemplates"):
                    var co2 = companies?.FirstOrDefault(co => co.Name == item.CompanyName);
                    if (co2 != null)
                    {
                        SelectedCompany = co2;
                        App.NavigationService?.NavigateTo(new TemplatesViewModel(co2));
                    }
                    break;
                case var c when c == Res("SearchCatArchive"):
                    App.NavigationService?.NavigateTo(new ArchiveViewModel());
                    break;
                case var c when c == Res("SearchCatCandidates"):
                    App.NavigationService?.NavigateTo(new CandidatesViewModel());
                    break;
                case var c when c == Res("SearchCatCompanies"):
                    var co3 = companies?.FirstOrDefault(co => co.Name == item.Title);
                    if (co3 != null) SelectedCompany = co3;
                    break;
            }
        }
    }
}

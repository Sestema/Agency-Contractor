using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AdminPanel
{
    public partial class MainWindow : Window
    {
        private sealed class GridColumnLayout
        {
            public string Key { get; set; } = string.Empty;
            public double Width { get; set; }
            public int DisplayIndex { get; set; }
        }

        private sealed class AdminPanelLayoutSettings
        {
            public List<GridColumnLayout> ClientGridColumns { get; set; } = new();
            public double WindowWidth { get; set; }
            public double WindowHeight { get; set; }
            public double WindowLeft { get; set; }
            public double WindowTop { get; set; }
            public string WindowState { get; set; } = nameof(System.Windows.WindowState.Normal);
            public string ClientSearch { get; set; } = string.Empty;
            public string ClientStatus { get; set; } = "all";
            public string LicenseFilter { get; set; } = "all";
            public string ActivityFilter { get; set; } = "all";
            public string VersionFilter { get; set; } = string.Empty;
            public string SortMemberPath { get; set; } = string.Empty;
            public string SortDirection { get; set; } = string.Empty;
        }

        private sealed class SessionSummaryRow
        {
            public DateTime StartedAt { get; set; }
            public DateTime LastActivityAt { get; set; }
            public string AppVersion { get; set; } = "";
            public string UpdateDisplay { get; set; } = "—";
            public int FirmsAdded { get; set; }
            public int EmployeesAdded { get; set; }
            public int ErrorCount { get; set; }
            public int EventCount { get; set; }
            public string SessionSummary { get; set; } = "—";

            public string DurationDisplay
            {
                get
                {
                    var duration = LastActivityAt - StartedAt;
                    if (duration.TotalMinutes < 1)
                        return "<1 хв";
                    if (duration.TotalHours < 1)
                        return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes))} хв";
                    return $"{(int)duration.TotalHours} г {duration.Minutes} хв";
                }
            }
        }

        private sealed class ActivityRow
        {
            public DateTime At { get; set; }
            public string Kind { get; set; } = "";
            public string ActionCode { get; set; } = "";
            public string ActionDisplay { get; set; } = "";
            public string Subject { get; set; } = "";
            public string Place { get; set; } = "";
            public string Details { get; set; } = "";

            public string DateDisplay => At.ToString("dd.MM.yyyy HH:mm");
            public string DayDisplay => FormatActivityDay(At);
        }

        private readonly SupabaseService _svc;
        private ClientRecord? _selected;
        private ClientProfileRecord? _selectedProfile;
        private TenantRecord? _loadedTenant;
        private List<ClientRecord> _allClients = new();
        private List<TelemetryRecord> _activeTelemetry = new();
        private string? _telemetryNextCursor;
        private bool _telemetryHasMore;
        private string? _telemetryClientId;
        private string? _telemetryLoadingClientId;
        private string? _profileClientId;
        private string? _profileLoadingClientId;
        private string? _tenantClientId;
        private string? _tenantLoadingClientId;
        private ClientMirrorSnapshot? _activitySnapshot;
        private string? _activityClientId;
        private string? _activityLoadingClientId;
        private bool _activityLoadFailed;
        private List<ActivityRow> _allActivity = new();
        private bool _isUpdatingTelemetryEventFilter;
        private bool _isPopulatingAccessConfig;
        private bool _isRestoringFilters;
        private bool _clientFiltersRestored;
        private AdminPanelLayoutSettings? _restoredLayout;
        private string? _clientSortMemberPath;
        private ListSortDirection _clientSortDirection = ListSortDirection.Descending;
        private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

        private const string BaseUrl = "https://tssgxhatnjvqthdiyuwo.supabase.co";
        private static readonly JsonSerializerOptions LayoutJsonOptions = new() { WriteIndented = true };

        public MainWindow()
        {
            InitializeComponent();
            _svc = new SupabaseService(BaseUrl);
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Minimized)
                    _lastNonMinimizedWindowState = WindowState;
            };
            Loaded += async (_, _) =>
            {
                if (!await EnsureAdminSessionAsync())
                {
                    Close();
                    return;
                }
                RestoreClientGridLayout();
                await RefreshAsync();
            };
            Closing += (_, _) => SaveClientGridLayout();
        }

        private async Task<bool> EnsureAdminSessionAsync()
        {
            while (true)
            {
                var login = new AdminLoginWindow
                {
                    Owner = this
                };

                if (login.ShowDialog() != true)
                    return false;

                TxtStatus.Text = "Авторизація...";
                if (await _svc.AuthenticateAsync(login.Password))
                    return true;

                MessageBox.Show(
                    this,
                    "Невірний пароль адміністратора або admin-gateway недоступний.",
                    "Admin login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async Task RefreshAsync(string? preferredClientId = null)
        {
            try
            {
                TxtStatus.Text = "Завантаження...";

                var selectedId = preferredClientId ?? _selected?.Id;
                var clientsTask = _svc.GetClientsAsync();
                await Task.WhenAll(clientsTask);

                _allClients = clientsTask.Result;

                _isRestoringFilters = true;
                try
                {
                    PopulateVersionFilter();
                    ApplyRestoredClientFilters();
                }
                finally
                {
                    _isRestoringFilters = false;
                }

                ApplyClientFilters(restoreSelection: false);
                RestoreClientSelection(selectedId);

                if (_selected == null)
                    OnClientSelected();
                else
                {
                    _activityClientId = null;
                    _activitySnapshot = null;
                    _activityLoadFailed = false;
                    _ = EnsureSelectedClientActivityLoadedAsync(_selected.Id);
                    if (IsActivityOrHistoryTabSelected())
                        _ = EnsureSelectedClientTelemetryLoadedAsync(_selected.Id);
                }

                TxtStatus.Text = $"Оновлено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Помилка: {ex.Message}";
                ShowActionError("завантажити дані", ex);
            }
        }

        private static string GetClientGridLayoutPath()
        {
            var settingsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AgencyContractorAdmin");
            Directory.CreateDirectory(settingsDir);
            return Path.Combine(settingsDir, "layout.json");
        }

        private void RestoreClientGridLayout()
        {
            try
            {
                var path = GetClientGridLayoutPath();
                if (!File.Exists(path))
                    return;

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AdminPanelLayoutSettings>(json, LayoutJsonOptions);
                _restoredLayout = settings;
                RestoreWindowLayout(settings);
                if (!string.IsNullOrWhiteSpace(settings?.SortMemberPath))
                {
                    _clientSortMemberPath = settings.SortMemberPath;
                    _clientSortDirection = string.Equals(settings.SortDirection, nameof(ListSortDirection.Ascending), StringComparison.Ordinal)
                        ? ListSortDirection.Ascending
                        : ListSortDirection.Descending;
                }

                var layouts = settings?.ClientGridColumns;
                if (layouts == null || layouts.Count == 0)
                    return;

                var byKey = layouts
                    .Where(layout => !string.IsNullOrWhiteSpace(layout.Key))
                    .ToDictionary(layout => layout.Key, StringComparer.Ordinal);

                foreach (var column in DgClients.Columns)
                {
                    var key = column.Header?.ToString();
                    if (string.IsNullOrWhiteSpace(key) || !byKey.TryGetValue(key, out var layout))
                        continue;

                    if (layout.Width > 0)
                        column.Width = new DataGridLength(layout.Width);
                }

                foreach (var layout in layouts.OrderBy(layout => layout.DisplayIndex))
                {
                    var column = DgClients.Columns.FirstOrDefault(col =>
                        string.Equals(col.Header?.ToString(), layout.Key, StringComparison.Ordinal));
                    if (column == null)
                        continue;

                    var safeIndex = Math.Max(0, Math.Min(layout.DisplayIndex, DgClients.Columns.Count - 1));
                    column.DisplayIndex = safeIndex;
                }
            }
            catch
            {
                // Layout restore should never block panel startup.
            }
        }

        private void SaveClientGridLayout()
        {
            try
            {
                var windowStateToSave = WindowState == WindowState.Minimized
                    ? _lastNonMinimizedWindowState
                    : WindowState;
                var bounds = windowStateToSave == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;
                CaptureClientGridSort();
                var settings = new AdminPanelLayoutSettings
                {
                    WindowWidth = bounds.Width,
                    WindowHeight = bounds.Height,
                    WindowLeft = bounds.Left,
                    WindowTop = bounds.Top,
                    WindowState = windowStateToSave == WindowState.Maximized
                        ? nameof(System.Windows.WindowState.Maximized)
                        : nameof(System.Windows.WindowState.Normal),
                    ClientSearch = TxtClientSearch.Text ?? string.Empty,
                    ClientStatus = GetSelectedComboTag(CmbClientStatus),
                    LicenseFilter = GetSelectedComboTag(CmbLicenseFilter),
                    ActivityFilter = GetSelectedComboTag(CmbActivityFilter),
                    VersionFilter = CmbVersionFilter.SelectedItem as string ?? string.Empty,
                    SortMemberPath = _clientSortMemberPath ?? string.Empty,
                    SortDirection = _clientSortDirection == ListSortDirection.Ascending
                        ? nameof(ListSortDirection.Ascending)
                        : nameof(ListSortDirection.Descending),
                    ClientGridColumns = DgClients.Columns
                        .Where(column => !string.IsNullOrWhiteSpace(column.Header?.ToString()))
                        .Select(column => new GridColumnLayout
                        {
                            Key = column.Header?.ToString() ?? string.Empty,
                            Width = column.ActualWidth > 0 ? column.ActualWidth : column.Width.DisplayValue,
                            DisplayIndex = column.DisplayIndex
                        })
                        .ToList()
                };

                var json = JsonSerializer.Serialize(settings, LayoutJsonOptions);
                File.WriteAllText(GetClientGridLayoutPath(), json);
            }
            catch
            {
                // Layout save should never block window closing.
            }
        }

        private void RestoreWindowLayout(AdminPanelLayoutSettings? settings)
        {
            if (settings == null)
                return;

            var savedBounds = new Rect(
                settings.WindowLeft,
                settings.WindowTop,
                settings.WindowWidth,
                settings.WindowHeight);

            if (IsVisibleOnAnyScreen(savedBounds))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Width = Math.Max(MinWidth, settings.WindowWidth);
                Height = Math.Max(MinHeight, settings.WindowHeight);
                Left = settings.WindowLeft;
                Top = settings.WindowTop;
            }

            if (!Enum.TryParse(settings.WindowState, out WindowState savedState))
                return;

            if (savedState == WindowState.Minimized)
                savedState = WindowState.Normal;

            _lastNonMinimizedWindowState = savedState;
            if (savedState == WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }

        private static bool IsVisibleOnAnyScreen(Rect bounds)
        {
            if (bounds.Width < 100 || bounds.Height < 100)
                return false;

            var desktopBounds = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            return desktopBounds.IntersectsWith(bounds);
        }

        private void RestoreClientSelection(string? selectedId)
        {
            var clients = DgClients.ItemsSource as List<ClientRecord> ?? new List<ClientRecord>();
            ClientRecord? match = null;

            if (!string.IsNullOrWhiteSpace(selectedId))
                match = clients.FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.Ordinal));

            if (match == null && string.IsNullOrWhiteSpace(selectedId) && clients.Count > 0)
                match = clients[0];

            DgClients.SelectedItem = match;

            if (match == null)
            {
                _selected = null;
                OnClientSelected();
            }
        }

        private void PopulateVersionFilter()
        {
            var selected = CmbVersionFilter.SelectedItem as string;
            var versions = _allClients
                .Select(c => c.AppVersion)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            versions.Insert(0, "Усі версії");
            CmbVersionFilter.ItemsSource = versions;
            CmbVersionFilter.SelectedItem = versions.Contains(selected ?? string.Empty) ? selected : versions[0];
        }

        private void ApplyClientFilters(bool restoreSelection = true)
        {
            IEnumerable<ClientRecord> filtered = _allClients;
            var query = (TxtClientSearch.Text ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(c =>
                    (c.DisplayName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.ProfileFirstName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.ProfileLastName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.MachineName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.MachineId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.IpAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.AppVersion?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var clientStatus = GetSelectedComboTag(CmbClientStatus);
            filtered = clientStatus switch
            {
                "trial" => filtered.Where(c => string.Equals(c.AccessStateCode, "trial", StringComparison.Ordinal)),
                "activated" => filtered.Where(c => string.Equals(c.AccessStateCode, "activated", StringComparison.Ordinal)),
                "readonly" => filtered.Where(c => string.Equals(c.AccessStateCode, "readonly", StringComparison.Ordinal)),
                "blocked" => filtered.Where(c => c.IsBlocked),
                _ => filtered
            };

            var licenseFilter = GetSelectedComboTag(CmbLicenseFilter);
            filtered = licenseFilter switch
            {
                "expired" => filtered.Where(c => GetDaysUntilExpiry(c) < 0),
                "7" => filtered.Where(c => IsExpiringWithin(c, 7)),
                "30" => filtered.Where(c => IsExpiringWithin(c, 30)),
                _ => filtered
            };

            var activityFilter = GetSelectedComboTag(CmbActivityFilter);
            filtered = activityFilter switch
            {
                "recent3" => filtered.Where(c => c.LastSeen.HasValue && c.LastSeen.Value >= DateTime.UtcNow.AddDays(-3)),
                "stale7" => filtered.Where(c => !c.LastSeen.HasValue || c.LastSeen.Value < DateTime.UtcNow.AddDays(-7)),
                "stale30" => filtered.Where(c => !c.LastSeen.HasValue || c.LastSeen.Value < DateTime.UtcNow.AddDays(-30)),
                "never" => filtered.Where(c => !c.LastSeen.HasValue),
                _ => filtered
            };

            var version = CmbVersionFilter.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(version) && !string.Equals(version, "Усі версії", StringComparison.Ordinal))
                filtered = filtered.Where(c => string.Equals(c.AppVersion, version, StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered
                .OrderByDescending(c => c.LastSeen ?? DateTime.MinValue)
                .ThenBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var selectedId = restoreSelection ? _selected?.Id : null;
            CaptureClientGridSort();
            DgClients.ItemsSource = filteredList;
            ApplyClientGridSort();
            UpdateClientCounters(filteredList);

            if (restoreSelection && !string.IsNullOrWhiteSpace(selectedId))
                DgClients.SelectedItem = filteredList.FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.Ordinal));
        }

        private void UpdateClientCounters(List<ClientRecord> filtered)
        {
            var expiringSoonFiltered = filtered.Count(c => IsExpiringWithin(c, 7));
            var expiringSoonAll = _allClients.Count(c => IsExpiringWithin(c, 7));
            TxtCount.Text = $"Показано: {filtered.Count}/{_allClients.Count} | Заблокованих: {filtered.Count(c => c.IsBlocked)} | <=7 днів: {expiringSoonFiltered}";
            if (TxtStatExpiring != null)
                TxtStatExpiring.Text = $"<=7 днів: {expiringSoonAll}";
        }

        private void ApplyRestoredClientFilters()
        {
            if (_clientFiltersRestored || _restoredLayout == null)
                return;

            TxtClientSearch.Text = _restoredLayout.ClientSearch ?? string.Empty;
            SelectComboTag(CmbClientStatus, string.IsNullOrWhiteSpace(_restoredLayout.ClientStatus) ? "all" : _restoredLayout.ClientStatus);
            SelectComboTag(CmbLicenseFilter, string.IsNullOrWhiteSpace(_restoredLayout.LicenseFilter) ? "all" : _restoredLayout.LicenseFilter);
            SelectComboTag(CmbActivityFilter, string.IsNullOrWhiteSpace(_restoredLayout.ActivityFilter) ? "all" : _restoredLayout.ActivityFilter);

            var version = _restoredLayout.VersionFilter;
            if (!string.IsNullOrWhiteSpace(version) && CmbVersionFilter.Items.Contains(version))
                CmbVersionFilter.SelectedItem = version;

            _clientFiltersRestored = true;
        }

        private void CaptureClientGridSort()
        {
            if (DgClients.Items.SortDescriptions.Count == 0)
                return;

            var sort = DgClients.Items.SortDescriptions[0];
            _clientSortMemberPath = sort.PropertyName;
            _clientSortDirection = sort.Direction;
        }

        private void ApplyClientGridSort()
        {
            DgClients.Items.SortDescriptions.Clear();
            foreach (var column in DgClients.Columns)
                column.SortDirection = null;

            if (string.IsNullOrWhiteSpace(_clientSortMemberPath))
                return;

            DgClients.Items.SortDescriptions.Add(new SortDescription(_clientSortMemberPath, _clientSortDirection));
            var sortedColumn = DgClients.Columns.FirstOrDefault(column =>
                string.Equals(GetColumnSortPath(column), _clientSortMemberPath, StringComparison.Ordinal));
            if (sortedColumn != null)
                sortedColumn.SortDirection = _clientSortDirection;
        }

        private static string? GetColumnSortPath(DataGridColumn column)
        {
            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
                return column.SortMemberPath;

            if (column is DataGridBoundColumn bound && bound.Binding is System.Windows.Data.Binding binding)
                return binding.Path?.Path;

            return column.Header?.ToString();
        }

        private void UpdateStats(List<TelemetryRecord> telemetry, string? clientId)
        {
            var filtered = clientId != null
                ? telemetry.Where(t => t.ClientId == clientId).ToList()
                : telemetry;

            var firmsCreated = filtered.Count(t => t.EventType == "firm_created");
            var employeesAdded = filtered.Count(t => t.EventType == "employee_added");
            var totalFirms = 0;
            var totalEmployees = 0;

            if (clientId != null)
            {
                var latestStats = GetLatestStatsTelemetry(filtered);
                if (latestStats != null)
                {
                    ExtractStats(latestStats, out totalFirms, out totalEmployees);
                }
                else if (_selected != null)
                {
                    totalFirms = _selected.FirmsCount;
                    totalEmployees = _selected.EmployeesCount;
                }
            }
            else
            {
                totalFirms = _allClients.Sum(c => c.FirmsCount);
                totalEmployees = _allClients.Sum(c => c.EmployeesCount);
            }

            var label = clientId != null ? " (клієнт)" : " (всі)";
            TxtStatFirms.Text = $"Фірм: {totalFirms} (створено: +{firmsCreated}){label}";
            TxtStatEmployees.Text = $"Працівників: {totalEmployees} (додано: +{employeesAdded}){label}";
            TxtStatEvents.Text = clientId != null
                ? string.Equals(_telemetryClientId, clientId, StringComparison.Ordinal)
                    ? $"Подій: {filtered.Count}"
                    : "Подій: ще не завантажено"
                : "Подій: завантажуються лише для вибраного клієнта";
        }

        private static void ExtractStats(TelemetryRecord? hb, out int firms, out int employees)
        {
            firms = 0;
            employees = 0;
            if (hb?.EventData == null)
                return;

            try
            {
                var el = hb.EventData.Value;
                if (el.TryGetProperty("firms_count", out var firmsCount))
                    firms = firmsCount.GetInt32();
                if (el.TryGetProperty("employees_count", out var employeesCount))
                    employees = employeesCount.GetInt32();
            }
            catch
            {
                // ignore malformed telemetry payloads
            }
        }

        private void DgClients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = DgClients.SelectedItem as ClientRecord;
            OnClientSelected();
        }

        private async void ClientDataTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, ClientDataTabs))
                return;

            var selectedId = _selected?.Id;
            if (string.IsNullOrWhiteSpace(selectedId))
                return;

            if (ReferenceEquals(ClientDataTabs.SelectedItem, TabSessions)
                || ReferenceEquals(ClientDataTabs.SelectedItem, TabEvents)
                || ReferenceEquals(ClientDataTabs.SelectedItem, TabActivity))
                await EnsureSelectedClientTelemetryLoadedAsync(selectedId);

            if (ReferenceEquals(ClientDataTabs.SelectedItem, TabActivity))
                await EnsureSelectedClientActivityLoadedAsync(selectedId);
        }

        private void DgClients_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedClientMirror();
        }

        private void OpenSelectedClientMirror()
        {
            if (_selected == null)
                return;

            var mirrorWindow = new ClientMirrorWindow(_svc, _selected)
            {
                Owner = this
            };
            mirrorWindow.Show();
        }

        private void UpdateActionButtons()
        {
            var hasSelection = _selected != null;
            BtnBlock.IsEnabled = hasSelection && _selected?.IsBlocked == false;
            BtnUnblock.IsEnabled = hasSelection && _selected?.IsBlocked == true;
            BtnBlockIp.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selected?.IpAddress);
            BtnExtend.IsEnabled = hasSelection;
            BtnDelete.IsEnabled = hasSelection;
            BtnResetProfile.IsEnabled = hasSelection && !string.Equals(_profileLoadingClientId, _selected?.Id, StringComparison.Ordinal);
            BtnSaveNotes.IsEnabled = hasSelection && HasNotesChanged();
            BtnSaveAccessConfig.IsEnabled = hasSelection && HasAccessConfigChanged();
        }

        private void OnClientSelected()
        {
            _selectedProfile = null;
            _profileClientId = null;
            _loadedTenant = null;
            _tenantClientId = null;

            _activeTelemetry = new List<TelemetryRecord>();
            _telemetryNextCursor = null;
            _telemetryHasMore = false;
            _telemetryClientId = null;

            _activitySnapshot = null;
            _activityClientId = null;
            _activityLoadFailed = false;
            _allActivity = new List<ActivityRow>();

            RefreshSessionSummaries();
            PopulateTelemetryEventFilter();
            ApplyTelemetryFilters();
            UpdateStats(_activeTelemetry, _selected?.Id);
            PopulateClientDetails(_selected);
            UpdateActionButtons();

            var selectedId = _selected?.Id;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                _ = EnsureSelectedClientTenantLoadedAsync(selectedId);
                _ = EnsureSelectedClientActivityLoadedAsync(selectedId);
                if (IsActivityOrHistoryTabSelected())
                    _ = EnsureSelectedClientTelemetryLoadedAsync(selectedId);
            }
        }

        private bool IsActivityOrHistoryTabSelected()
        {
            return ReferenceEquals(ClientDataTabs.SelectedItem, TabActivity)
                || ReferenceEquals(ClientDataTabs.SelectedItem, TabSessions)
                || ReferenceEquals(ClientDataTabs.SelectedItem, TabEvents);
        }

        private async Task EnsureSelectedClientTenantLoadedAsync(string expectedClientId)
        {
            if (string.IsNullOrWhiteSpace(expectedClientId))
                return;

            if (string.Equals(_tenantClientId, expectedClientId, StringComparison.Ordinal))
                return;

            if (string.Equals(_tenantLoadingClientId, expectedClientId, StringComparison.Ordinal))
                return;

            _tenantLoadingClientId = expectedClientId;

            try
            {
                var tenant = await _svc.TryGetTenantForClientAsync(expectedClientId);
                if (!string.Equals(_selected?.Id, expectedClientId, StringComparison.Ordinal))
                    return;

                _loadedTenant = tenant;
                _tenantClientId = expectedClientId;
                SetAccessConfigFields(() =>
                {
                    ApplyBusinessTenantFields(_selected, tenant);
                    UpdateBusinessPanelVisibility();
                });
                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                TxtBusinessTenantInfo.Text = "Tenant недоступний. Запустіть supabase/admin_tenant_rpc.sql у SQL Editor.";
                TxtStatus.Text = $"Tenant load: {ex.Message}";
            }
            finally
            {
                if (string.Equals(_tenantLoadingClientId, expectedClientId, StringComparison.Ordinal))
                    _tenantLoadingClientId = null;
            }
        }

        private async Task EnsureSelectedClientTelemetryLoadedAsync(string expectedClientId)
        {
            if (string.IsNullOrWhiteSpace(expectedClientId))
                return;

            if (string.Equals(_telemetryClientId, expectedClientId, StringComparison.Ordinal))
                return;

            if (string.Equals(_telemetryLoadingClientId, expectedClientId, StringComparison.Ordinal))
                return;

            _telemetryLoadingClientId = expectedClientId;
            TxtTelemetrySummary.Text = "Завантаження подій...";
            TxtSessionsSummary.Text = "Завантаження подій для побудови сесій...";
            UpdateTelemetryPaginationButton();

            try
            {
                var telemetryPage = await _svc.GetTelemetryPageAsync(expectedClientId, 200);
                if (!string.Equals(_selected?.Id, expectedClientId, StringComparison.Ordinal))
                    return;

                _activeTelemetry = telemetryPage.Items;
                _telemetryNextCursor = string.IsNullOrWhiteSpace(telemetryPage.NextCursor) ? null : telemetryPage.NextCursor;
                _telemetryHasMore = telemetryPage.HasMore;
                _telemetryClientId = expectedClientId;

                RefreshSessionSummaries();
                UpdateStats(_activeTelemetry, expectedClientId);
                PopulateClientDetails(_selected);
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                RefreshActivityUi();
                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Telemetry error: {ex.Message}";
            }
            finally
            {
                if (string.Equals(_telemetryLoadingClientId, expectedClientId, StringComparison.Ordinal))
                    _telemetryLoadingClientId = null;
                UpdateTelemetryPaginationButton();
            }
        }

        private async Task EnsureSelectedClientProfileLoadedAsync(string expectedClientId)
        {
            if (string.IsNullOrWhiteSpace(expectedClientId))
                return;

            if (string.Equals(_profileClientId, expectedClientId, StringComparison.Ordinal))
                return;

            if (string.Equals(_profileLoadingClientId, expectedClientId, StringComparison.Ordinal))
                    return;

            _profileLoadingClientId = expectedClientId;
            UpdateActionButtons();

            try
            {
                var profile = await _svc.GetClientProfileAsync(expectedClientId);
                if (!string.Equals(_selected?.Id, expectedClientId, StringComparison.Ordinal))
                    return;

                _selectedProfile = profile;
                _profileClientId = expectedClientId;
                PopulateClientDetails(_selected);
                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Profile error: {ex.Message}";
            }
            finally
            {
                if (string.Equals(_profileLoadingClientId, expectedClientId, StringComparison.Ordinal))
                    _profileLoadingClientId = null;
                UpdateActionButtons();
            }
        }

        private static TelemetryRecord? GetLatestStatsTelemetry(IEnumerable<TelemetryRecord> telemetry)
        {
            return telemetry
                .Where(t => t.EventData?.ValueKind == JsonValueKind.Object && HasStats(t.EventData.Value))
                .OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();
        }

        private static bool HasStats(JsonElement data)
        {
            return data.TryGetProperty("firms_count", out _) || data.TryGetProperty("employees_count", out _);
        }

        private void PopulateTelemetryEventFilter()
        {
            _isUpdatingTelemetryEventFilter = true;
            try
            {
                var selected = CmbTelemetryEvent.SelectedItem as string;
                var items = _activeTelemetry
                    .Select(t => t.EventType)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                items.Insert(0, "Усі події");
                CmbTelemetryEvent.ItemsSource = items;
                CmbTelemetryEvent.SelectedItem = items.Contains(selected ?? string.Empty) ? selected : items[0];
            }
            finally
            {
                _isUpdatingTelemetryEventFilter = false;
            }
        }

        private void ApplyTelemetryFilters()
        {
            if (_isUpdatingTelemetryEventFilter)
                return;

            IEnumerable<TelemetryRecord> filtered = _activeTelemetry;

            var preset = GetSelectedComboTag(CmbTelemetryPreset);
            filtered = preset switch
            {
                "heartbeat" => filtered.Where(t => string.Equals(t.EventType, "heartbeat", StringComparison.OrdinalIgnoreCase)),
                "errors" => filtered.Where(IsErrorLikeEvent),
                "license" => filtered.Where(t =>
                    t.EventType.Contains("license", StringComparison.OrdinalIgnoreCase) ||
                    t.EventType.Contains("activate", StringComparison.OrdinalIgnoreCase) ||
                    t.EventType.Contains("block", StringComparison.OrdinalIgnoreCase)),
                "firms" => filtered.Where(t => string.Equals(t.EventType, "firm_created", StringComparison.OrdinalIgnoreCase)),
                "employees" => filtered.Where(t => string.Equals(t.EventType, "employee_added", StringComparison.OrdinalIgnoreCase)),
                _ => filtered
            };

            var eventType = CmbTelemetryEvent.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(eventType) && !string.Equals(eventType, "Усі події", StringComparison.Ordinal))
                filtered = filtered.Where(t => string.Equals(t.EventType, eventType, StringComparison.OrdinalIgnoreCase));

            var query = (TxtTelemetrySearch.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(t =>
                    t.EventType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.EventTypeDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.MachineId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.IpAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.AppVersion.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.EventSummary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.EventDataDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered
                .OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue)
                .ToList();

            DgTelemetry.ItemsSource = filteredList;
            UpdateTelemetrySummary(filteredList);
        }

        private void UpdateTelemetrySummary(List<TelemetryRecord> telemetry)
        {
            if (_selected != null && !string.Equals(_telemetryClientId, _selected.Id, StringComparison.Ordinal))
            {
                TxtTelemetrySummary.Text = string.Equals(_telemetryLoadingClientId, _selected.Id, StringComparison.Ordinal)
                    ? "Завантаження подій..."
                    : "Події ще не завантажено для цього клієнта.";
                UpdateTelemetryPaginationButton();
                return;
            }

            var errorLike = telemetry.Count(IsErrorLikeEvent);
            var latest = telemetry.OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue).FirstOrDefault()?.CreatedAt;
            var latestText = latest.HasValue ? latest.Value.ToLocalTime().ToString("dd.MM HH:mm") : "—";
            var shownText = _telemetryHasMore
                ? $"Показано: {telemetry.Count}, є ще ->"
                : $"Показано: {telemetry.Count}";
            TxtTelemetrySummary.Text = $"{shownText} | Типів: {telemetry.Select(t => t.EventType).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | Error-like: {errorLike} | Остання: {latestText}";
            UpdateTelemetryPaginationButton();
        }

        private void UpdateTelemetryPaginationButton()
        {
            if (BtnLoadMoreTelemetry == null)
                return;

            var isLoadedForSelected = _selected != null && string.Equals(_telemetryClientId, _selected.Id, StringComparison.Ordinal);
            var isLoadingSelected = _selected != null && string.Equals(_telemetryLoadingClientId, _selected.Id, StringComparison.Ordinal);

            BtnLoadMoreTelemetry.IsEnabled = isLoadedForSelected && _telemetryHasMore && !isLoadingSelected;
            BtnLoadMoreTelemetry.Visibility = isLoadedForSelected && _telemetryHasMore ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void BtnLoadMoreTelemetry_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || !_telemetryHasMore || string.IsNullOrWhiteSpace(_telemetryNextCursor))
                return;

            if (string.Equals(_telemetryLoadingClientId, _selected.Id, StringComparison.Ordinal))
                return;

            var selectedId = _selected.Id;

            try
            {
                _telemetryLoadingClientId = selectedId;
                UpdateTelemetryPaginationButton();
                var page = await _svc.GetTelemetryPageAsync(selectedId, 200, _telemetryNextCursor);
                if (_selected?.Id != selectedId)
                    return;

                _activeTelemetry.AddRange(page.Items);
                _telemetryNextCursor = string.IsNullOrWhiteSpace(page.NextCursor) ? null : page.NextCursor;
                _telemetryHasMore = page.HasMore;
                _telemetryClientId = selectedId;
                RefreshSessionSummaries();
                UpdateStats(_activeTelemetry, selectedId);
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                RefreshActivityUi();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Telemetry paging error: {ex.Message}";
            }
            finally
            {
                if (string.Equals(_telemetryLoadingClientId, selectedId, StringComparison.Ordinal))
                    _telemetryLoadingClientId = null;
                UpdateTelemetryPaginationButton();
            }
        }

        private void RefreshSessionSummaries()
        {
            if (_selected == null)
            {
                DgSessions.ItemsSource = new List<SessionSummaryRow>();
                TxtSessionsSummary.Text = "Виберіть клієнта";
                return;
            }

            if (!string.Equals(_telemetryClientId, _selected.Id, StringComparison.Ordinal))
            {
                DgSessions.ItemsSource = new List<SessionSummaryRow>();
                TxtSessionsSummary.Text = string.Equals(_telemetryLoadingClientId, _selected.Id, StringComparison.Ordinal)
                    ? "Завантаження подій для побудови сесій..."
                    : "Сесії будуть побудовані після завантаження подій для цього клієнта.";
                return;
            }

            var sessions = BuildSessionSummaries(_activeTelemetry);
            DgSessions.ItemsSource = sessions;

            var firmsTotal = sessions.Sum(session => session.FirmsAdded);
            var employeesTotal = sessions.Sum(session => session.EmployeesAdded);
            var caveat = _telemetryHasMore
                ? $"на основі {_activeTelemetry.Count} завантажених подій, є ще"
                : $"на основі {_activeTelemetry.Count} завантажених подій";
            TxtSessionsSummary.Text = $"Сесій: {sessions.Count} | +Фірм: {firmsTotal} | +Працівників: {employeesTotal} | {caveat}";
        }

        private List<SessionSummaryRow> BuildSessionSummaries(IEnumerable<TelemetryRecord> telemetry)
        {
            var ordered = telemetry
                .Where(item => item.CreatedAt.HasValue)
                .OrderBy(item => item.CreatedAt!.Value)
                .ToList();

            if (ordered.Count == 0)
                return new List<SessionSummaryRow>();

            var sessions = new List<SessionSummaryRow>();
            SessionSummaryRow? current = null;

            foreach (var item in ordered)
            {
                var createdAt = item.CreatedAt!.Value.ToLocalTime();
                var hasLargeGap = current != null
                    && (createdAt - current.LastActivityAt).TotalMinutes > 10;
                var startsSession = string.Equals(item.EventType, "app_started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.EventType, "first_launch", StringComparison.OrdinalIgnoreCase)
                    || hasLargeGap;

                if (current == null || startsSession)
                {
                    if (current != null)
                        sessions.Add(current);

                    current = new SessionSummaryRow
                    {
                        StartedAt = createdAt,
                        LastActivityAt = createdAt,
                        AppVersion = item.AppVersion ?? string.Empty
                    };
                }

                current.LastActivityAt = createdAt;
                if (string.IsNullOrWhiteSpace(current.AppVersion) && !string.IsNullOrWhiteSpace(item.AppVersion))
                    current.AppVersion = item.AppVersion;

                current.EventCount++;

                if (string.Equals(item.EventType, "firm_created", StringComparison.OrdinalIgnoreCase))
                    current.FirmsAdded++;

                if (string.Equals(item.EventType, "employee_added", StringComparison.OrdinalIgnoreCase))
                    current.EmployeesAdded++;

                if (IsErrorLikeEvent(item))
                    current.ErrorCount++;
            }

            if (current != null)
                sessions.Add(current);

            string previousVersion = string.Empty;
            foreach (var session in sessions)
            {
                session.UpdateDisplay = string.IsNullOrWhiteSpace(previousVersion) || string.Equals(previousVersion, session.AppVersion, StringComparison.OrdinalIgnoreCase)
                    ? "—"
                    : $"{previousVersion} -> {session.AppVersion}";

                session.SessionSummary = BuildSessionSummary(session);
                previousVersion = session.AppVersion;
            }

            sessions.Reverse();
            return sessions;
        }

        private static string BuildSessionSummary(SessionSummaryRow session)
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(session.UpdateDisplay) && session.UpdateDisplay != "—")
                parts.Add($"оновлено {session.UpdateDisplay}");
            else
                parts.Add("вхід у програму");

            if (session.FirmsAdded > 0)
                parts.Add($"+{session.FirmsAdded} фірм");

            if (session.EmployeesAdded > 0)
                parts.Add($"+{session.EmployeesAdded} працівників");

            if (session.ErrorCount > 0)
                parts.Add($"помилок: {session.ErrorCount}");

            parts.Add($"подій: {session.EventCount}");
            return string.Join(" | ", parts);
        }

        private async Task EnsureSelectedClientActivityLoadedAsync(string expectedClientId)
        {
            if (string.IsNullOrWhiteSpace(expectedClientId))
                return;

            if (string.Equals(_activityClientId, expectedClientId, StringComparison.Ordinal) && !_activityLoadFailed)
                return;

            if (string.Equals(_activityLoadingClientId, expectedClientId, StringComparison.Ordinal))
                return;

            _activityLoadingClientId = expectedClientId;
            _activityLoadFailed = false;
            RefreshActivityUi();

            try
            {
                var snapshot = await _svc.GetClientMirrorSnapshotAsync(expectedClientId);
                if (!string.Equals(_selected?.Id, expectedClientId, StringComparison.Ordinal))
                    return;

                _activitySnapshot = snapshot;
                _activityClientId = expectedClientId;
                _activityLoadFailed = false;
                RefreshActivityUi();
            }
            catch (Exception ex)
            {
                if (!string.Equals(_selected?.Id, expectedClientId, StringComparison.Ordinal))
                    return;

                _activitySnapshot = null;
                _activityClientId = expectedClientId;
                _activityLoadFailed = true;
                TxtStatus.Text = $"Activity error: {ex.Message}";
                RefreshActivityUi();
            }
            finally
            {
                if (string.Equals(_activityLoadingClientId, expectedClientId, StringComparison.Ordinal))
                    _activityLoadingClientId = null;
                RefreshActivityUi();
            }
        }

        private void RefreshActivityUi()
        {
            if (TxtDetailActivity == null || TxtActivitySummary == null || DgActivity == null)
                return;

            if (_selected == null)
            {
                _allActivity = new List<ActivityRow>();
                DgActivity.ItemsSource = _allActivity;
                TxtActivitySummary.Text = "Виберіть клієнта";
                TxtDetailActivity.Text = "—";
                return;
            }

            var isLoading = string.Equals(_activityLoadingClientId, _selected.Id, StringComparison.Ordinal);
            var snapshotReady = string.Equals(_activityClientId, _selected.Id, StringComparison.Ordinal);
            var telemetryReady = string.Equals(_telemetryClientId, _selected.Id, StringComparison.Ordinal);

            if (!snapshotReady && !telemetryReady && isLoading)
            {
                _allActivity = new List<ActivityRow>();
                DgActivity.ItemsSource = _allActivity;
                TxtActivitySummary.Text = "Завантаження активності...";
                TxtDetailActivity.Text = "Завантаження активності...";
                return;
            }

            var telemetry = telemetryReady ? _activeTelemetry : new List<TelemetryRecord>();
            var snapshot = snapshotReady ? _activitySnapshot : null;
            _allActivity = BuildClientActivity(snapshot, telemetry);
            ApplyActivityFilters();
            TxtDetailActivity.Text = BuildActivityDetailSummary(_allActivity, isLoading, snapshotReady);
        }

        private void ApplyActivityFilters()
        {
            if (DgActivity == null || TxtActivitySummary == null)
                return;

            IEnumerable<ActivityRow> filtered = _allActivity;
            var kind = GetSelectedComboTag(CmbActivityKind);
            filtered = kind switch
            {
                "firm" => filtered.Where(row => row.Kind == "firm"),
                "employee" => filtered.Where(row => row.Kind == "employee"),
                "deleted" => filtered.Where(row => row.ActionCode == "deleted"),
                "app" => filtered.Where(row => row.Kind == "app"),
                _ => filtered
            };

            var query = (TxtActivitySearch?.Text ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                filtered = filtered.Where(row =>
                    row.ActionDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Subject.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Place.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.Details.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    row.DayDisplay.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = filtered.ToList();
            DgActivity.ItemsSource = filteredList;
            TxtActivitySummary.Text = BuildActivityTabSummary(filteredList);
        }

        private void ActivityFilter_Changed(object sender, EventArgs e)
        {
            if (!IsLoaded)
                return;

            ApplyActivityFilters();
        }

        private void BtnResetActivityFilters_Click(object sender, RoutedEventArgs e)
        {
            SelectComboTag(CmbActivityKind, "all");
            TxtActivitySearch.Text = string.Empty;
            ApplyActivityFilters();
        }

        private string BuildActivityTabSummary(List<ActivityRow> rows)
        {
            if (_selected == null)
                return "Виберіть клієнта";

            var isLoading = string.Equals(_activityLoadingClientId, _selected.Id, StringComparison.Ordinal);
            var snapshotReady = string.Equals(_activityClientId, _selected.Id, StringComparison.Ordinal);
            var telemetryReady = string.Equals(_telemetryClientId, _selected.Id, StringComparison.Ordinal);

            if (rows.Count == 0)
            {
                if (isLoading)
                    return "Завантаження активності...";
                if (_activityLoadFailed && snapshotReady)
                    return "Дзеркало недоступне. Показано лише події програми, якщо вони вже завантажені.";
                if (!snapshotReady && !telemetryReady)
                    return "Активність ще не завантажено.";
                return "Дій поки немає";
            }

            var last = rows[0];
            var extra = isLoading ? " | ще завантажується" : string.Empty;
            return $"Показано: {rows.Count} | Остання: {last.DateDisplay} — {last.ActionDisplay} {last.Subject}".Trim() + extra;
        }

        private string BuildActivityDetailSummary(List<ActivityRow> rows, bool isLoading, bool snapshotReady)
        {
            if (rows.Count == 0)
            {
                if (isLoading)
                    return "Завантаження активності...";
                if (_activityLoadFailed)
                    return "Не вдалося завантажити дзеркало. Відкрийте вкладку «Активність» після оновлення.";
                if (!snapshotReady)
                    return "Завантаження активності...";
                return "Дій у дзеркалі ще немає. Клієнт або ще не синхронізувався, або нічого не змінював.";
            }

            var last = rows[0];
            var parts = new List<string>();
            if (last.At.Date == DateTime.Today)
                parts.Add("Активний сьогодні");
            else if (last.At.Date == DateTime.Today.AddDays(-1))
                parts.Add("Остання дія вчора");
            else
                parts.Add($"Остання дія {last.At:dd.MM HH:mm}");

            var weekStart = DateTime.Now.AddDays(-7);
            var week = rows.Where(row => row.At >= weekStart).ToList();
            var addedFirms = week.Count(row => row.Kind == "firm" && row.ActionCode == "added");
            var addedEmployees = week.Count(row => row.Kind == "employee" && row.ActionCode == "added");
            var updated = week.Count(row => row.ActionCode == "updated" || row.ActionCode == "archived");
            var deleted = week.Count(row => row.ActionCode == "deleted");
            var weekParts = new List<string>();
            if (addedFirms > 0)
                weekParts.Add($"+{addedFirms} фірм");
            if (addedEmployees > 0)
                weekParts.Add($"+{addedEmployees} працівників");
            if (updated > 0)
                weekParts.Add($"{updated} змін");
            if (deleted > 0)
                weekParts.Add($"{deleted} видалень");

            parts.Add(weekParts.Count > 0
                ? "за 7 днів: " + string.Join(", ", weekParts)
                : "за 7 днів без змін у даних");
            parts.Add($"остання: {last.ActionDisplay} {last.Subject}".Trim());
            if (isLoading)
                parts.Add("оновлюється...");

            return string.Join(" · ", parts);
        }

        private List<ActivityRow> BuildClientActivity(ClientMirrorSnapshot? snapshot, IEnumerable<TelemetryRecord> telemetry)
        {
            var rows = new List<ActivityRow>();
            var employerNames = new Dictionary<Guid, string>();

            if (snapshot != null)
            {
                foreach (var employer in snapshot.Employers)
                    employerNames[employer.EmployerId] = employer.DisplayName;

                foreach (var agency in snapshot.Agencies)
                    AddMirrorEntityActivity(rows, "agency", agency.DisplayName, "", agency.SourceUpdatedAt, agency.DeletedAt, agency.IsDeleted);

                foreach (var employer in snapshot.Employers)
                    AddMirrorEntityActivity(rows, "firm", employer.DisplayName, "", employer.SourceUpdatedAt, employer.DeletedAt, employer.IsDeleted, employer.CreatedAt);

                foreach (var employee in snapshot.Employees)
                {
                    var place = !string.IsNullOrWhiteSpace(employee.EmployerDisplayName)
                        ? employee.EmployerDisplayName
                        : employee.EmployerId.HasValue && employerNames.TryGetValue(employee.EmployerId.Value, out var employerName)
                            ? employerName
                            : employee.ArchivedFromFirm;

                    if (employee.IsDeleted && employee.DeletedAt.HasValue)
                    {
                        AddActivity(rows, ToLocal(employee.DeletedAt), "employee", "deleted", "Видалив працівника", employee.FullName, place, "Видалено");
                        continue;
                    }

                    var at = ToLocal(employee.SourceUpdatedAt);
                    if (!at.HasValue)
                        continue;

                    if (employee.IsArchived)
                    {
                        var archiveDetails = string.IsNullOrWhiteSpace(place)
                            ? "В архіві"
                            : $"В архіві · {place}";
                        AddActivity(rows, at, "employee", "archived", "Перемістив в архів", employee.FullName, place, archiveDetails);
                    }
                    else
                    {
                        AddActivity(rows, at, "employee", "updated", "Оновив дані працівника", employee.FullName, place, "Редагування картки");
                    }
                }
            }

            foreach (var item in telemetry)
            {
                var at = ToLocal(item.CreatedAt);
                if (!at.HasValue)
                    continue;

                if (string.Equals(item.EventType, "heartbeat", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(item.EventType, "app_started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.EventType, "first_launch", StringComparison.OrdinalIgnoreCase))
                {
                    AddActivity(rows, at, "app", "started", "Запустив програму", item.AppVersion, "",
                        string.IsNullOrWhiteSpace(item.AppVersion) ? "" : $"версія {item.AppVersion}");
                    continue;
                }

                if (string.Equals(item.EventType, "app_updated", StringComparison.OrdinalIgnoreCase))
                {
                    var fromVersion = ReadTelemetryString(item, "from_version");
                    var toVersion = ReadTelemetryString(item, "to_version");
                    var details = !string.IsNullOrWhiteSpace(fromVersion) && !string.IsNullOrWhiteSpace(toVersion)
                        ? $"{fromVersion} → {toVersion}"
                        : item.AppVersion;
                    AddActivity(rows, at, "app", "updated", "Оновив програму", details, "", "");
                    continue;
                }

                if (string.Equals(item.EventType, "firm_created", StringComparison.OrdinalIgnoreCase))
                {
                    var firmName = ReadTelemetryString(item, "firm_name");
                    if (string.IsNullOrWhiteSpace(firmName))
                        firmName = "Фірма";
                    if (HasCloseActivity(rows, "firm", "added", firmName, at.Value))
                        continue;
                    AddActivity(rows, at, "firm", "added", "Додав фірму", firmName, "", "");
                    continue;
                }

                if (string.Equals(item.EventType, "employee_added", StringComparison.OrdinalIgnoreCase))
                {
                    var employeeName = ReadTelemetryString(item, "employee_name");
                    var firmName = ReadTelemetryString(item, "firm_name");
                    if (string.IsNullOrWhiteSpace(employeeName))
                        employeeName = "Працівник";
                    RemoveCloseActivity(rows, "employee", new[] { "updated", "archived" }, employeeName, at.Value);
                    AddActivity(rows, at, "employee", "added", "Додав працівника", employeeName, firmName, "Новий запис");
                }
            }

            return rows
                .OrderByDescending(row => row.At)
                .ThenBy(row => row.ActionDisplay, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddMirrorEntityActivity(
            List<ActivityRow> rows,
            string kind,
            string name,
            string place,
            DateTime? sourceUpdatedAt,
            DateTime? deletedAt,
            bool isDeleted,
            DateTime? createdAt = null)
        {
            var addedLabel = kind == "agency" ? "Додав агенцію" : "Додав фірму";
            var updatedLabel = kind == "agency" ? "Змінив агенцію" : "Змінив фірму";
            var deletedLabel = kind == "agency" ? "Видалив агенцію" : "Видалив фірму";

            var created = ToLocal(createdAt);
            var updated = ToLocal(sourceUpdatedAt);
            var deleted = ToLocal(deletedAt);

            if (created.HasValue)
                AddActivity(rows, created, kind, "added", addedLabel, name, place, "");

            if (isDeleted && deleted.HasValue)
                AddActivity(rows, deleted, kind, "deleted", deletedLabel, name, place, "");

            if (!updated.HasValue)
                return;

            if (created.HasValue && Math.Abs((updated.Value - created.Value).TotalMinutes) < 3)
                return;

            if (deleted.HasValue && Math.Abs((updated.Value - deleted.Value).TotalMinutes) < 3)
                return;

            AddActivity(rows, updated, kind, "updated", updatedLabel, name, place, "");
        }

        private static void AddActivity(
            List<ActivityRow> rows,
            DateTime? at,
            string kind,
            string actionCode,
            string actionDisplay,
            string subject,
            string place,
            string details)
        {
            if (!at.HasValue)
                return;

            rows.Add(new ActivityRow
            {
                At = at.Value,
                Kind = kind,
                ActionCode = actionCode,
                ActionDisplay = actionDisplay,
                Subject = string.IsNullOrWhiteSpace(subject) ? "—" : subject.Trim(),
                Place = place?.Trim() ?? string.Empty,
                Details = details?.Trim() ?? string.Empty
            });
        }

        private static bool HasCloseActivity(List<ActivityRow> rows, string kind, string actionCode, string subject, DateTime at)
        {
            return rows.Any(row =>
                row.Kind == kind
                && row.ActionCode == actionCode
                && string.Equals(row.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && Math.Abs((row.At - at).TotalMinutes) <= 20);
        }

        private static void RemoveCloseActivity(List<ActivityRow> rows, string kind, IEnumerable<string> actionCodes, string subject, DateTime at)
        {
            var codes = actionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            rows.RemoveAll(row =>
                row.Kind == kind
                && codes.Contains(row.ActionCode)
                && string.Equals(row.Subject, subject, StringComparison.OrdinalIgnoreCase)
                && Math.Abs((row.At - at).TotalMinutes) <= 20);
        }

        private static DateTime? ToLocal(DateTime? value)
        {
            if (!value.HasValue)
                return null;

            var at = value.Value;
            if (at.Kind == DateTimeKind.Unspecified)
                at = DateTime.SpecifyKind(at, DateTimeKind.Utc);

            return at.ToLocalTime();
        }

        private static string FormatActivityDay(DateTime at)
        {
            var day = at.Date;
            var today = DateTime.Today;
            if (day == today)
                return "Сьогодні";
            if (day == today.AddDays(-1))
                return "Вчора";
            return at.ToString("dd.MM.yyyy");
        }

        private static string ReadTelemetryString(TelemetryRecord telemetry, string propertyName)
        {
            if (telemetry.EventData?.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!telemetry.EventData.Value.TryGetProperty(propertyName, out var value))
                return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                _ => value.ToString()
            };
        }

        private void PopulateClientDetails(ClientRecord? client)
        {
            if (client == null)
            {
                TxtDetailHeader.Text = "Клієнт не вибраний";
                TxtDetailStatus.Text = "—";
                TxtDetailRisk.Text = "—";
                TxtDetailClientId.Text = "—";
                TxtDetailMachine.Text = "—";
                TxtDetailMachineId.Text = "—";
                TxtDetailIp.Text = "—";
                TxtDetailVersion.Text = "—";
                TxtDetailActivated.Text = "—";
                TxtDetailExpires.Text = "—";
                TxtDetailLastSeen.Text = "—";
                TxtDetailBlockReason.Text = "—";
                TxtDetailLicense.Text = "—";
                TxtDetailHeartbeat.Text = "—";
                TxtDetailProfileName.Text = "—";
                TxtDetailProfileStatus.Text = "—";
                TxtDetailRememberMe.Text = "—";
                TxtDetailProfileUpdatedAt.Text = "—";
                SetAccessConfigFields(() =>
                {
                    SelectComboTag(CmbAccessPlan, "trial");
                    TxtManagedGeminiKey.Text = string.Empty;
                    BusinessTenantPanel.Visibility = Visibility.Collapsed;
                    TxtBusinessMaxUsers.Text = "10";
                    TxtBusinessMaxDevices.Text = "3";
                    ChkBusinessMultiUserEnabled.IsChecked = true;
                    SelectComboTag(CmbBusinessTenantStatus, "active");
                    TxtBusinessTenantInfo.Text = "Tenant буде створено при збереженні";
                });
                TxtNotes.Text = string.Empty;
                BtnSaveNotes.IsEnabled = false;
                BtnSaveAccessConfig.IsEnabled = false;
                RefreshActivityUi();
                return;
            }

            TxtDetailHeader.Text = client.DisplayName;
            TxtDetailStatus.Text = client.AccessStateLabel;
            TxtDetailStatus.Foreground = client.AccessStateCode switch
            {
                "blocked" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8")),
                "readonly" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAB387")),
                "trial" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9E2AF")),
                "activated" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"))
            };
            TxtDetailRisk.Text = client.RiskReasons == null || client.RiskReasons.Count == 0
                ? client.RiskDisplay
                : $"{client.RiskDisplay}: {string.Join("; ", client.RiskReasons)}";
            TxtDetailClientId.Text = client.Id;
            TxtDetailMachine.Text = client.MachineName;
            TxtDetailMachineId.Text = client.MachineId;
            TxtDetailIp.Text = string.IsNullOrWhiteSpace(client.IpAddress) ? "—" : client.IpAddress;
            TxtDetailVersion.Text = string.IsNullOrWhiteSpace(client.AppVersion)
                ? "—"
                : client.IsOutdatedVersion ? $"{client.AppVersion} (outdated)" : client.AppVersion;
            TxtDetailActivated.Text = FormatDate(client.ActivatedAt, "dd.MM.yyyy");
            TxtDetailExpires.Text = FormatDate(client.ExpiresAt, "dd.MM.yyyy");
            TxtDetailLastSeen.Text = FormatDate(client.LastSeen?.ToLocalTime(), "dd.MM.yyyy HH:mm");
            TxtDetailBlockReason.Text = string.IsNullOrWhiteSpace(client.BlockReason) ? "—" : client.BlockReason;
            TxtDetailLicense.Text = client.AccessStateDetail;
            TxtDetailHeartbeat.Text = BuildLatestStateSummary();
            TxtDetailProfileName.Text = _selectedProfile == null
                ? (string.IsNullOrWhiteSpace(client.ProfileFullName) ? "Профіль не створено" : client.ProfileFullName)
                : $"{_selectedProfile.FirstName} {_selectedProfile.LastName}".Trim();
            TxtDetailProfileStatus.Text = _selectedProfile == null
                ? (string.IsNullOrWhiteSpace(client.ProfileFullName) ? "Профіль не створено" : "Деталі завантажуються на вимогу")
                : _selectedProfile.MustResetPassword ? "Очікує примусового reset" : "Активний";
            TxtDetailRememberMe.Text = _selectedProfile == null
                ? (string.IsNullOrWhiteSpace(client.ProfileFullName) ? "—" : "На вимогу")
                : _selectedProfile.RememberMeEnabled ? "Увімкнено" : "Вимкнено";
            TxtDetailProfileUpdatedAt.Text = _selectedProfile?.UpdatedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                ?? (string.IsNullOrWhiteSpace(client.ProfileFullName) ? "—" : "На вимогу");
            SetAccessConfigFields(() =>
            {
                SelectComboTag(CmbAccessPlan, NormalizeClientPlan(client.Plan));
                TxtManagedGeminiKey.Text = client.GeminiApiKey ?? string.Empty;
                ApplyBusinessTenantFields(client, _loadedTenant);
                UpdateBusinessPanelVisibility();
            });
            TxtNotes.Text = client.Notes ?? string.Empty;
            BtnSaveNotes.IsEnabled = HasNotesChanged();
            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
            RefreshActivityUi();
        }

        private void SetAccessConfigFields(Action apply)
        {
            _isPopulatingAccessConfig = true;
            try
            {
                apply();
            }
            finally
            {
                _isPopulatingAccessConfig = false;
            }
        }

        private bool CanReactToAccessConfigChanges()
        {
            return IsLoaded && !_isPopulatingAccessConfig;
        }

        private void UpdateBusinessPanelVisibility()
        {
            BusinessTenantPanel.Visibility = GetSelectedAccessPlan() == "business"
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void ApplyBusinessTenantFields(ClientRecord? client, TenantRecord? tenant)
        {
            if (client == null)
                return;

            if (tenant != null && string.Equals(tenant.SupabaseClientId, client.Id, StringComparison.Ordinal))
            {
                TxtBusinessMaxUsers.Text = tenant.MaxUsers.ToString();
                TxtBusinessMaxDevices.Text = tenant.MaxDevices.ToString();
                ChkBusinessMultiUserEnabled.IsChecked = tenant.MultiUserEnabled;
                SelectComboTag(CmbBusinessTenantStatus, NormalizeTenantStatus(tenant.Status));
                var updatedDisplay = tenant.UpdatedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
                TxtBusinessTenantInfo.Text = string.IsNullOrWhiteSpace(tenant.Id)
                    ? "Tenant буде створено при збереженні"
                    : $"Tenant ID: {tenant.Id} | plan_key: {tenant.PlanKey} | оновлено: {updatedDisplay}";
                return;
            }

            TxtBusinessMaxUsers.Text = "10";
            TxtBusinessMaxDevices.Text = "3";
            ChkBusinessMultiUserEnabled.IsChecked = true;
            SelectComboTag(CmbBusinessTenantStatus, "active");
            TxtBusinessTenantInfo.Text = NormalizeClientPlan(client.Plan) == "business"
                ? "Tenant буде створено при збереженні"
                : "Tenant не налаштовано";
        }

        private string BuildLatestStateSummary()
        {
            var latestStats = GetLatestStatsTelemetry(_activeTelemetry);

            ExtractStats(latestStats, out var firms, out var employees);
            if (latestStats == null && _selected != null)
                return $"Фірм: {_selected.FirmsCount}, працівників: {_selected.EmployeesCount}";

            return latestStats == null ? "—" : $"Фірм: {firms}, працівників: {employees}";
        }

        private static string FormatDate(DateTime? value, string format)
        {
            return value.HasValue ? value.Value.ToString(format) : "—";
        }

        private static bool IsErrorLikeEvent(TelemetryRecord telemetry)
        {
            return telemetry.EventType.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                   telemetry.EventDataDisplay.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                   telemetry.EventDataDisplay.Contains("exception", StringComparison.OrdinalIgnoreCase);
        }

        private void ClientFilter_Changed(object sender, EventArgs e)
        {
            if (!IsLoaded || _isRestoringFilters)
                return;

            ApplyClientFilters();
        }

        private void DashboardFilterCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tag)
                return;

            var parts = tag.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                return;

            switch (parts[0])
            {
                case "license":
                    ToggleComboTag(CmbLicenseFilter, parts[1], "all");
                    break;
                default:
                    return;
            }

            ApplyClientFilters();
            TxtStatus.Text = $"Фільтр: {GetDashboardFilterLabel(tag)}";
        }

        private void BtnResetClientFilters_Click(object sender, RoutedEventArgs e)
        {
            TxtClientSearch.Text = string.Empty;
            SelectComboTag(CmbClientStatus, "all");
            SelectComboTag(CmbLicenseFilter, "all");
            SelectComboTag(CmbActivityFilter, "all");
            if (CmbVersionFilter.Items.Count > 0)
                CmbVersionFilter.SelectedIndex = 0;
            ApplyClientFilters();
        }

        private void TelemetryFilter_Changed(object sender, EventArgs e)
        {
            if (!IsLoaded)
                return;

            ApplyTelemetryFilters();
        }

        private void BtnResetTelemetryFilters_Click(object sender, RoutedEventArgs e)
        {
            SelectComboTag(CmbTelemetryPreset, "all");
            TxtTelemetrySearch.Text = string.Empty;
            if (CmbTelemetryEvent.Items.Count > 0)
                CmbTelemetryEvent.SelectedIndex = 0;
            ApplyTelemetryFilters();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void BtnSaveNotes_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            try
            {
                var selectedId = _selected.Id;
                var previousNotes = _selected.Notes ?? string.Empty;
                var nextNotes = TxtNotes.Text.Trim();
                await _svc.UpdateNotesAsync(selectedId, nextNotes);
                await _svc.TryWriteAuditAsync(selectedId, "notes_updated",
                    new { notes = previousNotes }, new { notes = nextNotes }, "Оператор оновив нотатки");
                await RefreshAsync(selectedId);
                TxtStatus.Text = $"Нотатки збережено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                ShowActionError("зберегти нотатки", ex);
            }
        }

        private void TxtNotes_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnSaveNotes.IsEnabled = HasNotesChanged();
        }

        private void CmbAccessPlan_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!CanReactToAccessConfigChanges())
                return;

            UpdateBusinessPanelVisibility();
            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
        }

        private void BusinessTenantField_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!CanReactToAccessConfigChanges())
                return;

            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
        }

        private void BusinessTenantField_Changed(object sender, RoutedEventArgs e)
        {
            if (!CanReactToAccessConfigChanges())
                return;

            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
        }

        private void BusinessTenantField_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!CanReactToAccessConfigChanges())
                return;

            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
        }

        private void TxtManagedGeminiKey_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnSaveAccessConfig.IsEnabled = HasAccessConfigChanged();
        }

        private async void BtnSaveAccessConfig_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            try
            {
                var selectedId = _selected.Id;
                var previousPlan = NormalizeClientPlan(_selected.Plan);
                var previousHasManagedKey = !string.IsNullOrWhiteSpace(_selected.GeminiApiKey);
                var nextPlan = GetSelectedAccessPlan();
                var nextManagedKey = (TxtManagedGeminiKey.Text ?? string.Empty).Trim();
                BusinessTenantAccessConfig? businessConfig = null;
                if (nextPlan == "business")
                {
                    if (!TryBuildBusinessTenantConfig(_selected, out businessConfig, out var validationError))
                    {
                        MessageBox.Show(validationError, "Business workspace", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                var updateResult = await _svc.UpdateClientAccessAsync(selectedId, nextPlan, nextManagedKey, businessConfig);
                var savedPlan = NormalizeClientPlan(updateResult?.Plan ?? string.Empty);
                if (!string.Equals(savedPlan, nextPlan, StringComparison.Ordinal))
                {
                    MessageBox.Show(
                        $"Сервер зберег план \"{savedPlan}\" замість \"{nextPlan}\".\n\n" +
                        "Ймовірно на Supabase ще стара версія admin-gateway без підтримки Business.\n" +
                        "Запустіть deploy:\n" +
                        "supabase\\deploy-admin-gateway.ps1",
                        "План не збережено",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    await RefreshAsync(selectedId);
                    return;
                }

                _tenantClientId = null;
                await _svc.TryWriteAuditAsync(selectedId, "client_access_updated",
                    new
                    {
                        plan = previousPlan,
                        has_managed_ai_key = previousHasManagedKey,
                        tenant = BuildTenantAuditSnapshot(_loadedTenant)
                    },
                    new
                    {
                        plan = nextPlan,
                        has_managed_ai_key = !string.IsNullOrWhiteSpace(nextManagedKey),
                        tenant = businessConfig == null
                            ? null
                            : new
                            {
                                max_users = businessConfig.MaxUsers,
                                max_devices = businessConfig.MaxDevices,
                                multi_user_enabled = businessConfig.MultiUserEnabled,
                                status = businessConfig.TenantStatus
                            }
                    },
                    nextPlan == "business"
                        ? "Оператор оновив план клієнта на Business та workspace tenant"
                        : "Оператор оновив план клієнта та managed Gemini key");
                await RefreshAsync(selectedId);
                TxtStatus.Text = $"План та AI оновлено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                ShowActionError("оновити план та AI", ex);
            }
        }

        private bool HasNotesChanged()
        {
            if (_selected == null)
                return false;

            return !string.Equals(
                (_selected.Notes ?? string.Empty).Trim(),
                (TxtNotes.Text ?? string.Empty).Trim(),
                StringComparison.Ordinal);
        }

        private bool HasAccessConfigChanged()
        {
            if (_selected == null)
                return false;

            return !string.Equals(NormalizeClientPlan(_selected.Plan), GetSelectedAccessPlan(), StringComparison.Ordinal)
                || !string.Equals((_selected.GeminiApiKey ?? string.Empty).Trim(), (TxtManagedGeminiKey.Text ?? string.Empty).Trim(), StringComparison.Ordinal)
                || HasBusinessTenantChanged();
        }

        private bool HasBusinessTenantChanged()
        {
            if (GetSelectedAccessPlan() != "business")
                return false;

            if (!TryBuildBusinessTenantConfig(_selected, out var nextConfig, out _))
                return true;

            if (_loadedTenant == null || !string.Equals(_loadedTenant.SupabaseClientId, _selected?.Id, StringComparison.Ordinal))
                return true;

            return _loadedTenant.MaxUsers != nextConfig!.MaxUsers
                || _loadedTenant.MaxDevices != nextConfig.MaxDevices
                || _loadedTenant.MultiUserEnabled != nextConfig.MultiUserEnabled
                || !string.Equals(NormalizeTenantStatus(_loadedTenant.Status), nextConfig.TenantStatus, StringComparison.Ordinal);
        }

        private bool TryBuildBusinessTenantConfig(
            ClientRecord? client,
            out BusinessTenantAccessConfig? config,
            out string validationError)
        {
            config = null;
            validationError = string.Empty;

            if (client == null)
            {
                validationError = "Клієнт не вибраний.";
                return false;
            }

            if (!int.TryParse((TxtBusinessMaxUsers.Text ?? string.Empty).Trim(), out var maxUsers) || maxUsers < 1)
            {
                validationError = "Max users має бути цілим числом не менше 1.";
                return false;
            }

            if (!int.TryParse((TxtBusinessMaxDevices.Text ?? string.Empty).Trim(), out var maxDevices) || maxDevices < 1)
            {
                validationError = "Max devices має бути цілим числом не менше 1.";
                return false;
            }

            config = new BusinessTenantAccessConfig
            {
                MaxUsers = maxUsers,
                MaxDevices = maxDevices,
                MultiUserEnabled = ChkBusinessMultiUserEnabled.IsChecked == true,
                TenantStatus = NormalizeTenantStatus(GetSelectedComboTag(CmbBusinessTenantStatus)),
                TenantName = client.DisplayName
            };
            return true;
        }

        private static object? BuildTenantAuditSnapshot(TenantRecord? tenant)
        {
            if (tenant == null)
                return null;

            return new
            {
                id = tenant.Id,
                max_users = tenant.MaxUsers,
                max_devices = tenant.MaxDevices,
                multi_user_enabled = tenant.MultiUserEnabled,
                status = tenant.Status,
                plan_key = tenant.PlanKey
            };
        }

        private static string NormalizeTenantStatus(string? status)
        {
            return (status ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "suspended" => "suspended",
                "blocked" => "blocked",
                "expired" => "expired",
                "trial" => "trial",
                _ => "active"
            };
        }

        private string GetSelectedAccessPlan()
        {
            return NormalizeClientPlan(GetSelectedComboTag(CmbAccessPlan));
        }

        private static string NormalizeClientPlan(string? plan)
        {
            return (plan ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "standard" => "standard",
                "pro" => "pro",
                "business" => "business",
                _ => "trial"
            };
        }

        private static void ShowActionError(string action, Exception ex)
        {
            MessageBox.Show($"Не вдалося {action}.\n\n{DescribeOperatorError(ex)}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private static string DescribeOperatorError(Exception ex)
        {
            return ex switch
            {
                TaskCanceledException => "Сервер не відповів вчасно. Спробуйте ще раз.",
                HttpRequestException => "Не вдалося зв'язатися із сервером. Перевірте підключення та повторіть спробу.",
                JsonException => "Отримано некоректну відповідь від сервера. Оновіть дані ще раз.",
                _ => string.IsNullOrWhiteSpace(ex.Message)
                    ? "Сталася внутрішня помилка. Перевірте журнал і повторіть спробу."
                    : ex.Message
            };
        }

        private async void BtnBlock_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var reason = PromptInput("Причина блокування:", "Заблокувати клієнта");
            if (reason == null)
                return;

            try
            {
                await _svc.BlockClientAsync(_selected.Id, reason);
                await _svc.TryWriteAuditAsync(_selected.Id, "client_blocked",
                    new { is_blocked = false, block_reason = _selected.BlockReason }, new { is_blocked = true, block_reason = reason }, reason);
                await RefreshAsync(_selected.Id);
            }
            catch (Exception ex)
            {
                ShowActionError("заблокувати клієнта", ex);
            }
        }

        private async void BtnUnblock_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            if (MessageBox.Show($"Розблокувати {_selected.MachineName}?", "Підтвердження",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                await _svc.UnblockClientAsync(_selected.Id);
                await _svc.TryWriteAuditAsync(_selected.Id, "client_unblocked",
                    new { is_blocked = true, block_reason = _selected.BlockReason }, new { is_blocked = false, block_reason = (string?)null }, "Розблоковано");
                await RefreshAsync(_selected.Id);
            }
            catch (Exception ex)
            {
                ShowActionError("розблокувати клієнта", ex);
            }
        }

        private async void BtnBlockIp_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || string.IsNullOrWhiteSpace(_selected.IpAddress))
                return;

            var ip = _selected.IpAddress;
            if (MessageBox.Show($"Заблокувати всіх клієнтів з IP {ip}?", "Блокування по IP",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                await _svc.BlockByIpAsync(ip, $"IP block: {ip}");
                await _svc.TryWriteAuditAsync(_selected.Id, "ip_blocked",
                    new { ip }, new { ip, is_blocked = true }, $"IP block: {ip}");
                await RefreshAsync(_selected.Id);
            }
            catch (Exception ex)
            {
                ShowActionError("заблокувати клієнтів за IP", ex);
            }
        }

        private async void BtnExtend_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var input = PromptInput("На скільки днів активувати або продовжити доступ:", "Активувати доступ", "365");
            if (input == null || !int.TryParse(input, out var days) || days <= 0)
                return;

            try
            {
                var baseDate = _selected.ExpiresAt.HasValue && _selected.ExpiresAt.Value > DateTime.UtcNow
                    ? _selected.ExpiresAt.Value
                    : DateTime.UtcNow;
                var newExpiry = baseDate.AddDays(days);
                await _svc.ExtendLicenseAsync(_selected.Id, newExpiry);
                await _svc.TryWriteAuditAsync(_selected.Id, "license_extended",
                    new { expires_at = _selected.ExpiresAt }, new { expires_at = newExpiry }, $"Доступ активовано/продовжено на {days} днів");
                MessageBox.Show($"Доступ активовано до {newExpiry:dd.MM.yyyy}", "Готово",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshAsync(_selected.Id);
            }
            catch (Exception ex)
            {
                ShowActionError("продовжити доступ", ex);
            }
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            if (MessageBox.Show(
                    $"Видалити клієнта {_selected.MachineName} ({_selected.MachineId})?\nЦя дія також видалить усю телеметрію.",
                    "Підтвердження видалення",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var confirmation = PromptInput(
                $"Для остаточного видалення введіть Machine ID:\n{_selected.MachineId}",
                "Безпечне видалення");

            if (!string.Equals((confirmation ?? string.Empty).Trim(), _selected.MachineId, StringComparison.Ordinal))
            {
                MessageBox.Show("Machine ID не співпав. Видалення скасовано.", "Скасовано",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var snapshot = new
                {
                    id = _selected.Id,
                    machine_id = _selected.MachineId,
                    machine_name = _selected.MachineName,
                    ip_address = _selected.IpAddress,
                    app_version = _selected.AppVersion
                };
                await _svc.DeleteClientAsync(_selected.Id);
                await _svc.TryWriteAuditAsync(_selected.Id, "client_deleted", snapshot, null, "Видалено клієнта і telemetry");
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                ShowActionError("видалити клієнта", ex);
            }
        }

        private async void BtnResetProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var selectedId = _selected.Id;
            await EnsureSelectedClientProfileLoadedAsync(selectedId);
            if (!string.Equals(_selected?.Id, selectedId, StringComparison.Ordinal))
                return;

            if (_selectedProfile == null)
            {
                MessageBox.Show("У цього клієнта ще немає створеного профілю.", "Профіль відсутній",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var selectedMachineName = _selected?.MachineName ?? "цього клієнта";

            if (MessageBox.Show(
                    $"Скинути пароль профілю для {selectedMachineName}?\nКористувач при наступному запуску повинен буде задати новий пароль.",
                    "Скидання пароля профілю",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                var before = new
                {
                    must_reset_password = _selectedProfile.MustResetPassword,
                    remember_me_enabled = _selectedProfile.RememberMeEnabled,
                    session_version = _selectedProfile.SessionVersion
                };

                var updatedProfile = await _svc.ResetClientProfilePasswordAsync(selectedId);
                if (updatedProfile == null)
                {
                    MessageBox.Show("У цього клієнта ще немає створеного профілю.", "Профіль відсутній",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await _svc.TryWriteAuditAsync(selectedId, "profile_password_reset",
                    before,
                    new
                    {
                        must_reset_password = updatedProfile.MustResetPassword,
                        remember_me_enabled = updatedProfile.RememberMeEnabled,
                        session_version = updatedProfile.SessionVersion
                    },
                    "Адміністратор скинув пароль профілю");

                MessageBox.Show("Пароль профілю скинуто. При наступному запуску клієнт повинен буде ввести новий пароль.",
                    "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

                await RefreshAsync(selectedId);
            }
            catch (Exception ex)
            {
                ShowActionError("скинути пароль профілю", ex);
            }
        }

        private static int GetDaysUntilExpiry(ClientRecord client)
        {
            if (!client.ExpiresAt.HasValue)
                return int.MaxValue;

            return (int)(client.ExpiresAt.Value.Date - DateTime.UtcNow.Date).TotalDays;
        }

        private static bool IsExpiringWithin(ClientRecord client, int days)
        {
            if (!client.ExpiresAt.HasValue)
                return false;

            var remaining = GetDaysUntilExpiry(client);
            return remaining >= 0 && remaining <= days;
        }

        private static string GetSelectedComboTag(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        }

        private static void SelectComboTag(ComboBox comboBox, string tag)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem comboItem && string.Equals(comboItem.Tag as string, tag, StringComparison.Ordinal))
                {
                    comboBox.SelectedItem = comboItem;
                    return;
                }
            }
        }

        private static void ToggleComboTag(ComboBox comboBox, string tag, string defaultTag)
        {
            var currentTag = GetSelectedComboTag(comboBox);
            SelectComboTag(comboBox, string.Equals(currentTag, tag, StringComparison.Ordinal) ? defaultTag : tag);
        }

        private static string GetDashboardFilterLabel(string tag)
        {
            return tag switch
            {
                "license:7" => "Ліцензія <= 7 днів",
                _ => "Оновлено"
            };
        }

        private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                await RefreshAsync();
                return;
            }

            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                TxtClientSearch.Focus();
                TxtClientSearch.SelectAll();
                return;
            }

            if (e.Key == Key.Enter && DgClients.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                OpenSelectedClientMirror();
                return;
            }

            if (e.Key != Key.Escape || IsTypingInEditor())
                return;

            if (TxtTelemetrySearch.IsKeyboardFocusWithin || CmbTelemetryPreset.IsKeyboardFocusWithin || CmbTelemetryEvent.IsKeyboardFocusWithin)
            {
                BtnResetTelemetryFilters_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (TxtActivitySearch.IsKeyboardFocusWithin || CmbActivityKind.IsKeyboardFocusWithin)
            {
                BtnResetActivityFilters_Click(sender, e);
                e.Handled = true;
                return;
            }

            BtnResetClientFilters_Click(sender, e);
            e.Handled = true;
        }

        private bool IsTypingInEditor()
        {
            return TxtNotes.IsKeyboardFocusWithin
                || TxtManagedGeminiKey.IsKeyboardFocusWithin
                || TxtBusinessMaxUsers.IsKeyboardFocusWithin
                || TxtBusinessMaxDevices.IsKeyboardFocusWithin;
        }

        private void DgClients_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.Item is ClientRecord client)
                DgClients.SelectedItem = client;
        }

        private void ClientGridContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            var hasSelection = _selected != null;
            MenuCopyIp.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selected?.IpAddress);
            MenuBlockClient.IsEnabled = hasSelection && _selected?.IsBlocked == false;
            MenuUnblockClient.IsEnabled = hasSelection && _selected?.IsBlocked == true;
        }

        private void MenuOpenMirror_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedClientMirror();
        }

        private void MenuCopyClientId_Click(object sender, RoutedEventArgs e)
        {
            CopyClientField(_selected?.Id, "Client ID");
        }

        private void MenuCopyMachineId_Click(object sender, RoutedEventArgs e)
        {
            CopyClientField(_selected?.MachineId, "Machine ID");
        }

        private void MenuCopyIp_Click(object sender, RoutedEventArgs e)
        {
            CopyClientField(_selected?.IpAddress, "IP");
        }

        private void CopyClientField(string? value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            Clipboard.SetText(value);
            TxtStatus.Text = $"Скопійовано {label}";
        }

        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match)
                    return match;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        private string? PromptInput(string message, string title, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 440,
                MinHeight = 200,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            var label = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            var textBox = new TextBox
            {
                Text = defaultValue,
                FontSize = 13,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Color.FromRgb(0x31, 0x32, 0x44)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x5B, 0x70)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4))
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var cancel = new Button
            {
                Content = "Скасувати",
                Width = 110,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true,
                Background = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4))
            };
            var ok = new Button
            {
                Content = "OK",
                Width = 90,
                Padding = new Thickness(0, 6, 0, 6),
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                FontWeight = FontWeights.SemiBold
            };
            cancel.Click += (_, _) => { dialog.DialogResult = false; dialog.Close(); };
            ok.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, _) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            return dialog.ShowDialog() == true ? textBox.Text : null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        private readonly SupabaseService _svc;
        private ClientRecord? _selected;
        private ClientProfileRecord? _selectedProfile;
        private List<ClientRecord> _allClients = new();
        private List<TelemetryRecord> _allTelemetry = new();
        private List<TelemetryRecord> _activeTelemetry = new();
        private bool _isUpdatingTelemetryEventFilter;
        private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

        private const string BaseUrl = "https://tssgxhatnjvqthdiyuwo.supabase.co";
        private const string ServiceKey =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRzc2d4aGF0bmp2cXRoZGl5dXdvIiwi" +
            "cm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc3MjY3NTE4MSwiZXhwIjoyMDg4MjUxMTgxfQ." +
            "3FvcQIgE8617lsBaLgbbjWSsLD9Uug_lDQ-D03QZofA";
        private static readonly JsonSerializerOptions LayoutJsonOptions = new() { WriteIndented = true };

        public MainWindow()
        {
            InitializeComponent();
            _svc = new SupabaseService(BaseUrl, ServiceKey);
            StateChanged += (_, _) =>
            {
                if (WindowState != WindowState.Minimized)
                    _lastNonMinimizedWindowState = WindowState;
            };
            Loaded += async (_, _) =>
            {
                RestoreClientGridLayout();
                await RefreshAsync();
            };
            Closing += (_, _) => SaveClientGridLayout();
        }

        private async Task RefreshAsync(string? preferredClientId = null)
        {
            try
            {
                TxtStatus.Text = "Завантаження...";

                var selectedId = preferredClientId ?? _selected?.Id;
                var clientsTask = _svc.GetClientsAsync();
                var telemetryTask = _svc.GetTelemetryAsync(limit: 500);
                var profilesTask = _svc.GetClientProfilesAsync();
                await Task.WhenAll(clientsTask, telemetryTask, profilesTask);

                _allClients = clientsTask.Result;
                _allTelemetry = telemetryTask.Result;
                ApplyProfilesToClients(profilesTask.Result);
                EnrichClientsWithRiskSignals();

                PopulateVersionFilter();
                ApplyClientFilters(restoreSelection: false);
                RestoreClientSelection(selectedId);

                if (_selected == null)
                {
                    _selectedProfile = null;
                    _activeTelemetry = _allTelemetry;
                    RefreshSessionSummaries();
                    PopulateTelemetryEventFilter();
                    ApplyTelemetryFilters();
                    UpdateStats(_allTelemetry, null);
                    PopulateClientDetails(null);
                    UpdateActionButtons();
                }

                TxtStatus.Text = $"Оновлено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Помилка: {ex.Message}";
                MessageBox.Show($"Не вдалося завантажити дані:\n{ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyProfilesToClients(List<ClientProfileRecord> profiles)
        {
            var profilesByClientId = profiles
                .Where(profile => !string.IsNullOrWhiteSpace(profile.ClientId))
                .GroupBy(profile => profile.ClientId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var client in _allClients)
            {
                if (!profilesByClientId.TryGetValue(client.Id, out var profile))
                    continue;

                client.ProfileFirstName = profile.FirstName ?? string.Empty;
                client.ProfileLastName = profile.LastName ?? string.Empty;
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
                RestoreWindowLayout(settings);
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
                var settings = new AdminPanelLayoutSettings
                {
                    WindowWidth = bounds.Width,
                    WindowHeight = bounds.Height,
                    WindowLeft = bounds.Left,
                    WindowTop = bounds.Top,
                    WindowState = windowStateToSave == WindowState.Maximized
                        ? nameof(System.Windows.WindowState.Maximized)
                        : nameof(System.Windows.WindowState.Normal),
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
                _selectedProfile = null;
                _activeTelemetry = _allTelemetry;
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                UpdateStats(_allTelemetry, null);
                PopulateClientDetails(null);
                UpdateActionButtons();
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
            DgClients.ItemsSource = filteredList;
            UpdateClientCounters(filteredList);

            if (restoreSelection && !string.IsNullOrWhiteSpace(selectedId))
                DgClients.SelectedItem = filteredList.FirstOrDefault(c => string.Equals(c.Id, selectedId, StringComparison.Ordinal));
        }

        private void UpdateClientCounters(List<ClientRecord> filtered)
        {
            var expiringSoon = filtered.Count(c => IsExpiringWithin(c, 7));
            TxtCount.Text = $"Показано: {filtered.Count}/{_allClients.Count} | Заблокованих: {filtered.Count(c => c.IsBlocked)} | <=7 днів: {expiringSoon}";
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
                ExtractStats(latestStats, out totalFirms, out totalEmployees);
            }
            else
            {
                var byClient = telemetry
                    .Where(t => t.ClientId != null)
                    .GroupBy(t => t.ClientId);

                foreach (var group in byClient)
                {
                    var latest = GetLatestStatsTelemetry(group);
                    ExtractStats(latest, out var firms, out var employees);
                    totalFirms += firms;
                    totalEmployees += employees;
                }
            }

            var label = clientId != null ? " (клієнт)" : " (всі)";
            TxtStatFirms.Text = $"Фірм: {totalFirms} (створено: +{firmsCreated}){label}";
            TxtStatEmployees.Text = $"Працівників: {totalEmployees} (додано: +{employeesAdded}){label}";
            TxtStatEvents.Text = $"Подій: {filtered.Count}";
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
            _selectedProfile = null;
            UpdateActionButtons();
            PopulateClientDetails(_selected);
            _ = LoadSelectedClientDataAsync();
        }

        private void UpdateActionButtons()
        {
            var hasSelection = _selected != null;
            BtnBlock.IsEnabled = hasSelection && _selected?.IsBlocked == false;
            BtnUnblock.IsEnabled = hasSelection && _selected?.IsBlocked == true;
            BtnBlockIp.IsEnabled = hasSelection && !string.IsNullOrWhiteSpace(_selected?.IpAddress);
            BtnExtend.IsEnabled = hasSelection;
            BtnDelete.IsEnabled = hasSelection;
            BtnResetProfile.IsEnabled = hasSelection && _selectedProfile != null;
            BtnSaveNotes.IsEnabled = hasSelection && HasNotesChanged();
        }

        private async Task LoadSelectedClientDataAsync()
        {
            try
            {
                if (_selected == null)
                {
                    _selectedProfile = null;
                    _activeTelemetry = _allTelemetry;
                    RefreshSessionSummaries();
                    PopulateTelemetryEventFilter();
                    ApplyTelemetryFilters();
                    UpdateStats(_allTelemetry, null);
                    PopulateClientDetails(null);
                    return;
                }

                var selectedId = _selected.Id;
                var telemetryTask = _svc.GetTelemetryAsync(selectedId, 500);
                var profileTask = _svc.GetClientProfileAsync(selectedId);
                await Task.WhenAll(telemetryTask, profileTask);

                if (_selected?.Id != selectedId)
                    return;

                _activeTelemetry = telemetryTask.Result;
                _selectedProfile = profileTask.Result;

                RefreshSessionSummaries();
                UpdateStats(_activeTelemetry, selectedId);
                PopulateClientDetails(_selected);
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                UpdateActionButtons();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Telemetry error: {ex.Message}";
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
            var errorLike = telemetry.Count(IsErrorLikeEvent);
            var latest = telemetry.OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue).FirstOrDefault()?.CreatedAt;
            var latestText = latest.HasValue ? latest.Value.ToLocalTime().ToString("dd.MM HH:mm") : "—";
            TxtTelemetrySummary.Text = $"Показано: {telemetry.Count} | Типів: {telemetry.Select(t => t.EventType).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | Error-like: {errorLike} | Остання: {latestText}";
        }

        private void RefreshSessionSummaries()
        {
            if (_selected == null)
            {
                DgSessions.ItemsSource = new List<SessionSummaryRow>();
                TxtSessionsSummary.Text = "Виберіть клієнта";
                return;
            }

            var sessions = BuildSessionSummaries(_activeTelemetry);
            DgSessions.ItemsSource = sessions;

            var firmsTotal = sessions.Sum(session => session.FirmsAdded);
            var employeesTotal = sessions.Sum(session => session.EmployeesAdded);
            TxtSessionsSummary.Text = $"Сесій: {sessions.Count} | +Фірм: {firmsTotal} | +Працівників: {employeesTotal}";
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

        private void PopulateClientDetails(ClientRecord? client)
        {
            if (client == null)
            {
                TxtDetailHeader.Text = "Клієнт не вибраний";
                TxtDetailStatus.Text = "—";
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
                TxtNotes.Text = string.Empty;
                BtnSaveNotes.IsEnabled = false;
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
                ? "Відсутній"
                : _selectedProfile.MustResetPassword ? "Очікує примусового reset" : "Активний";
            TxtDetailRememberMe.Text = _selectedProfile == null
                ? "—"
                : _selectedProfile.RememberMeEnabled ? "Увімкнено" : "Вимкнено";
            TxtDetailProfileUpdatedAt.Text = _selectedProfile?.UpdatedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";
            TxtNotes.Text = client.Notes ?? string.Empty;
            BtnSaveNotes.IsEnabled = HasNotesChanged();
        }

        private string BuildLatestStateSummary()
        {
            var latestStats = GetLatestStatsTelemetry(_activeTelemetry);

            ExtractStats(latestStats, out var firms, out var employees);
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

        private void EnrichClientsWithRiskSignals()
        {
            var latestVersion = GetLatestKnownVersion(_allClients);
            var telemetryByClient = _allTelemetry
                .Where(t => !string.IsNullOrWhiteSpace(t.ClientId))
                .GroupBy(t => t.ClientId!, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (var client in _allClients)
            {
                telemetryByClient.TryGetValue(client.Id, out var clientTelemetry);
                clientTelemetry ??= new List<TelemetryRecord>();

                var latestHeartbeat = clientTelemetry
                    .Where(t => string.Equals(t.EventType, "heartbeat", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue)
                    .FirstOrDefault();

                var reasons = new List<string>();
                var score = 0;
                var daysToExpiry = GetDaysUntilExpiry(client);
                var daysSinceLastSeen = GetDaysSinceLastSeen(client.LastSeen);
                var errorLikeCount = clientTelemetry.Count(IsErrorLikeEvent);
                var isOutdatedVersion = IsOutdatedVersion(client.AppVersion, latestVersion);

                if (client.IsBlocked)
                {
                    score += 100;
                    reasons.Add("Клієнт заблокований");
                }

                if (daysToExpiry < 0)
                {
                    score += 80;
                    reasons.Add($"Ліцензія протермінована на {Math.Abs(daysToExpiry)} дн.");
                }
                else if (daysToExpiry <= 7)
                {
                    score += 35;
                    reasons.Add($"Ліцензія закінчується через {daysToExpiry} дн.");
                }
                else if (daysToExpiry <= 30)
                {
                    score += 15;
                    reasons.Add($"Ліцензія закінчується через {daysToExpiry} дн.");
                }

                if (!client.LastSeen.HasValue)
                {
                    score += 35;
                    reasons.Add("Немає активності");
                }
                else if (daysSinceLastSeen > 30)
                {
                    score += 50;
                    reasons.Add($"Неактивний {daysSinceLastSeen} дн.");
                }
                else if (daysSinceLastSeen > 7)
                {
                    score += 25;
                    reasons.Add($"Неактивний {daysSinceLastSeen} дн.");
                }

                if (isOutdatedVersion)
                {
                    score += 20;
                    reasons.Add($"Стара версія {client.AppVersion}");
                }

                if (errorLikeCount >= 5)
                {
                    score += 30;
                    reasons.Add($"Багато error-like подій ({errorLikeCount})");
                }
                else if (errorLikeCount > 0)
                {
                    score += 10;
                    reasons.Add($"Є error-like події ({errorLikeCount})");
                }

                if (latestHeartbeat?.CreatedAt == null)
                {
                    score += 10;
                    reasons.Add("Немає heartbeat");
                }

                client.RiskScore = score;
                client.RiskLevel = score >= 60 ? "High risk" : score >= 20 ? "Warning" : "OK";
                client.RiskReasons = reasons.Count == 0 ? new List<string> { "Сигналів ризику не виявлено" } : reasons;
                client.IsOutdatedVersion = isOutdatedVersion;
                client.ErrorLikeCount = errorLikeCount;
                client.LatestHeartbeatAt = latestHeartbeat?.CreatedAt;
            }

        }

        private static int GetDaysSinceLastSeen(DateTime? lastSeen)
        {
            if (!lastSeen.HasValue)
                return int.MaxValue;

            return (int)(DateTime.UtcNow - lastSeen.Value.ToUniversalTime()).TotalDays;
        }

        private static Version? GetLatestKnownVersion(IEnumerable<ClientRecord> clients)
        {
            Version? latest = null;
            foreach (var client in clients)
            {
                if (!TryParseComparableVersion(client.AppVersion, out var current))
                    continue;

                if (latest == null || current > latest)
                    latest = current;
            }

            return latest;
        }

        private static bool IsOutdatedVersion(string? appVersion, Version? latestVersion)
        {
            if (latestVersion == null || !TryParseComparableVersion(appVersion, out var current))
                return false;

            return current < latestVersion;
        }

        private static bool TryParseComparableVersion(string? value, out Version version)
        {
            version = new Version(0, 0);

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            var comparable = new string(trimmed.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
            if (string.IsNullOrWhiteSpace(comparable) || !Version.TryParse(comparable, out var parsedVersion))
                return false;

            version = parsedVersion;
            return true;
        }

        private void ClientFilter_Changed(object sender, EventArgs e)
        {
            if (!IsLoaded)
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
                var previousNotes = _selected.Notes ?? string.Empty;
                var nextNotes = TxtNotes.Text.Trim();
                await _svc.UpdateNotesAsync(_selected.Id, nextNotes);
                await _svc.TryWriteAuditAsync(_selected.Id, "notes_updated",
                    new { notes = previousNotes }, new { notes = nextNotes }, "Оператор оновив нотатки");
                await RefreshAsync(_selected.Id);
                TxtStatus.Text = $"Нотатки збережено: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtNotes_TextChanged(object sender, TextChangedEventArgs e)
        {
            BtnSaveNotes.IsEnabled = HasNotesChanged();
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
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnResetProfile_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || _selectedProfile == null)
                return;

            if (MessageBox.Show(
                    $"Скинути пароль профілю для {_selected.MachineName}?\nКористувач при наступному запуску повинен буде задати новий пароль.",
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

                var updatedProfile = await _svc.ResetClientProfilePasswordAsync(_selected.Id);
                if (updatedProfile == null)
                {
                    MessageBox.Show("У цього клієнта ще немає створеного профілю.", "Профіль відсутній",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                await _svc.TryWriteAuditAsync(_selected.Id, "profile_password_reset",
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

                await RefreshAsync(_selected.Id);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private static string? PromptInput(string message, string title, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 190,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
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
                Padding = new Thickness(8, 6, 8, 6)
            };
            var button = new Button
            {
                Content = "OK",
                Width = 90,
                Padding = new Thickness(0, 6, 0, 6),
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(0x89, 0xB4, 0xFA)),
                FontWeight = FontWeights.SemiBold
            };
            button.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };

            panel.Children.Add(label);
            panel.Children.Add(textBox);
            panel.Children.Add(button);
            dialog.Content = panel;

            return dialog.ShowDialog() == true ? textBox.Text : null;
        }
    }
}

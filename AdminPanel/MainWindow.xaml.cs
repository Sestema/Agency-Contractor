using System;
using System.Collections.Generic;
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
        private readonly SupabaseService _svc;
        private ClientRecord? _selected;
        private List<ClientRecord> _allClients = new();
        private List<TelemetryRecord> _allTelemetry = new();
        private List<TelemetryRecord> _activeTelemetry = new();
        private List<AdminCommandRecord> _activeCommands = new();
        private List<AdminAuditRecord> _activeAuditEntries = new();
        private List<ClientDiagnosticRecord> _activeDiagnostics = new();
        private bool _isUpdatingTelemetryEventFilter;

        private const string BaseUrl = "https://tssgxhatnjvqthdiyuwo.supabase.co";
        private const string ServiceKey =
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRzc2d4aGF0bmp2cXRoZGl5dXdvIiwi" +
            "cm9sZSI6InNlcnZpY2Vfcm9sZSIsImlhdCI6MTc3MjY3NTE4MSwiZXhwIjoyMDg4MjUxMTgxfQ." +
            "3FvcQIgE8617lsBaLgbbjWSsLD9Uug_lDQ-D03QZofA";

        public MainWindow()
        {
            InitializeComponent();
            _svc = new SupabaseService(BaseUrl, ServiceKey);
            Loaded += async (_, _) => await RefreshAsync();
        }

        private async Task RefreshAsync(string? preferredClientId = null)
        {
            try
            {
                TxtStatus.Text = "Завантаження...";

                var selectedId = preferredClientId ?? _selected?.Id;
                _allClients = await _svc.GetClientsAsync();
                _allTelemetry = await _svc.GetTelemetryAsync(limit: 500);
                EnrichClientsWithRiskSignals();

                PopulateVersionFilter();
                ApplyClientFilters(restoreSelection: false);
                RestoreClientSelection(selectedId);

                if (_selected == null)
                {
                    _activeTelemetry = _allTelemetry;
                    PopulateTelemetryEventFilter();
                    ApplyTelemetryFilters();
                    UpdateStats(_allTelemetry, null);
                    PopulateClientDetails(null);
                    ClearRemoteControlData();
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
                _activeTelemetry = _allTelemetry;
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                UpdateStats(_allTelemetry, null);
                PopulateClientDetails(null);
                ClearRemoteControlData();
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
                    (c.MachineName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.MachineId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.IpAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.AppVersion?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (c.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var clientStatus = GetSelectedComboTag(CmbClientStatus);
            filtered = clientStatus switch
            {
                "active" => filtered.Where(c => !c.IsBlocked),
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

            var riskFilter = GetSelectedComboTag(CmbRiskFilter);
            filtered = riskFilter switch
            {
                "high" => filtered.Where(c => string.Equals(c.RiskLevel, "High risk", StringComparison.Ordinal)),
                "warning" => filtered.Where(c => string.Equals(c.RiskLevel, "Warning", StringComparison.Ordinal)),
                "outdated" => filtered.Where(c => c.IsOutdatedVersion),
                _ => filtered
            };

            var version = CmbVersionFilter.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(version) && !string.Equals(version, "Усі версії", StringComparison.Ordinal))
                filtered = filtered.Where(c => string.Equals(c.AppVersion, version, StringComparison.OrdinalIgnoreCase));

            var filteredList = filtered
                .OrderByDescending(c => c.RiskScore)
                .ThenByDescending(c => c.LastSeen ?? DateTime.MinValue)
                .ThenBy(c => c.MachineName, StringComparer.OrdinalIgnoreCase)
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
            var highRisk = filtered.Count(c => string.Equals(c.RiskLevel, "High risk", StringComparison.Ordinal));
            var warnings = filtered.Count(c => string.Equals(c.RiskLevel, "Warning", StringComparison.Ordinal));
            TxtCount.Text = $"Показано: {filtered.Count}/{_allClients.Count} | High risk: {highRisk} | Warning: {warnings} | Заблокованих: {filtered.Count(c => c.IsBlocked)} | <=7 днів: {expiringSoon}";
        }

        private void UpdateRiskDashboard()
        {
            TxtStatHighRisk.Text = $"High risk: {_allClients.Count(c => string.Equals(c.RiskLevel, "High risk", StringComparison.Ordinal))}";
            TxtStatWarnings.Text = $"Warning: {_allClients.Count(c => string.Equals(c.RiskLevel, "Warning", StringComparison.Ordinal))}";
            TxtStatOutdated.Text = $"Outdated: {_allClients.Count(c => c.IsOutdatedVersion)}";
            TxtStatExpiring.Text = $"<=7 днів: {_allClients.Count(c => IsExpiringWithin(c, 7))}";
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
                var hb = filtered
                    .Where(t => t.EventType == "heartbeat" && t.EventData?.ValueKind == JsonValueKind.Object)
                    .OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue)
                    .FirstOrDefault();
                ExtractStats(hb, out totalFirms, out totalEmployees);
            }
            else
            {
                var byClient = telemetry
                    .Where(t => t.ClientId != null && t.EventType == "heartbeat" && t.EventData?.ValueKind == JsonValueKind.Object)
                    .GroupBy(t => t.ClientId);

                foreach (var group in byClient)
                {
                    var latest = group.OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue).FirstOrDefault();
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
            BtnSaveNotes.IsEnabled = hasSelection && HasNotesChanged();

            BtnSendAdminMessage.IsEnabled = hasSelection;
            BtnRequestDiagnostics.IsEnabled = hasSelection;
            BtnRunUpdateCheck.IsEnabled = hasSelection;
            BtnRemoteRestart.IsEnabled = hasSelection;
            BtnOpenLicenseRemote.IsEnabled = hasSelection;
            BtnSetReadOnlyCommand.IsEnabled = hasSelection;
            BtnSavePolicy.IsEnabled = hasSelection;
        }

        private async Task LoadSelectedClientDataAsync()
        {
            try
            {
                if (_selected == null)
                {
                    _activeTelemetry = _allTelemetry;
                    PopulateTelemetryEventFilter();
                    ApplyTelemetryFilters();
                    UpdateStats(_allTelemetry, null);
                    PopulateClientDetails(null);
                    ClearRemoteControlData();
                    return;
                }

                var selectedId = _selected.Id;
                var telemetryTask = _svc.GetTelemetryAsync(selectedId, 500);
                var commandsTask = _svc.GetAdminCommandsAsync(selectedId, 100);
                var auditTask = _svc.GetAdminAuditLogAsync(selectedId, 100);
                var diagnosticsTask = _svc.GetClientDiagnosticsAsync(selectedId, 50);
                var policyTask = _svc.GetClientPolicyAsync(selectedId);

                await Task.WhenAll(telemetryTask, commandsTask, auditTask, diagnosticsTask, policyTask);

                if (_selected?.Id != selectedId)
                    return;

                _activeTelemetry = telemetryTask.Result;
                _activeCommands = commandsTask.Result;
                _activeAuditEntries = auditTask.Result;
                _activeDiagnostics = diagnosticsTask.Result;

                UpdateStats(_activeTelemetry, selectedId);
                PopulateClientDetails(_selected);
                PopulateTelemetryEventFilter();
                ApplyTelemetryFilters();
                ApplyPolicyToEditor(policyTask.Result);
                RefreshRemoteDataViews();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"Telemetry/control error: {ex.Message}";
            }
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
                    t.MachineId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.IpAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.AppVersion.Contains(query, StringComparison.OrdinalIgnoreCase) ||
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

        private void PopulateClientDetails(ClientRecord? client)
        {
            if (client == null)
            {
                TxtDetailHeader.Text = "Клієнт не вибраний";
                TxtDetailStatus.Text = "—";
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
                TxtDetailRisk.Text = "—";
                TxtDetailRiskReasons.Text = "—";
                TxtDetailErrors.Text = "—";
                TxtNotes.Text = string.Empty;
                BtnSaveNotes.IsEnabled = false;
                return;
            }

            TxtDetailHeader.Text = client.MachineName;
            TxtDetailStatus.Text = client.IsBlocked ? "⛔ Заблокований" : "✅ Активний";
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
            TxtDetailLicense.Text = GetLicenseStateLabel(client);
            TxtDetailHeartbeat.Text = BuildHeartbeatSummary();
            TxtDetailRisk.Text = $"{client.RiskLevel} ({client.RiskScore})";
            TxtDetailRiskReasons.Text = BuildRiskReasonsText(client);
            TxtDetailErrors.Text = client.ErrorLikeCount == 0 ? "Немає error-like подій" : $"{client.ErrorLikeCount} error-like подій";
            TxtNotes.Text = client.Notes ?? string.Empty;
            BtnSaveNotes.IsEnabled = HasNotesChanged();
        }

        private string BuildHeartbeatSummary()
        {
            var hb = _activeTelemetry
                .Where(t => string.Equals(t.EventType, "heartbeat", StringComparison.OrdinalIgnoreCase) && t.EventData?.ValueKind == JsonValueKind.Object)
                .OrderByDescending(t => t.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();

            ExtractStats(hb, out var firms, out var employees);
            return hb == null ? "—" : $"Фірм: {firms}, працівників: {employees}";
        }

        private void ApplyPolicyToEditor(ClientPolicyRecord? policy)
        {
            var effective = policy ?? new ClientPolicyRecord
            {
                ClientId = _selected?.Id ?? string.Empty,
                UpdateChannel = "stable"
            };

            TxtPolicyMinVersion.Text = effective.MinimumSupportedVersion ?? string.Empty;
            TxtPolicyRecommendedVersion.Text = effective.RecommendedVersion ?? string.Empty;
            TxtPolicyVersion.Text = string.IsNullOrWhiteSpace(effective.PolicyVersion)
                ? "policy: not set"
                : $"policy: {effective.PolicyVersion}";
            TxtPolicyAdminMessage.Text = effective.AdminMessage ?? string.Empty;
            ChkPolicyForceUpdate.IsChecked = effective.ForceUpdate;
            ChkPolicyMaintenance.IsChecked = effective.MaintenanceMode;
            ChkPolicyReadOnly.IsChecked = effective.ReadOnlyMode;
            ChkPolicyDisableAI.IsChecked = effective.DisableAI;
            ChkPolicyDisableExports.IsChecked = effective.DisableExports;
            ChkPolicyHideTemplates.IsChecked = effective.HideTemplates;
            ChkPolicyHideFinance.IsChecked = effective.HideFinance;
            ChkPolicyRequireOnline.IsChecked = effective.RequireOnlineCheck;
            SelectComboTag(CmbPolicyChannel, string.IsNullOrWhiteSpace(effective.UpdateChannel) ? "stable" : effective.UpdateChannel);
        }

        private ClientPolicyRecord BuildPolicyFromEditor()
        {
            return new ClientPolicyRecord
            {
                ClientId = _selected?.Id ?? string.Empty,
                MinimumSupportedVersion = (TxtPolicyMinVersion.Text ?? string.Empty).Trim(),
                RecommendedVersion = (TxtPolicyRecommendedVersion.Text ?? string.Empty).Trim(),
                UpdateChannel = GetSelectedComboTag(CmbPolicyChannel),
                ForceUpdate = ChkPolicyForceUpdate.IsChecked == true,
                MaintenanceMode = ChkPolicyMaintenance.IsChecked == true,
                ReadOnlyMode = ChkPolicyReadOnly.IsChecked == true,
                DisableAI = ChkPolicyDisableAI.IsChecked == true,
                DisableExports = ChkPolicyDisableExports.IsChecked == true,
                HideTemplates = ChkPolicyHideTemplates.IsChecked == true,
                HideFinance = ChkPolicyHideFinance.IsChecked == true,
                RequireOnlineCheck = ChkPolicyRequireOnline.IsChecked == true,
                AdminMessage = (TxtPolicyAdminMessage.Text ?? string.Empty).Trim(),
                PolicyVersion = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                UpdatedAt = DateTime.UtcNow
            };
        }

        private void RefreshRemoteDataViews()
        {
            DgCommands.ItemsSource = _activeCommands;
            DgAudit.ItemsSource = _activeAuditEntries;
            DgDiagnostics.ItemsSource = _activeDiagnostics;

            TxtCommandsSummary.Text = $"Команди: {_activeCommands.Count} | Pending: {_activeCommands.Count(c => string.Equals(c.Status, "pending", StringComparison.OrdinalIgnoreCase))}";
            TxtAuditSummary.Text = $"Аудит: {_activeAuditEntries.Count}";
            TxtDiagnosticsSummary.Text = $"Діагностика: {_activeDiagnostics.Count}";
        }

        private void ClearRemoteControlData()
        {
            _activeCommands = new();
            _activeAuditEntries = new();
            _activeDiagnostics = new();
            DgCommands.ItemsSource = _activeCommands;
            DgAudit.ItemsSource = _activeAuditEntries;
            DgDiagnostics.ItemsSource = _activeDiagnostics;
            TxtCommandsSummary.Text = "Команди: 0";
            TxtAuditSummary.Text = "Аудит: 0";
            TxtDiagnosticsSummary.Text = "Діагностика: 0";
            ApplyPolicyToEditor(null);
            TxtCommandMessage.Text = string.Empty;
        }

        private static string FormatDate(DateTime? value, string format)
        {
            return value.HasValue ? value.Value.ToString(format) : "—";
        }

        private string GetLicenseStateLabel(ClientRecord client)
        {
            var days = GetDaysUntilExpiry(client);
            if (client.IsBlocked)
                return "Заблокований";
            if (!client.ExpiresAt.HasValue)
                return "Без строку";
            if (days < 0)
                return $"Протерміновано на {Math.Abs(days)} дн.";
            if (days <= 30)
                return $"Закінчується через {days} дн.";
            return $"Активна, ще {days} дн.";
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

            UpdateRiskDashboard();
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

        private static string BuildRiskReasonsText(ClientRecord client)
        {
            return client.RiskReasons.Count == 0
                ? "—"
                : string.Join(Environment.NewLine, client.RiskReasons.Select(reason => $"- {reason}"));
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
                case "risk":
                    ToggleComboTag(CmbRiskFilter, parts[1], "all");
                    break;
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
            SelectComboTag(CmbRiskFilter, "all");
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

            var input = PromptInput("На скільки днів продовжити (від сьогодні):", "Продовжити ліцензію", "365");
            if (input == null || !int.TryParse(input, out var days) || days <= 0)
                return;

            try
            {
                var newExpiry = DateTime.UtcNow.AddDays(days);
                await _svc.ExtendLicenseAsync(_selected.Id, newExpiry);
                await _svc.TryWriteAuditAsync(_selected.Id, "license_extended",
                    new { expires_at = _selected.ExpiresAt }, new { expires_at = newExpiry }, $"Продовжено на {days} днів");
                MessageBox.Show($"Ліцензію продовжено до {newExpiry:dd.MM.yyyy}", "Готово",
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

        private async void BtnSendAdminMessage_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            var message = (TxtCommandMessage.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                MessageBox.Show("Введіть текст повідомлення.", "Команда", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var payload = new { message, severity = "info", modal = false };
                await _svc.CreateAdminCommandAsync(_selected.Id, "show_message", payload);
                await _svc.TryWriteAuditAsync(_selected.Id, "command_sent", null, new { command = "show_message", payload }, "Надіслано повідомлення");
                TxtCommandMessage.Text = string.Empty;
                await LoadSelectedClientDataAsync();
                TxtStatus.Text = "Команду show_message додано";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Команда", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRequestDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            await QueueSimpleCommandAsync("upload_diagnostics", "Запит діагностики");
        }

        private async void BtnRunUpdateCheck_Click(object sender, RoutedEventArgs e)
        {
            await QueueSimpleCommandAsync("run_update_check", "Remote update check");
        }

        private async void BtnRemoteRestart_Click(object sender, RoutedEventArgs e)
        {
            await QueueSimpleCommandAsync("restart_app", "Remote restart");
        }

        private async void BtnOpenLicenseRemote_Click(object sender, RoutedEventArgs e)
        {
            await QueueSimpleCommandAsync("open_license_window", "Open license window");
        }

        private async void BtnSetReadOnlyCommand_Click(object sender, RoutedEventArgs e)
        {
            var message = (TxtCommandMessage.Text ?? string.Empty).Trim();
            var payload = string.IsNullOrWhiteSpace(message)
                ? new { admin_message = "Клієнт переведено в read-only режим адміністратором." }
                : new { admin_message = message };
            await QueueCommandAsync("enter_readonly_mode", payload, "Read-only command");
        }

        private async void BtnSavePolicy_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
                return;

            try
            {
                var existing = await _svc.GetClientPolicyAsync(_selected.Id);
                var policy = BuildPolicyFromEditor();
                await _svc.UpsertClientPolicyAsync(policy);
                await _svc.TryWriteAuditAsync(_selected.Id, "policy_updated", existing, policy, "Оновлено remote policy");
                await LoadSelectedClientDataAsync();
                TxtStatus.Text = "Policy збережено";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Policy", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task QueueSimpleCommandAsync(string commandType, string note)
        {
            await QueueCommandAsync(commandType, new { }, note);
        }

        private async Task QueueCommandAsync(string commandType, object payload, string note)
        {
            if (_selected == null)
                return;

            try
            {
                await _svc.CreateAdminCommandAsync(_selected.Id, commandType, payload);
                await _svc.TryWriteAuditAsync(_selected.Id, "command_sent", null, new { command = commandType, payload }, note);
                await LoadSelectedClientDataAsync();
                TxtStatus.Text = $"Команду {commandType} додано";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Команда", MessageBoxButton.OK, MessageBoxImage.Error);
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
                "risk:high" => "High risk",
                "risk:warning" => "Warning",
                "risk:outdated" => "Outdated version",
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

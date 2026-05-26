using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class HeartbeatResult
    {
        public bool IsAllowed { get; set; } = true;
        public string? ClientId { get; set; }
        public RemotePolicy? Policy { get; set; }
        public List<RemoteCommand> PendingCommands { get; set; } = new();
        public ClientAccessState AccessState { get; set; } = new();
    }

    public sealed class ClientAccessState
    {
        private static readonly TimeSpan OfflineGraceWindow = TimeSpan.FromDays(7);

        public string? ClientId { get; set; }
        public bool ClientExists { get; set; }
        public bool IsBlocked { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string Plan { get; set; } = string.Empty;
        public string ManagedGeminiApiKey { get; set; } = string.Empty;
        public RemotePolicy? Policy { get; set; }
        public List<RemoteCommand> PendingCommands { get; set; } = new();
        public bool IsLive { get; set; }
        public bool IsFromCache { get; set; }
        public bool IsStale { get; set; }
        public DateTime? LastServerCheckUtc { get; set; }
        public string Source { get; set; } = string.Empty;

        public bool HasRemoteAccessWindow =>
            !IsBlocked && ExpiresAtUtc.HasValue && ExpiresAtUtc.Value > DateTime.UtcNow;

        public bool IsExpired =>
            !IsBlocked && ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= DateTime.UtcNow;

        public bool HasKnownState =>
            IsBlocked || ExpiresAtUtc.HasValue || !string.IsNullOrWhiteSpace(ClientId);

        public bool IsOfflineGraceActive =>
            IsFromCache
            && LastServerCheckUtc.HasValue
            && DateTime.UtcNow - LastServerCheckUtc.Value <= OfflineGraceWindow;

        public int DaysRemaining =>
            !ExpiresAtUtc.HasValue
                ? 0
                : Math.Max(0, (int)Math.Ceiling((ExpiresAtUtc.Value - DateTime.UtcNow).TotalDays));

        public int OfflineGraceDaysRemaining =>
            !LastServerCheckUtc.HasValue
                ? 0
                : Math.Max(0, (int)Math.Ceiling((LastServerCheckUtc.Value.Add(OfflineGraceWindow) - DateTime.UtcNow).TotalDays));
    }

    public static class TelemetryService
    {
        private static AppSettingsService? _appSettingsService;
        private static CompanyService? _companyService;
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
        private static readonly string _baseUrl = Encoding.UTF8.GetString(Convert.FromBase64String(
            "aHR0cHM6Ly90c3NneGhhdG5qdnF0aGRpeXV3by5zdXBhYmFzZS5jbw=="));

        private static bool _isBlocked;
        private static string? _clientId;
        private static RemotePolicy? _cachedPolicy;

        public static bool IsBlocked => _isBlocked;
        public static string? GetCurrentClientId() => _clientId;
        internal static RemotePolicy? GetCachedPolicy() => _cachedPolicy;
        internal static HttpClient HttpClient => _http;
        internal static string BaseUrl => _baseUrl;

        public static void Initialize(AppSettingsService appSettingsService, CompanyService companyService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
        }

        private static string GetKey()
        {
            var p = new[]
            {
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9",
                "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRzc2d4aGF0bmp2cXRoZGl5dXdvIiwi" +
                "cm9sZSI6ImFub24iLCJpYXQiOjE3NzI2NzUxODEsImV4cCI6MjA4ODI1MTE4MX0",
                "90eAJDS-zPA1Jlni_Lp2DdIrDxj_lMLn6AlKzJkW1kc"
            };
            return string.Join(".", p);
        }

        internal static void ApplyHeaders(HttpRequestMessage request)
        {
            var key = GetKey();
            request.Headers.Remove("apikey");
            request.Headers.Remove("Authorization");
            request.Headers.TryAddWithoutValidation("apikey", key);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        }

        private static Dictionary<string, object> CollectStats(bool includeEmployeeCounts = true)
        {
            var stats = new Dictionary<string, object>();
            try
            {
                var companyService = _companyService;
                if (companyService != null)
                {
                    var companies = companyService.Companies.ToList();
                    stats["firms_count"] = companies.Count;
                    if (includeEmployeeCounts)
                    {
                        int totalEmployees = 0;
                        foreach (var c in companies)
                        {
                            try { totalEmployees += companyService.GetActiveEmployeeCount(c); }
                            catch (Exception ex) { LoggingService.LogWarning("Telemetry.CountEmployees", ex.Message); }
                        }

                        stats["employees_count"] = totalEmployees;
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogWarning("Telemetry.CollectStats", ex.Message); }
            return stats;
        }

        /// <summary>
        /// Silent heartbeat + remote admin envelope.
        /// </summary>
        public static async Task<HeartbeatResult> SendHeartbeatAsync()
        {
            var result = new HeartbeatResult();
            try
            {
                var stats = CollectStats();
                var gateway = await CallGatewayAsync("heartbeat", new Dictionary<string, object?>
                {
                    ["event_type"] = "heartbeat",
                    ["event_data"] = stats,
                    ["ip_address"] = await GetIpSilentAsync().ConfigureAwait(false)
                }).ConfigureAwait(false);

                if (gateway == null || !gateway.Ok)
                    return result;

                var accessState = BuildAccessState(gateway, isFromCache: false);
                UpdateCachedGatewayState(gateway, accessState);
                result.ClientId = accessState.ClientId;
                result.IsAllowed = !accessState.IsBlocked;
                result.Policy = gateway.Policy;
                result.PendingCommands = gateway.PendingCommands ?? new List<RemoteCommand>();
                result.AccessState = accessState;

                return result;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService", $"Heartbeat failed: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Lightweight startup sync for AdminPanel visibility.
        /// Updates the client row immediately when the app launches.
        /// </summary>
        public static async Task ReportStartupSnapshotAsync()
        {
            try
            {
                var stats = CollectStats(includeEmployeeCounts: false);
                var gateway = await CallGatewayAsync("startup", new Dictionary<string, object?>
                {
                    ["event_type"] = "app_started",
                    ["event_data"] = stats,
                    ["ip_address"] = ""
                }).ConfigureAwait(false);
                if (gateway != null && gateway.Ok)
                    UpdateCachedGatewayState(gateway, BuildAccessState(gateway, isFromCache: false));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.StartupSnapshot", ex.Message);
            }
        }

        public static async Task<string?> EnsureStartupClientIdAsync()
        {
            if (!string.IsNullOrWhiteSpace(_clientId))
                return _clientId;

            await ReportStartupSnapshotAsync().ConfigureAwait(false);
            return _clientId;
        }

        internal static ClientAccessState GetCachedAccessStateSnapshot()
        {
            return LoadCachedAccessState();
        }

        public static async Task<ClientAccessState> GetStartupAccessStateAsync()
        {
            var accessState = LoadCachedAccessState();

            try
            {
                var stats = CollectStats(includeEmployeeCounts: false);
                var gateway = await CallGatewayAsync("startup", new Dictionary<string, object?>
                {
                    ["event_type"] = "app_started",
                    ["event_data"] = stats,
                    ["ip_address"] = await GetIpSilentAsync().ConfigureAwait(false)
                }).ConfigureAwait(false);
                if (gateway == null || !gateway.Ok)
                {
                    ApplyRemoteMultiUserFlag(accessState.Plan);
                    return accessState;
                }

                accessState = BuildAccessState(gateway, isFromCache: false);
                UpdateCachedGatewayState(gateway, accessState);
                return accessState;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.GetStartupAccessState", ex.Message);
                ApplyRemoteMultiUserFlag(accessState.Plan);
                if (!accessState.HasKnownState)
                    return accessState;

                accessState.IsStale = true;
                accessState.Source = accessState.IsOfflineGraceActive ? "cache_offline_grace" : "cache_stale";
                return accessState;
            }
        }

        public static async Task<ClientAccessState?> MigrateLegacyLicenseAsync(
            string plan,
            string expiresOn,
            string activatedOn,
            bool isUnlimited)
        {
            try
            {
                var gateway = await CallGatewayAsync("migrate_legacy_license", new Dictionary<string, object?>
                {
                    ["plan"] = plan,
                    ["expires_on"] = expiresOn,
                    ["activated_on"] = activatedOn,
                    ["is_unlimited"] = isUnlimited,
                    ["source"] = "local_file"
                }).ConfigureAwait(false);

                if (gateway == null)
                {
                    LoggingService.LogWarning("TelemetryService.MigrateLegacyLicense", "gateway_failed");
                    return null;
                }

                if (!gateway.Ok)
                {
                    var reason = string.IsNullOrWhiteSpace(gateway.Error) ? "gateway_failed" : gateway.Error;
                    LoggingService.LogWarning("TelemetryService.MigrateLegacyLicense", reason);
                    return null;
                }

                if (string.Equals(gateway.MigrationResult, "noop", StringComparison.OrdinalIgnoreCase))
                    LoggingService.LogWarning("TelemetryService.MigrateLegacyLicense", "noop_server_newer");

                var accessState = BuildAccessState(gateway, isFromCache: false);
                UpdateCachedGatewayState(gateway, accessState);
                return accessState;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.MigrateLegacyLicense", $"gateway_failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Track a custom event (firm_created, employee_added, etc.)
        /// </summary>
        public static void TrackEvent(string eventType, Dictionary<string, object>? data = null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var eventData = data ?? new Dictionary<string, object>();
                    var allStats = CollectStats();
                    foreach (var kv in allStats)
                        eventData.TryAdd(kv.Key, kv.Value);
                    await CallGatewayAsync("track_event", new Dictionary<string, object?>
                    {
                        ["event_type"] = eventType,
                        ["event_data"] = eventData,
                        ["ip_address"] = await GetIpSilentAsync().ConfigureAwait(false)
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("TelemetryService.TrackEvent", ex.Message);
                }
            });
        }

        private static async Task<string> GetIpSilentAsync()
        {
            try
            {
                return (await _http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
            }
            catch (Exception ex) { LoggingService.LogWarning("Telemetry.GetIp", ex.Message); return ""; }
        }

        internal static async Task<bool> AcknowledgeCommandAsync(string commandId, string status, Dictionary<string, object?>? result, string? errorText)
        {
            if (string.IsNullOrWhiteSpace(commandId))
                return false;

            var gateway = await CallGatewayAsync("ack_command", new Dictionary<string, object?>
            {
                ["command_id"] = commandId,
                ["command_status"] = status,
                ["command_result"] = result,
                ["command_error"] = errorText
            }).ConfigureAwait(false);

            return gateway?.Ok == true;
        }

        private static async Task<GatewayResponse?> CallGatewayAsync(string action, Dictionary<string, object?>? extras = null)
        {
            var body = new Dictionary<string, object?>
            {
                ["action"] = action,
                ["machine_id"] = LicenseService.GetMachineId(),
                ["machine_name"] = Environment.MachineName,
                ["app_version"] = AppSettingsService.CurrentAppVersion
            };
            if (extras != null)
            {
                foreach (var kv in extras)
                    body[kv.Key] = kv.Value;
            }

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/functions/v1/client-gateway")
            {
                Content = new StringContent(JsonSerializer.Serialize(body, _jsonOptions), Encoding.UTF8, "application/json")
            };
            ApplyHeaders(req);

            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                LoggingService.LogWarning("TelemetryService.Gateway", $"{action}: {(int)resp.StatusCode} {json}");
                try
                {
                    var errorResponse = DeserializeGatewayResponse(json);
                    if (errorResponse != null)
                    {
                        errorResponse.Ok = false;
                        if (string.IsNullOrWhiteSpace(errorResponse.Error))
                            errorResponse.Error = resp.StatusCode.ToString();
                        return errorResponse;
                    }
                }
                catch
                {
                }

                return new GatewayResponse
                {
                    Ok = false,
                    Error = string.IsNullOrWhiteSpace(json) ? resp.StatusCode.ToString() : json
                };
            }

            return DeserializeGatewayResponse(json);
        }

        private static GatewayResponse? DeserializeGatewayResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var gateway = JsonSerializer.Deserialize<GatewayResponse>(json, _jsonOptions);
                if (gateway == null)
                    return null;

                gateway.RawPayload = document.RootElement.EnumerateObject()
                    .ToDictionary(item => item.Name, item => item.Value.Clone());
                return gateway;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.DeserializeGatewayResponse", ex.Message);
                return JsonSerializer.Deserialize<GatewayResponse>(json, _jsonOptions);
            }
        }

        private static ClientAccessState BuildAccessState(GatewayResponse gateway, bool isFromCache)
        {
            return new ClientAccessState
            {
                ClientId = gateway.ClientId,
                ClientExists = !string.IsNullOrWhiteSpace(gateway.ClientId),
                IsBlocked = gateway.IsBlocked,
                ExpiresAtUtc = gateway.ExpiresAt,
                Plan = gateway.Plan ?? string.Empty,
                ManagedGeminiApiKey = gateway.GeminiApiKey ?? string.Empty,
                Policy = gateway.Policy,
                PendingCommands = gateway.PendingCommands ?? new List<RemoteCommand>(),
                IsLive = !isFromCache,
                IsFromCache = isFromCache,
                IsStale = false,
                LastServerCheckUtc = DateTime.UtcNow,
                Source = isFromCache ? "cache" : "server"
            };
        }

        private static ClientAccessState LoadCachedAccessState()
        {
            var settings = _appSettingsService?.Settings;
            if (settings == null)
                return new ClientAccessState();

            if (string.IsNullOrWhiteSpace(settings.CachedAccessLastCheckedAtUtc))
                return new ClientAccessState();

            if (!DateTime.TryParse(settings.CachedAccessLastCheckedAtUtc, out var lastCheckedUtc))
                return new ClientAccessState();

            DateTime? expiresAtUtc = null;
            if (!string.IsNullOrWhiteSpace(settings.CachedAccessExpiresAtUtc)
                && DateTime.TryParse(settings.CachedAccessExpiresAtUtc, out var parsedExpires))
            {
                expiresAtUtc = parsedExpires.Kind == DateTimeKind.Utc
                    ? parsedExpires
                    : parsedExpires.ToUniversalTime();
            }

            return new ClientAccessState
            {
                ClientId = settings.CachedAccessClientId,
                ClientExists = !string.IsNullOrWhiteSpace(settings.CachedAccessClientId),
                IsBlocked = settings.CachedAccessIsBlocked,
                ExpiresAtUtc = expiresAtUtc,
                Plan = settings.CachedAccessPlan ?? string.Empty,
                ManagedGeminiApiKey = string.Empty,
                Policy = _cachedPolicy,
                IsLive = false,
                IsFromCache = true,
                IsStale = false,
                LastServerCheckUtc = lastCheckedUtc.Kind == DateTimeKind.Utc
                    ? lastCheckedUtc
                    : lastCheckedUtc.ToUniversalTime(),
                Source = string.IsNullOrWhiteSpace(settings.CachedAccessSource) ? "cache" : settings.CachedAccessSource
            };
        }

        private static void UpdateCachedGatewayState(GatewayResponse gateway, ClientAccessState accessState)
        {
            _clientId = string.IsNullOrWhiteSpace(gateway.ClientId) ? _clientId : gateway.ClientId;
            _isBlocked = gateway.IsBlocked;
            _cachedPolicy = gateway.Policy;

            var settings = _appSettingsService?.Settings;
            if (settings == null)
                return;

            settings.CachedAccessClientId = accessState.ClientId ?? string.Empty;
            settings.CachedAccessIsBlocked = accessState.IsBlocked;
            settings.CachedAccessExpiresAtUtc = accessState.ExpiresAtUtc?.ToString("o") ?? string.Empty;
            settings.CachedAccessLastCheckedAtUtc = accessState.LastServerCheckUtc?.ToString("o") ?? DateTime.UtcNow.ToString("o");
            settings.CachedAccessSource = "server";
            settings.CachedAccessPlan = accessState.Plan;
            ApplyRemoteMultiUserFlag(accessState.Plan);
            _appSettingsService?.SaveSettings();
        }

        private static void ApplyRemoteMultiUserFlag(string? plan)
        {
            var settings = _appSettingsService?.Settings;
            if (settings == null)
                return;

            var normalized = (plan ?? string.Empty).Trim().ToLowerInvariant();
            var shouldEnable = normalized == "business";
            if (settings.ExperimentalMultiUser == shouldEnable)
                return;

            settings.ExperimentalMultiUser = shouldEnable;
            LoggingService.LogInfo(
                "TelemetryService.ApplyRemoteMultiUserFlag",
                shouldEnable
                    ? "Enabled ExperimentalMultiUser from remote Business plan."
                    : "Disabled ExperimentalMultiUser because remote plan is not Business.");
        }

        public static async Task<WorkspaceDeviceSessionGatewayResponse?> ReportWorkspaceDeviceSessionAsync(
            string ownerClientId,
            string workspaceId,
            string actorKind,
            string localUserId,
            string actorDisplayName)
        {
            try
            {
                var gateway = await CallGatewayAsync("workspace_session_heartbeat", new Dictionary<string, object?>
                {
                    ["owner_client_id"] = ownerClientId,
                    ["workspace_id"] = workspaceId,
                    ["actor_kind"] = actorKind,
                    ["local_user_id"] = localUserId,
                    ["actor_display_name"] = actorDisplayName,
                    ["windows_user"] = Environment.UserName,
                    ["ip_address"] = await GetIpSilentAsync().ConfigureAwait(false)
                }).ConfigureAwait(false);

                return ParseWorkspaceSessionGatewayResponse(gateway);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.WorkspaceSessionHeartbeat", ex.Message);
                return null;
            }
        }

        public static async Task<WorkspaceDeviceStatusResult?> GetWorkspaceDeviceStatusAsync(
            string ownerClientId,
            string? workspaceId = null)
        {
            try
            {
                var gateway = await CallGatewayAsync("get_workspace_device_status", new Dictionary<string, object?>
                {
                    ["owner_client_id"] = ownerClientId,
                    ["workspace_id"] = workspaceId
                }).ConfigureAwait(false);

                if (gateway?.RawPayload == null)
                    return null;

                return ParseWorkspaceDeviceStatus(gateway.RawPayload);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.GetWorkspaceDeviceStatus", ex.Message);
                return null;
            }
        }

        public static async Task EndWorkspaceDeviceSessionAsync(string ownerClientId, string workspaceId)
        {
            try
            {
                await CallGatewayAsync("end_workspace_device_session", new Dictionary<string, object?>
                {
                    ["owner_client_id"] = ownerClientId,
                    ["workspace_id"] = workspaceId,
                    ["windows_user"] = Environment.UserName
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService.EndWorkspaceDeviceSession", ex.Message);
            }
        }

        private static WorkspaceDeviceSessionGatewayResponse? ParseWorkspaceSessionGatewayResponse(GatewayResponse? gateway)
        {
            if (gateway == null)
                return null;

            var payload = gateway.RawPayload;
            if (payload != null && payload.ContainsKey("tenant_id"))
            {
                return new WorkspaceDeviceSessionGatewayResponse
                {
                    Ok = ReadBool(payload, "ok"),
                    Error = ReadString(payload, "error"),
                    TenantId = ReadString(payload, "tenant_id"),
                    MaxDevices = ReadInt(payload, "max_devices"),
                    ActiveDevices = ReadInt(payload, "active_devices")
                };
            }

            return new WorkspaceDeviceSessionGatewayResponse
            {
                Ok = gateway.Ok,
                Error = gateway.Error ?? string.Empty
            };
        }

        private static WorkspaceDeviceStatusResult? ParseWorkspaceDeviceStatus(Dictionary<string, JsonElement>? payload)
        {
            if (payload == null || !ReadBool(payload, "ok"))
            {
                return new WorkspaceDeviceStatusResult
                {
                    Ok = false,
                    Error = payload == null ? "empty_payload" : ReadString(payload, "error")
                };
            }

            var summary = ReadObject(payload, "summary");
            var users = new List<WorkspaceDeviceUserStatus>();
            if (payload.TryGetValue("users", out var usersElement) && usersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var userElement in usersElement.EnumerateArray())
                {
                    users.Add(new WorkspaceDeviceUserStatus
                    {
                        LocalUserId = ReadString(userElement, "local_user_id"),
                        ActorKind = ReadString(userElement, "actor_kind"),
                        DisplayName = ReadString(userElement, "display_name"),
                        LastSeenAtUtc = ReadDateTime(userElement, "last_seen_at"),
                        DevicesOnline = ReadInt(userElement, "devices_online"),
                        DevicesTotal = ReadInt(userElement, "devices_total")
                    });
                }
            }

            var sessions = new List<WorkspaceDeviceSessionStatus>();
            if (payload.TryGetValue("sessions", out var sessionsElement) && sessionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sessionElement in sessionsElement.EnumerateArray())
                {
                    sessions.Add(new WorkspaceDeviceSessionStatus
                    {
                        WorkspaceId = ReadString(sessionElement, "workspace_id"),
                        MachineId = ReadString(sessionElement, "machine_id"),
                        MachineName = ReadString(sessionElement, "machine_name"),
                        WindowsUser = ReadString(sessionElement, "windows_user"),
                        ActorKind = ReadString(sessionElement, "actor_kind"),
                        LocalUserId = ReadString(sessionElement, "local_user_id"),
                        ActorDisplayName = ReadString(sessionElement, "actor_display_name"),
                        AppVersion = ReadString(sessionElement, "app_version"),
                        IpAddress = ReadString(sessionElement, "ip_address"),
                        LastSeenAtUtc = ReadDateTime(sessionElement, "last_seen_at"),
                        IsOnline = ReadBool(sessionElement, "is_online")
                    });
                }
            }

            return new WorkspaceDeviceStatusResult
            {
                Ok = true,
                MaxDevices = ReadInt(summary, "max_devices"),
                DevicesOnline = ReadInt(summary, "devices_online"),
                Users = users,
                Sessions = sessions
            };
        }

        private static Dictionary<string, JsonElement>? ReadObject(Dictionary<string, JsonElement>? payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Object)
                return null;

            return element.EnumerateObject().ToDictionary(item => item.Name, item => item.Value);
        }

        private static string ReadString(Dictionary<string, JsonElement>? payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var element))
                return string.Empty;

            return ReadJsonScalarString(element);
        }

        private static string ReadString(JsonElement element, string key)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property))
                return ReadJsonScalarString(property);

            return string.Empty;
        }

        private static string ReadJsonScalarString(JsonElement element) =>
            element.ValueKind switch
            {
                JsonValueKind.String => element.GetString() ?? string.Empty,
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };

        private static int ReadInt(Dictionary<string, JsonElement>? payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var element))
                return 0;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var directValue))
                return directValue;

            return ReadInt(element, key);
        }

        private static int ReadInt(JsonElement element, string key)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property))
            {
                if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
                    return value;
            }

            return 0;
        }

        private static bool ReadBool(Dictionary<string, JsonElement>? payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var element))
                return false;

            if (element.ValueKind == JsonValueKind.True)
                return true;

            if (element.ValueKind == JsonValueKind.False)
                return false;

            return ReadBool(element, key);
        }

        private static bool ReadBool(JsonElement element, string key)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var property))
                return property.ValueKind == JsonValueKind.True;

            return false;
        }

        private static DateTime? ReadDateTime(JsonElement element, string key)
        {
            var text = ReadString(element, key);
            if (string.IsNullOrWhiteSpace(text))
                return null;

            return DateTime.TryParse(text, out var parsed) ? parsed.ToUniversalTime() : null;
        }
    }
}

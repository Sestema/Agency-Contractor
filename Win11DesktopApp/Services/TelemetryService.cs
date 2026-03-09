using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class HeartbeatResult
    {
        public bool IsAllowed { get; set; } = true;
        public string? ClientId { get; set; }
        public RemotePolicy? Policy { get; set; }
        public List<RemoteCommand> PendingCommands { get; set; } = new();
    }

    public static class TelemetryService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly string _baseUrl = Encoding.UTF8.GetString(Convert.FromBase64String(
            "aHR0cHM6Ly90c3NneGhhdG5qdnF0aGRpeXV3by5zdXBhYmFzZS5jbw=="));

        private static bool _isBlocked;
        private static string? _clientId;

        public static bool IsBlocked => _isBlocked;
        internal static HttpClient HttpClient => _http;
        internal static string BaseUrl => _baseUrl;

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

        internal static void ConfigureHeaders()
        {
            var key = GetKey();
            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", key);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {key}");
        }

        private static Dictionary<string, object> CollectStats()
        {
            var stats = new Dictionary<string, object>();
            try
            {
                var companies = App.CompanyService?.Companies;
                if (companies != null)
                {
                    stats["firms_count"] = companies.Count;
                    int totalEmployees = 0;
                    foreach (var c in companies)
                    {
                        try { totalEmployees += App.CompanyService!.GetActiveEmployeeCount(c); }
                        catch (Exception ex) { LoggingService.LogWarning("Telemetry.CountEmployees", ex.Message); }
                    }
                    stats["employees_count"] = totalEmployees;
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
                var machineId = LicenseService.GetMachineId();
                var machineName = Environment.MachineName;
                var appVersion = AppSettingsService.CurrentAppVersion;
                var expiresAt = LicenseService.GetExpiresAt();
                var stats = CollectStats();

                string ip = await GetIpSilentAsync().ConfigureAwait(false);

                ConfigureHeaders();

                var existing = await FindClientAsync(machineId).ConfigureAwait(false);

                if (existing != null)
                {
                    _clientId = existing.Value.GetProperty("id").GetString();
                    _isBlocked = existing.Value.TryGetProperty("is_blocked", out var b) && b.GetBoolean();

                    await UpdateClientAsync(_clientId!, ip, appVersion, machineName, expiresAt).ConfigureAwait(false);
                    await InsertTelemetryAsync(_clientId!, machineId, ip, appVersion, "heartbeat", stats).ConfigureAwait(false);
                }
                else
                {
                    _clientId = await RegisterClientAsync(machineId, machineName, ip, appVersion, expiresAt).ConfigureAwait(false);
                    if (_clientId != null)
                        await InsertTelemetryAsync(_clientId, machineId, ip, appVersion, "first_launch", stats).ConfigureAwait(false);
                }

                result.ClientId = _clientId;
                result.IsAllowed = !_isBlocked;

                if (!string.IsNullOrWhiteSpace(_clientId))
                {
                    result.Policy = await PolicyService.FetchPolicyAsync(_clientId).ConfigureAwait(false);
                    result.PendingCommands = await CommandService.GetPendingCommandsAsync(_clientId).ConfigureAwait(false);
                }

                return result;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TelemetryService", $"Heartbeat failed: {ex.Message}");
                return result;
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
                    if (string.IsNullOrEmpty(_clientId)) return;
                    var machineId = LicenseService.GetMachineId();
                    var appVersion = AppSettingsService.CurrentAppVersion;
                    ConfigureHeaders();
                    var eventData = data ?? new Dictionary<string, object>();
                    var allStats = CollectStats();
                    foreach (var kv in allStats)
                        eventData.TryAdd(kv.Key, kv.Value);
                    await InsertTelemetryAsync(_clientId, machineId, "", appVersion, eventType, eventData).ConfigureAwait(false);
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

        private static async Task<JsonElement?> FindClientAsync(string machineId)
        {
            var url = $"{_baseUrl}/rest/v1/clients?machine_id=eq.{Uri.EscapeDataString(machineId)}&select=id,is_blocked";
            var resp = await _http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.Clone();
            if (arr.GetArrayLength() > 0)
                return arr[0];
            return null;
        }

        private static async Task UpdateClientAsync(string clientId, string ip, string version, string name, string expiresAt)
        {
            var payload = new
            {
                last_seen = DateTime.UtcNow.ToString("o"),
                ip_address = ip,
                app_version = version,
                machine_name = name,
                expires_at = expiresAt
            };
            var body = JsonSerializer.Serialize(payload);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            await _http.SendAsync(req).ConfigureAwait(false);
        }

        private static async Task<string?> RegisterClientAsync(string machineId, string machineName, string ip, string version, string expiresAt)
        {
            var licenseKey = $"AC-{machineId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var payload = new
            {
                license_key = licenseKey,
                machine_id = machineId,
                machine_name = machineName,
                ip_address = ip,
                app_version = version,
                activated_at = DateTime.UtcNow.ToString("o"),
                expires_at = expiresAt,
                last_seen = DateTime.UtcNow.ToString("o"),
                is_blocked = false
            };
            var body = JsonSerializer.Serialize(payload);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/clients")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Prefer", "return=representation");
            var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var respJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);
            if (doc.RootElement.GetArrayLength() > 0)
                return doc.RootElement[0].GetProperty("id").GetString();
            return null;
        }

        private static async Task InsertTelemetryAsync(string clientId, string machineId, string ip, string version, string eventType, Dictionary<string, object>? eventData = null)
        {
            var dict = new Dictionary<string, object?>
            {
                ["client_id"] = clientId,
                ["machine_id"] = machineId,
                ["ip_address"] = ip,
                ["app_version"] = version,
                ["event_type"] = eventType
            };
            if (eventData != null && eventData.Count > 0)
                dict["event_data"] = eventData;

            var body = JsonSerializer.Serialize(dict);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/telemetry")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            await _http.SendAsync(req).ConfigureAwait(false);
        }
    }
}

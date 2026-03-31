using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AdminPanel
{
    public class ClientRecord
    {
        public string Id { get; set; } = "";
        public string LicenseKey { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public DateTime? ActivatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsBlocked { get; set; }
        public string? BlockReason { get; set; }
        public DateTime? LastSeen { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? Notes { get; set; }
        public string ProfileFirstName { get; set; } = "";
        public string ProfileLastName { get; set; } = "";
        public string RiskLevel { get; set; } = "OK";
        public int RiskScore { get; set; }
        public List<string> RiskReasons { get; set; } = new();
        public bool IsOutdatedVersion { get; set; }
        public int ErrorLikeCount { get; set; }
        public DateTime? LatestHeartbeatAt { get; set; }
        public int FirmsCount { get; set; }
        public int EmployeesCount { get; set; }

        public string ProfileFullName =>
            string.Join(" ", new[] { ProfileFirstName, ProfileLastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

        public string DisplayName =>
            string.IsNullOrWhiteSpace(ProfileFullName) ? MachineName : ProfileFullName;

        public string RiskDisplay =>
            $"{(string.IsNullOrWhiteSpace(RiskLevel) ? "Unknown" : RiskLevel)} ({RiskScore})";

        public string LatestHeartbeatDisplay =>
            LatestHeartbeatAt?.ToLocalTime().ToString("dd.MM HH:mm") ?? "—";

        public int AccessDaysRemaining =>
            !ExpiresAt.HasValue
                ? int.MaxValue
                : (int)(ExpiresAt.Value.Date - DateTime.UtcNow.Date).TotalDays;

        public string AccessStateCode
        {
            get
            {
                if (IsBlocked)
                    return "blocked";
                if (!ExpiresAt.HasValue)
                    return "unknown";
                if (AccessDaysRemaining < 0)
                    return "readonly";
                if (LooksLikeTrialWindow())
                    return "trial";
                return "activated";
            }
        }

        public string AccessStateLabel => AccessStateCode switch
        {
            "blocked" => "⛔ Blocked",
            "readonly" => "👁 Read-only",
            "trial" => "⏳ Trial",
            "activated" => "✅ Activated",
            _ => "❔ Unknown"
        };

        public string AccessStateDetail => AccessStateCode switch
        {
            "blocked" => string.IsNullOrWhiteSpace(BlockReason)
                ? "Клієнт заблокований адміністратором."
                : $"Заблоковано: {BlockReason}",
            "readonly" => $"Пробний період завершився {Math.Abs(AccessDaysRemaining)} дн. тому.",
            "trial" => $"Пробний період, ще {AccessDaysRemaining} дн.",
            "activated" => ExpiresAt.HasValue
                ? $"Активовано до {ExpiresAt.Value.ToLocalTime():dd.MM.yyyy}"
                : "Активовано",
            _ => "Статус потребує перевірки."
        };

        private bool LooksLikeTrialWindow()
        {
            if (!ExpiresAt.HasValue)
                return false;

            if (ActivatedAt.HasValue)
            {
                var activatedUtc = ActivatedAt.Value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(ActivatedAt.Value, DateTimeKind.Utc)
                    : ActivatedAt.Value.ToUniversalTime();
                return ExpiresAt.Value.Date <= activatedUtc.Date.AddDays(14);
            }

            return AccessDaysRemaining <= 14;
        }
    }

    public class TelemetryRecord
    {
        public long Id { get; set; }
        public string? ClientId { get; set; }
        public string MachineId { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string EventType { get; set; } = "";
        public JsonElement? EventData { get; set; }
        public DateTime? CreatedAt { get; set; }

        public string EventDataDisplay => EventData?.ValueKind == JsonValueKind.Object
            ? EventData.Value.ToString() : EventData?.ToString() ?? "";

        public string EventTypeDisplay => EventType switch
        {
            "app_started" => "Запуск програми",
            "first_launch" => "Перший запуск",
            "heartbeat" => "Heartbeat",
            "employee_added" => "Додано працівника",
            "firm_created" => "Створено фірму",
            _ when EventType.Contains("license", StringComparison.OrdinalIgnoreCase) => "Ліцензія",
            _ when EventType.Contains("activate", StringComparison.OrdinalIgnoreCase) => "Активація",
            _ when EventType.Contains("block", StringComparison.OrdinalIgnoreCase) => "Блокування",
            _ when EventType.Contains("error", StringComparison.OrdinalIgnoreCase) => "Помилка",
            _ => string.IsNullOrWhiteSpace(EventType) ? "Подія" : EventType
        };

        public string EventSummary
        {
            get
            {
                if (EventData?.ValueKind != JsonValueKind.Object)
                    return string.IsNullOrWhiteSpace(EventDataDisplay) ? "—" : EventDataDisplay;

                var data = EventData.Value;
                return EventType switch
                {
                    "app_started" => BuildStatsSummary("Програму запущено", data),
                    "first_launch" => BuildStatsSummary("Перший запуск програми", data),
                    "heartbeat" => BuildStatsSummary("Синхронізація стану", data),
                    "employee_added" => BuildEmployeeAddedSummary(data),
                    "firm_created" => BuildFirmCreatedSummary(data),
                    _ => BuildGenericSummary(data)
                };
            }
        }

        private static string BuildEmployeeAddedSummary(JsonElement data)
        {
            var employeeName = GetString(data, "employee_name");
            var firmName = GetString(data, "firm_name");
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(employeeName))
                parts.Add($"Працівник: {employeeName}");
            if (!string.IsNullOrWhiteSpace(firmName))
                parts.Add($"Фірма: {firmName}");

            AppendStats(parts, data);
            return parts.Count == 0 ? "Додано працівника" : string.Join(" | ", parts);
        }

        private static string BuildFirmCreatedSummary(JsonElement data)
        {
            var firmName = GetString(data, "firm_name");
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(firmName))
                parts.Add($"Фірма: {firmName}");

            AppendStats(parts, data);
            return parts.Count == 0 ? "Створено фірму" : string.Join(" | ", parts);
        }

        private static string BuildStatsSummary(string prefix, JsonElement data)
        {
            var parts = new List<string> { prefix };
            AppendStats(parts, data);
            return string.Join(" | ", parts);
        }

        private static void AppendStats(List<string> parts, JsonElement data)
        {
            if (TryGetInt(data, "firms_count", out var firms))
                parts.Add($"Фірм: {firms}");
            if (TryGetInt(data, "employees_count", out var employees))
                parts.Add($"Працівників: {employees}");
        }

        private static string BuildGenericSummary(JsonElement data)
        {
            var parts = new List<string>();

            AddValue(parts, "Фірма", GetString(data, "firm_name"));
            AddValue(parts, "Працівник", GetString(data, "employee_name"));
            AddValue(parts, "Повідомлення", GetString(data, "message"));
            AddValue(parts, "Причина", GetString(data, "reason"));

            AppendStats(parts, data);

            if (parts.Count > 0)
                return string.Join(" | ", parts);

            var raw = data.ToString();
            return string.IsNullOrWhiteSpace(raw) ? "—" : raw;
        }

        private static void AddValue(List<string> parts, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{label}: {value}");
        }

        private static string GetString(JsonElement data, string propertyName)
        {
            if (!data.TryGetProperty(propertyName, out var value))
                return string.Empty;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "Так",
                JsonValueKind.False => "Ні",
                _ => value.ToString()
            };
        }

        private static bool TryGetInt(JsonElement data, string propertyName, out int value)
        {
            value = 0;
            if (!data.TryGetProperty(propertyName, out var element))
                return false;

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value))
                return true;

            if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out value))
                return true;

            return false;
        }
    }

    public class ClientProfileRecord
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public bool MustResetPassword { get; set; }
        public bool RememberMeEnabled { get; set; }
        public int SessionVersion { get; set; } = 1;
        public DateTime? CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    internal sealed class AdminGatewayEnvelope<T>
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    internal sealed class AdminGatewayLoginResponse
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string AdminToken { get; set; } = string.Empty;
    }

    public sealed class TelemetryPageResult
    {
        public List<TelemetryRecord> Items { get; set; } = new();
        public string NextCursor { get; set; } = string.Empty;
        public bool HasMore { get; set; }
    }

    public class SupabaseService
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string _baseUrl;
        private readonly string _publicKey;
        private string _adminToken = string.Empty;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public SupabaseService(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _publicKey = BuildPublicKey();
        }

        public async Task<bool> AuthenticateAsync(string password)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/functions/v1/admin-gateway")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    action = "login",
                    admin_password = password
                }, _jsonOpts), Encoding.UTF8, "application/json")
            };
            SetHeaders(request, requireAuth: false);

            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return false;

            var payload = JsonSerializer.Deserialize<AdminGatewayLoginResponse>(json, _jsonOpts);
            if (payload?.Ok != true || string.IsNullOrWhiteSpace(payload.AdminToken))
                return false;

            _adminToken = payload.AdminToken;
            return true;
        }

        private static string BuildPublicKey()
        {
            var parts = new[]
            {
                "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9",
                "eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRzc2d4aGF0bmp2cXRoZGl5dXdvIiwi" +
                "cm9sZSI6ImFub24iLCJpYXQiOjE3NzI2NzUxODEsImV4cCI6MjA4ODI1MTE4MX0",
                "90eAJDS-zPA1Jlni_Lp2DdIrDxj_lMLn6AlKzJkW1kc"
            };
            return string.Join(".", parts);
        }

        private void SetHeaders(HttpRequestMessage req, bool requireAuth = true)
        {
            req.Headers.TryAddWithoutValidation("apikey", _publicKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {(requireAuth ? _adminToken : _publicKey)}");
        }

        private async Task<T?> CallAsync<T>(string action, object? payload = null)
        {
            if (string.IsNullOrWhiteSpace(_adminToken))
                throw new InvalidOperationException("Admin session is not initialized.");

            var body = payload == null
                ? JsonSerializer.Serialize(new { action }, _jsonOpts)
                : JsonSerializer.Serialize(new { action, payload }, _jsonOpts);

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/functions/v1/admin-gateway")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(json) ? $"Admin gateway failed for {action}." : json);

            var envelope = JsonSerializer.Deserialize<AdminGatewayEnvelope<T>>(json, _jsonOpts);
            if (envelope?.Ok != true)
                throw new InvalidOperationException(envelope?.Error ?? $"Admin gateway action '{action}' failed.");

            return envelope.Data;
        }

        public async Task<List<ClientRecord>> GetClientsAsync()
        {
            return await CallAsync<List<ClientRecord>>("list_clients") ?? new List<ClientRecord>();
        }

        public async Task<TelemetryPageResult> GetTelemetryPageAsync(string? clientId = null, int limit = 200, string? beforeCreatedAt = null)
        {
            return await CallAsync<TelemetryPageResult>("get_telemetry", new
            {
                client_id = clientId,
                limit,
                before_created_at = beforeCreatedAt
            }) ?? new TelemetryPageResult();
        }

        public async Task<List<TelemetryRecord>> GetTelemetryAsync(string? clientId = null, int limit = 200, string? beforeCreatedAt = null)
        {
            var page = await GetTelemetryPageAsync(clientId, limit, beforeCreatedAt);
            return page.Items;
        }

        public async Task BlockClientAsync(string clientId, string reason)
        {
            await CallAsync<object>("block_client", new { client_id = clientId, reason });
        }

        public async Task UnblockClientAsync(string clientId)
        {
            await CallAsync<object>("unblock_client", new { client_id = clientId });
        }

        public async Task BlockByIpAsync(string ip, string reason)
        {
            await CallAsync<object>("block_by_ip", new { ip_address = ip, reason });
        }

        public async Task ExtendLicenseAsync(string clientId, DateTime newExpiry)
        {
            await CallAsync<object>("extend_license", new
            {
                client_id = clientId,
                expires_at = newExpiry.ToUniversalTime().ToString("o")
            });
        }

        public async Task UpdateNotesAsync(string clientId, string notes)
        {
            await CallAsync<object>("update_notes", new { client_id = clientId, notes });
        }

        public async Task DeleteClientAsync(string clientId)
        {
            await CallAsync<object>("delete_client", new { client_id = clientId });
        }

        public async Task<ClientProfileRecord?> GetClientProfileAsync(string clientId)
        {
            return await CallAsync<ClientProfileRecord>("get_profile", new { client_id = clientId });
        }

        public async Task<ClientMirrorStateRecord?> GetClientMirrorStateAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.State;
        }

        public async Task<List<AdminMirrorAgencyRecord>> GetMirrorAgenciesAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Agencies;
        }

        public async Task<List<AdminMirrorEmployerRecord>> GetMirrorEmployersAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Employers;
        }

        public async Task<List<AdminMirrorEmployerAddressRecord>> GetMirrorEmployerAddressesAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Employers.SelectMany(item => item.Addresses).ToList();
        }

        public async Task<List<AdminMirrorEmployerPositionRecord>> GetMirrorEmployerPositionsAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Employers.SelectMany(item => item.Positions).ToList();
        }

        public async Task<List<AdminMirrorEmployeeRecord>> GetMirrorEmployeesAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Employees;
        }

        public async Task<List<AdminMirrorEmployeeFirmHistoryRecord>> GetMirrorEmployeeHistoryAsync(string clientId)
        {
            var snapshot = await GetClientMirrorSnapshotAsync(clientId);
            return snapshot.Employees.SelectMany(item => item.FirmHistory).ToList();
        }

        public async Task<ClientMirrorSnapshot> GetClientMirrorSnapshotAsync(string clientId)
        {
            return await CallAsync<ClientMirrorSnapshot>("get_mirror_snapshot", new { client_id = clientId })
                   ?? new ClientMirrorSnapshot();
        }

        public async Task<ClientProfileRecord?> ResetClientProfilePasswordAsync(string clientId)
        {
            return await CallAsync<ClientProfileRecord>("reset_password", new { client_id = clientId });
        }

        public async Task WriteAuditAsync(string? clientId, string actionType, object? oldValue, object? newValue, string? note, string actor = "admin-panel")
        {
            await CallAsync<object>("write_audit", new
            {
                client_id = clientId,
                action_type = actionType,
                old_value = oldValue,
                new_value = newValue,
                note,
                actor
            });
        }

        public async Task TryWriteAuditAsync(string? clientId, string actionType, object? oldValue, object? newValue, string? note, string actor = "admin-panel")
        {
            try
            {
                await WriteAuditAsync(clientId, actionType, oldValue, newValue, note, actor);
            }
            catch
            {
                // Audit should not block operator actions.
            }
        }

    }
}

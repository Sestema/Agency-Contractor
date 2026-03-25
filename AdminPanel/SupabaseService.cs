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

        public string ProfileFullName =>
            string.Join(" ", new[] { ProfileFirstName, ProfileLastName }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()));

        public string DisplayName =>
            string.IsNullOrWhiteSpace(ProfileFullName) ? MachineName : ProfileFullName;

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

    public class SupabaseService
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly string _baseUrl;
        private readonly string _serviceKey;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public SupabaseService(string baseUrl, string serviceKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _serviceKey = serviceKey;
        }

        private void SetHeaders(HttpRequestMessage req)
        {
            req.Headers.TryAddWithoutValidation("apikey", _serviceKey);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_serviceKey}");
        }

        public async Task<List<ClientRecord>> GetClientsAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/clients?select=*&order=last_seen.desc");
            SetHeaders(req);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ClientRecord>>(json, _jsonOpts) ?? new();
        }

        public async Task<List<TelemetryRecord>> GetTelemetryAsync(string? clientId = null, int limit = 200)
        {
            var filter = clientId != null ? $"&client_id=eq.{clientId}" : "";
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/telemetry?select=*&order=created_at.desc&limit={limit}{filter}");
            SetHeaders(req);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<TelemetryRecord>>(json, _jsonOpts) ?? new();
        }

        public async Task BlockClientAsync(string clientId, string reason)
        {
            var payload = JsonSerializer.Serialize(new { is_blocked = true, block_reason = reason }, _jsonOpts);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        public async Task UnblockClientAsync(string clientId)
        {
            var payload = JsonSerializer.Serialize(new { is_blocked = false, block_reason = (string?)null }, _jsonOpts);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        public async Task BlockByIpAsync(string ip, string reason)
        {
            var payload = JsonSerializer.Serialize(new { is_blocked = true, block_reason = reason }, _jsonOpts);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?ip_address=eq.{Uri.EscapeDataString(ip)}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        public async Task ExtendLicenseAsync(string clientId, DateTime newExpiry)
        {
            var payload = JsonSerializer.Serialize(new { expires_at = newExpiry.ToUniversalTime().ToString("o") }, _jsonOpts);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        public async Task UpdateNotesAsync(string clientId, string notes)
        {
            var payload = JsonSerializer.Serialize(new { notes }, _jsonOpts);
            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeleteClientAsync(string clientId)
        {
            var req1 = new HttpRequestMessage(HttpMethod.Delete,
                $"{_baseUrl}/rest/v1/telemetry?client_id=eq.{clientId}");
            SetHeaders(req1);
            var resp1 = await _http.SendAsync(req1);
            resp1.EnsureSuccessStatusCode();

            var req2 = new HttpRequestMessage(HttpMethod.Delete,
                $"{_baseUrl}/rest/v1/clients?id=eq.{clientId}");
            SetHeaders(req2);
            var resp2 = await _http.SendAsync(req2);
            resp2.EnsureSuccessStatusCode();
        }

        public async Task<ClientProfileRecord?> GetClientProfileAsync(string clientId)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/client_profiles?client_id=eq.{Uri.EscapeDataString(clientId)}&select=*");
            SetHeaders(req);

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                if (LooksLikeMissingTable(json))
                    return null;
                resp.EnsureSuccessStatusCode();
            }

            var profiles = JsonSerializer.Deserialize<List<ClientProfileRecord>>(json, _jsonOpts) ?? new();
            return profiles.Count == 0 ? null : profiles[0];
        }

        public async Task<List<ClientProfileRecord>> GetClientProfilesAsync()
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/client_profiles?select=*");
            SetHeaders(req);

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                if (LooksLikeMissingTable(json))
                    return new List<ClientProfileRecord>();
                resp.EnsureSuccessStatusCode();
            }

            return JsonSerializer.Deserialize<List<ClientProfileRecord>>(json, _jsonOpts) ?? new();
        }

        public async Task<ClientProfileRecord?> ResetClientProfilePasswordAsync(string clientId)
        {
            var profile = await GetClientProfileAsync(clientId);
            if (profile == null)
                return null;

            var randomPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var newSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var newHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                randomPassword,
                Convert.FromBase64String(newSalt),
                100_000,
                HashAlgorithmName.SHA256,
                32));

            var payload = JsonSerializer.Serialize(new
            {
                password_hash = newHash,
                password_salt = newSalt,
                must_reset_password = true,
                remember_me_enabled = false,
                session_version = profile.SessionVersion + 1
            }, _jsonOpts);

            var req = new HttpRequestMessage(new HttpMethod("PATCH"),
                $"{_baseUrl}/rest/v1/client_profiles?client_id=eq.{Uri.EscapeDataString(clientId)}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("Prefer", "return=representation");
            SetHeaders(req);

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();

            var profiles = JsonSerializer.Deserialize<List<ClientProfileRecord>>(json, _jsonOpts) ?? new();
            return profiles.Count == 0 ? null : profiles[0];
        }

        public async Task WriteAuditAsync(string? clientId, string actionType, object? oldValue, object? newValue, string? note, string actor = "admin-panel")
        {
            var body = JsonSerializer.Serialize(new
            {
                target_client_id = clientId,
                action_type = actionType,
                old_value_json = oldValue,
                new_value_json = newValue,
                note,
                actor
            }, _jsonOpts);

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/admin_audit_log")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            await SendMutationAsync(req);
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

        private async Task SendMutationAsync(HttpRequestMessage request)
        {
            var response = await _http.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode && !LooksLikeMissingTable(json))
                response.EnsureSuccessStatusCode();
        }

        private static bool LooksLikeMissingTable(string payload)
        {
            return (payload.Contains("relation", StringComparison.OrdinalIgnoreCase) &&
                    payload.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
                   || payload.Contains("schema cache", StringComparison.OrdinalIgnoreCase)
                   || payload.Contains("client_profiles", StringComparison.OrdinalIgnoreCase);
        }
    }
}

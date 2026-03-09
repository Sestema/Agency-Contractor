using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
        public string RiskLevel { get; set; } = "OK";
        public int RiskScore { get; set; }
        public List<string> RiskReasons { get; set; } = new();
        public string RiskReasonsDisplay => RiskReasons.Count == 0 ? "Немає ризиків" : string.Join(" | ", RiskReasons);
        public bool IsOutdatedVersion { get; set; }
        public int ErrorLikeCount { get; set; }
        public DateTime? LatestHeartbeatAt { get; set; }
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
    }

    public class AdminCommandRecord
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string CommandType { get; set; } = "";
        public JsonElement? PayloadJson { get; set; }
        public string Status { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public JsonElement? ResultJson { get; set; }
        public string? ErrorText { get; set; }
        public string PayloadDisplay => PayloadJson?.ToString() ?? "";
        public string ResultDisplay => ResultJson?.ToString() ?? "";
    }

    public class ClientPolicyRecord
    {
        public string ClientId { get; set; } = "";
        public string MinimumSupportedVersion { get; set; } = "";
        public string RecommendedVersion { get; set; } = "";
        public string UpdateChannel { get; set; } = "stable";
        public bool ForceUpdate { get; set; }
        public bool MaintenanceMode { get; set; }
        public bool ReadOnlyMode { get; set; }
        public bool DisableAI { get; set; }
        public bool DisableExports { get; set; }
        public bool HideTemplates { get; set; }
        public bool HideFinance { get; set; }
        public bool RequireOnlineCheck { get; set; }
        public string? AdminMessage { get; set; }
        public string PolicyVersion { get; set; } = "";
        public DateTime? UpdatedAt { get; set; }
    }

    public class AdminAuditRecord
    {
        public string Id { get; set; } = "";
        public string? TargetClientId { get; set; }
        public string ActionType { get; set; } = "";
        public JsonElement? OldValueJson { get; set; }
        public JsonElement? NewValueJson { get; set; }
        public string? Note { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string Actor { get; set; } = "";
        public string OldValueDisplay => OldValueJson?.ToString() ?? "";
        public string NewValueDisplay => NewValueJson?.ToString() ?? "";
    }

    public class ClientDiagnosticRecord
    {
        public string Id { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string Kind { get; set; } = "";
        public JsonElement? PayloadJson { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string PayloadDisplay => PayloadJson?.ToString() ?? "";
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

        public async Task<List<AdminCommandRecord>> GetAdminCommandsAsync(string? clientId = null, int limit = 100)
        {
            var filter = string.IsNullOrWhiteSpace(clientId) ? "" : $"&client_id=eq.{clientId}";
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/admin_commands?select=*&order=created_at.desc&limit={limit}{filter}");
            SetHeaders(req);
            return await SendListRequestAsync<AdminCommandRecord>(req);
        }

        public async Task<List<AdminAuditRecord>> GetAdminAuditLogAsync(string? clientId = null, int limit = 100)
        {
            var filter = string.IsNullOrWhiteSpace(clientId) ? "" : $"&target_client_id=eq.{clientId}";
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/admin_audit_log?select=*&order=created_at.desc&limit={limit}{filter}");
            SetHeaders(req);
            return await SendListRequestAsync<AdminAuditRecord>(req);
        }

        public async Task<List<ClientDiagnosticRecord>> GetClientDiagnosticsAsync(string? clientId = null, int limit = 50)
        {
            var filter = string.IsNullOrWhiteSpace(clientId) ? "" : $"&client_id=eq.{clientId}";
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/client_diagnostics?select=*&order=created_at.desc&limit={limit}{filter}");
            SetHeaders(req);
            return await SendListRequestAsync<ClientDiagnosticRecord>(req);
        }

        public async Task<ClientPolicyRecord?> GetClientPolicyAsync(string clientId)
        {
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{_baseUrl}/rest/v1/client_policies?client_id=eq.{clientId}&select=*&limit=1");
            SetHeaders(req);

            var list = await SendListRequestAsync<ClientPolicyRecord>(req);
            return list.Count > 0 ? list[0] : null;
        }

        public async Task UpsertClientPolicyAsync(ClientPolicyRecord policy)
        {
            var payload = JsonSerializer.Serialize(policy, _jsonOpts);
            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/client_policies")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            req.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates");
            await SendMutationAsync(req);
        }

        public async Task CreateAdminCommandAsync(string clientId, string commandType, object payload, string createdBy = "admin-panel")
        {
            var body = JsonSerializer.Serialize(new
            {
                client_id = clientId,
                command_type = commandType,
                payload_json = payload,
                status = "pending",
                created_by = createdBy
            }, _jsonOpts);

            var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/admin_commands")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            SetHeaders(req);
            await SendMutationAsync(req);
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

        private async Task<List<T>> SendListRequestAsync<T>(HttpRequestMessage request)
        {
            try
            {
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound || LooksLikeMissingTable(json))
                        return new List<T>();
                    response.EnsureSuccessStatusCode();
                }

                return JsonSerializer.Deserialize<List<T>>(json, _jsonOpts) ?? new List<T>();
            }
            catch (HttpRequestException)
            {
                return new List<T>();
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
            return payload.Contains("relation", StringComparison.OrdinalIgnoreCase) &&
                   payload.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public static class DiagnosticsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public static async Task<bool> UploadDiagnosticsAsync(string? clientId, string kind = "manual")
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return false;

            try
            {
                var payload = CollectDiagnosticsPayload();
                var body = JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["client_id"] = clientId,
                    ["kind"] = kind,
                    ["payload_json"] = payload
                }, JsonOptions);

                TelemetryService.ConfigureHeaders();
                var request = new HttpRequestMessage(HttpMethod.Post, $"{TelemetryService.BaseUrl}/rest/v1/client_diagnostics")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                var response = await TelemetryService.HttpClient.SendAsync(request).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.NotFound || LooksLikeMissingTable(responseText))
                        return false;

                    LoggingService.LogWarning("DiagnosticsService.Upload", responseText);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DiagnosticsService.Upload", ex.Message);
                return false;
            }
        }

        public static Dictionary<string, object?> CollectDiagnosticsPayload()
        {
            var activityEntries = App.ActivityLogService?.GetAll()
                .Take(50)
                .Select(entry => new Dictionary<string, object?>
                {
                    ["timestamp"] = entry.Timestamp,
                    ["action_type"] = entry.ActionType,
                    ["category"] = entry.Category,
                    ["firm_name"] = entry.FirmName,
                    ["employee_name"] = entry.EmployeeName,
                    ["description"] = entry.Description
                })
                .ToList() ?? new List<Dictionary<string, object?>>();

            return new Dictionary<string, object?>
            {
                ["captured_at"] = DateTime.UtcNow.ToString("o"),
                ["app_version"] = AppSettingsService.CurrentAppVersion,
                ["machine_id"] = LicenseService.GetMachineId(),
                ["root_path"] = App.FolderService?.RootPath ?? string.Empty,
                ["policy_version"] = App.AppSettingsService?.Settings.RemotePolicyVersion ?? string.Empty,
                ["read_only_mode"] = PolicyService.IsReadOnlyMode,
                ["maintenance_mode"] = App.AppSettingsService?.Settings.AdminMaintenanceMode ?? false,
                ["log_tail"] = LoggingService.GetRecentLogText(120),
                ["activity_entries"] = activityEntries
            };
        }

        private static bool LooksLikeMissingTable(string payload)
        {
            return payload.Contains("relation", StringComparison.OrdinalIgnoreCase) &&
                   payload.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class RemotePolicy
    {
        public string ClientId { get; set; } = string.Empty;
        public string MinimumSupportedVersion { get; set; } = string.Empty;
        public string RecommendedVersion { get; set; } = string.Empty;
        public string UpdateChannel { get; set; } = "stable";
        public bool ForceUpdate { get; set; }
        public bool MaintenanceMode { get; set; }
        public bool ReadOnlyMode { get; set; }
        public bool DisableAI { get; set; }
        public bool DisableExports { get; set; }
        public bool HideTemplates { get; set; }
        public bool HideFinance { get; set; }
        public bool RequireOnlineCheck { get; set; }
        public string AdminMessage { get; set; } = string.Empty;
        public string PolicyVersion { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }

    public static class PolicyService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };

        public static RemotePolicy CurrentPolicy { get; private set; } = new();

        public static bool IsReadOnlyMode => CurrentPolicy.ReadOnlyMode || (App.AppSettingsService?.Settings.AdminReadOnlyMode ?? false);
        public static bool IsExportsDisabled => CurrentPolicy.DisableExports || (App.AppSettingsService?.Settings.AdminDisableExports ?? false);
        public static bool IsAIDisabled => CurrentPolicy.DisableAI || (App.AppSettingsService?.Settings.AdminDisableAI ?? false);

        public static async Task<RemotePolicy?> FetchPolicyAsync(string? clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return null;

            try
            {
                TelemetryService.ConfigureHeaders();
                var url = $"{TelemetryService.BaseUrl}/rest/v1/client_policies?client_id=eq.{Uri.EscapeDataString(clientId)}&select=*&limit=1";
                var response = await TelemetryService.HttpClient.GetAsync(url).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (LooksLikeMissingTable(json))
                        return null;

                    LoggingService.LogWarning("PolicyService.FetchPolicy", json);
                    return null;
                }

                var items = JsonSerializer.Deserialize<List<RemotePolicy>>(json, JsonOptions);
                return items?.Count > 0 ? items[0] : null;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PolicyService.FetchPolicy", ex.Message);
                return null;
            }
        }

        public static async Task ApplyPolicyAsync(RemotePolicy? policy, bool saveSettings = true)
        {
            CurrentPolicy = policy ?? new RemotePolicy();

            var settings = App.AppSettingsService?.Settings;
            if (settings == null)
                return;

            settings.RemotePolicyVersion = CurrentPolicy.PolicyVersion ?? string.Empty;
            settings.AdminReadOnlyMode = CurrentPolicy.ReadOnlyMode;
            settings.AdminDisableAI = CurrentPolicy.DisableAI;
            settings.AdminDisableExports = CurrentPolicy.DisableExports;
            settings.AdminMaintenanceMode = CurrentPolicy.MaintenanceMode;
            settings.AdminHideTemplates = CurrentPolicy.HideTemplates;
            settings.AdminHideFinance = CurrentPolicy.HideFinance;
            settings.AdminMessage = CurrentPolicy.AdminMessage ?? string.Empty;
            settings.AdminUpdateChannel = string.IsNullOrWhiteSpace(CurrentPolicy.UpdateChannel) ? "stable" : CurrentPolicy.UpdateChannel;
            settings.AdminMinimumSupportedVersion = CurrentPolicy.MinimumSupportedVersion ?? string.Empty;
            settings.AdminRecommendedVersion = CurrentPolicy.RecommendedVersion ?? string.Empty;
            settings.AdminForceUpdate = CurrentPolicy.ForceUpdate;

            if (saveSettings && App.AppSettingsService != null)
                await App.AppSettingsService.SaveSettingsImmediate();
        }

        public static bool IsFeatureVisible(string featureId)
        {
            return featureId switch
            {
                "templates" => !CurrentPolicy.HideTemplates,
                "finances" => !CurrentPolicy.HideFinance,
                "aichat" => !CurrentPolicy.DisableAI,
                _ => true
            };
        }

        public static bool EnsureWriteAllowed(string actionName)
        {
            if (!IsReadOnlyMode)
                return true;

            var message = $"Дія \"{actionName}\" вимкнена. Клієнт переведений у read-only режим адміністратором.";
            ToastService.Instance.Warning(message);
            return false;
        }

        public static bool EnsureExportsAllowed(string actionName)
        {
            if (!IsExportsDisabled)
                return true;

            var message = $"Дія \"{actionName}\" вимкнена політикою адміністратора.";
            ToastService.Instance.Warning(message);
            return false;
        }

        public static bool IsCurrentVersionBelowMinimum(string currentVersion)
        {
            return CompareVersions(currentVersion, CurrentPolicy.MinimumSupportedVersion) < 0;
        }

        public static int CompareVersions(string? currentVersion, string? targetVersion)
        {
            if (!TryParseVersion(currentVersion, out var current))
                return -1;
            if (!TryParseVersion(targetVersion, out var target))
                return 1;

            return current.CompareTo(target);
        }

        private static bool TryParseVersion(string? value, out Version parsed)
        {
            parsed = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..];

            var normalized = new StringBuilder();
            foreach (var ch in trimmed)
            {
                if (char.IsDigit(ch) || ch == '.')
                    normalized.Append(ch);
                else
                    break;
            }

            if (normalized.Length == 0 || !Version.TryParse(normalized.ToString(), out var parsedVersion))
                return false;

            parsed = parsedVersion;
            return true;
        }

        private static bool LooksLikeMissingTable(string payload)
        {
            return payload.Contains("relation", StringComparison.OrdinalIgnoreCase) &&
                   payload.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
        }
    }
}

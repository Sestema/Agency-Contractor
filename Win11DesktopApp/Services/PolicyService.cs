using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

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
        private static AppSettingsService? _appSettingsService;
        private static CurrentProfileService? _currentProfileService;
        public static RemotePolicy CurrentPolicy { get; private set; } = new();

        public static void Initialize(AppSettingsService appSettingsService, CurrentProfileService? currentProfileService = null)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _currentProfileService = currentProfileService;
        }

        private static AppSettingsService.AppSettings? Settings => _appSettingsService?.Settings;

        public static bool IsReadOnlyMode => CurrentPolicy.ReadOnlyMode || (Settings?.AdminReadOnlyMode ?? false);
        public static bool IsExportsDisabled => CurrentPolicy.DisableExports || (Settings?.AdminDisableExports ?? false);
        public static bool IsAIDisabled => CurrentPolicy.DisableAI || (Settings?.AdminDisableAI ?? false);

        public static async Task<RemotePolicy?> FetchPolicyAsync(string? clientId)
        {
            await Task.Yield();
            if (string.IsNullOrWhiteSpace(clientId))
                return null;

            var cached = TelemetryService.GetCachedPolicy();
            if (cached != null && string.Equals(cached.ClientId, clientId, StringComparison.OrdinalIgnoreCase))
                return cached;

            return cached;
        }

        public static async Task ApplyPolicyAsync(RemotePolicy? policy, bool saveSettings = true)
        {
            CurrentPolicy = policy ?? new RemotePolicy();

            var settings = Settings;
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

            if (saveSettings && _appSettingsService != null)
                await _appSettingsService.SaveSettingsImmediate();
        }

        public static bool IsFeatureVisible(string featureId)
        {
            var settings = Settings;
            return featureId switch
            {
                "templates" => !CurrentPolicy.HideTemplates && !(settings?.AdminHideTemplates ?? false),
                "finances" => !CurrentPolicy.HideFinance && !(settings?.AdminHideFinance ?? false),
                "aichat" => !CurrentPolicy.DisableAI && !(settings?.AdminDisableAI ?? false),
                _ => true
            };
        }

        public static bool HasPermission(string permissionKey)
        {
            if (!IsMultiUserPermissionEnabled)
                return true;

            if (string.IsNullOrWhiteSpace(permissionKey))
                return true;

            var profile = _currentProfileService?.CurrentProfile;
            if (profile == null)
                return true;

            if (profile.IsActive == false)
                return false;

            var roleKey = NormalizeRoleKey(profile.RoleKey);
            if (string.Equals(roleKey, "owner", StringComparison.OrdinalIgnoreCase))
                return true;

            return ContainsPermission(profile, permissionKey);
        }

        public static bool RequirePermission(string permissionKey, string actionName)
        {
            var allowed = HasPermission(permissionKey);
            if (allowed)
                return true;

            var settings = Settings;
            var profile = _currentProfileService?.CurrentProfile;
            var user = profile == null
                ? "unknown"
                : $"{profile.FirstName} {profile.LastName}".Trim();

            LoggingService.LogWarning(
                "PolicyService.RequirePermission",
                $"Permission denied. action=\"{actionName}\" permission=\"{permissionKey}\" user=\"{user}\" role=\"{profile?.RoleKey ?? string.Empty}\" softMode={settings?.PermissionSoftMode ?? true} hardEnforcement={settings?.MultiUserHardEnforcement ?? false}.");

            if (settings?.PermissionSoftMode != false || settings?.MultiUserHardEnforcement != true)
                return true;

            ToastService.Instance.Warning($"Дія \"{actionName}\" недоступна для вашої ролі.");
            return false;
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

        private static bool IsMultiUserPermissionEnabled => Settings?.ExperimentalMultiUser == true;

        private static string NormalizeRoleKey(string? roleKey)
        {
            return string.IsNullOrWhiteSpace(roleKey)
                ? "owner"
                : roleKey.Trim();
        }

        private static bool ContainsPermission(ClientProfileRecord profile, string permissionKey)
        {
            if (profile.Permissions == null || profile.Permissions.Count == 0)
                return false;

            foreach (var permission in profile.Permissions)
            {
                if (string.Equals(permission, permissionKey, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(permission, "*", StringComparison.Ordinal))
                {
                    return true;
                }
            }

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
    }
}

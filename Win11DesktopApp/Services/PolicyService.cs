using System;
using System.Collections.Generic;
using System.Linq;
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

            var policyVersion = CurrentPolicy.PolicyVersion ?? string.Empty;
            var adminMessage = CurrentPolicy.AdminMessage ?? string.Empty;
            var updateChannel = string.IsNullOrWhiteSpace(CurrentPolicy.UpdateChannel) ? "stable" : CurrentPolicy.UpdateChannel;
            var minimumSupportedVersion = CurrentPolicy.MinimumSupportedVersion ?? string.Empty;
            var recommendedVersion = CurrentPolicy.RecommendedVersion ?? string.Empty;

            var changed =
                !string.Equals(settings.RemotePolicyVersion, policyVersion, StringComparison.Ordinal)
                || settings.AdminReadOnlyMode != CurrentPolicy.ReadOnlyMode
                || settings.AdminDisableAI != CurrentPolicy.DisableAI
                || settings.AdminDisableExports != CurrentPolicy.DisableExports
                || settings.AdminMaintenanceMode != CurrentPolicy.MaintenanceMode
                || settings.AdminHideTemplates != CurrentPolicy.HideTemplates
                || settings.AdminHideFinance != CurrentPolicy.HideFinance
                || !string.Equals(settings.AdminMessage, adminMessage, StringComparison.Ordinal)
                || !string.Equals(settings.AdminUpdateChannel, updateChannel, StringComparison.Ordinal)
                || !string.Equals(settings.AdminMinimumSupportedVersion, minimumSupportedVersion, StringComparison.Ordinal)
                || !string.Equals(settings.AdminRecommendedVersion, recommendedVersion, StringComparison.Ordinal)
                || settings.AdminForceUpdate != CurrentPolicy.ForceUpdate;

            settings.RemotePolicyVersion = policyVersion;
            settings.AdminReadOnlyMode = CurrentPolicy.ReadOnlyMode;
            settings.AdminDisableAI = CurrentPolicy.DisableAI;
            settings.AdminDisableExports = CurrentPolicy.DisableExports;
            settings.AdminMaintenanceMode = CurrentPolicy.MaintenanceMode;
            settings.AdminHideTemplates = CurrentPolicy.HideTemplates;
            settings.AdminHideFinance = CurrentPolicy.HideFinance;
            settings.AdminMessage = adminMessage;
            settings.AdminUpdateChannel = updateChannel;
            settings.AdminMinimumSupportedVersion = minimumSupportedVersion;
            settings.AdminRecommendedVersion = recommendedVersion;
            settings.AdminForceUpdate = CurrentPolicy.ForceUpdate;

            if (changed && saveSettings && _appSettingsService != null)
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

            var businessUser = _currentProfileService?.CurrentBusinessUser;
            if (businessUser != null)
                return HasBusinessUserPermission(businessUser, permissionKey);

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
            var businessUser = _currentProfileService?.CurrentBusinessUser;
            var user = businessUser != null
                ? $"{businessUser.FirstName} {businessUser.LastName}".Trim()
                : profile == null
                    ? "unknown"
                    : $"{profile.FirstName} {profile.LastName}".Trim();
            var roleKey = businessUser?.RoleKey ?? profile?.RoleKey ?? string.Empty;

            LoggingService.LogWarning(
                "PolicyService.RequirePermission",
                $"Permission denied. action=\"{actionName}\" permission=\"{permissionKey}\" user=\"{user}\" role=\"{roleKey}\" softMode={settings?.PermissionSoftMode ?? true} hardEnforcement={settings?.MultiUserHardEnforcement ?? false}.");

            if (settings?.PermissionSoftMode != false || settings?.MultiUserHardEnforcement != true)
                return true;

            var (moduleKey, requiredAccess) = ParsePermissionKey(permissionKey);
            var hasViewOnlyAccess = string.Equals(requiredAccess, "edit", StringComparison.OrdinalIgnoreCase)
                                    && !string.IsNullOrWhiteSpace(moduleKey)
                                    && HasPermission($"{moduleKey}:view");
            ToastService.Instance.Warning(hasViewOnlyAccess
                ? "У вас доступ тільки для перегляду. Ви не можете змінювати дані."
                : $"Дія \"{actionName}\" недоступна для вашої ролі.");
            return false;
        }

        public static bool EnsureWriteAllowed(string actionName)
        {
            if (!RequirePermission(ResolvePermissionKey(actionName, "edit"), actionName))
                return false;

            if (!IsReadOnlyMode)
                return true;

            var message = $"Дія \"{actionName}\" вимкнена. Клієнт переведений у read-only режим адміністратором.";
            ToastService.Instance.Warning(message);
            return false;
        }

        public static bool EnsureExportsAllowed(string actionName)
        {
            if (!RequirePermission(ResolvePermissionKey(actionName, "export"), actionName))
                return false;

            if (!IsExportsDisabled)
                return true;

            var message = $"Дія \"{actionName}\" вимкнена політикою адміністратора.";
            ToastService.Instance.Warning(message);
            return false;
        }

        public static bool HasActiveBusinessUser => _currentProfileService?.CurrentBusinessUser != null;

        public static bool IsCompanyDataScopeRestricted
        {
            get
            {
                var businessUser = _currentProfileService?.CurrentBusinessUser;
                if (businessUser == null || !businessUser.IsActive)
                    return false;

                var roleKey = NormalizeRoleKey(businessUser.RoleKey);
                if (string.Equals(roleKey, "owner", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(roleKey, "admin", StringComparison.OrdinalIgnoreCase))
                    return false;

                return !string.Equals(businessUser.AccessScope?.Mode, "AllData", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool CanAccessCompany(EmployerCompany? company)
        {
            if (company == null)
                return true;

            return CanAccessEmployer(company.Id.ToString(), company.Name, company.Agency?.Name);
        }

        public static bool CanEditCompany(EmployerCompany? company)
        {
            if (company == null)
                return true;

            return CanEditEmployer(company.Id.ToString(), company.Name, company.Agency?.Name);
        }

        public static bool CanAccessEmployer(string? employerCompanyId, string? employerName, string? agencyName)
        {
            var businessUser = _currentProfileService?.CurrentBusinessUser;
            if (businessUser == null)
                return true;

            if (!businessUser.IsActive)
                return false;

            var roleKey = NormalizeRoleKey(businessUser.RoleKey);
            if (string.Equals(roleKey, "owner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleKey, "admin", StringComparison.OrdinalIgnoreCase))
                return true;

            var scope = businessUser.AccessScope ?? new AppSettingsService.UserAccessScopeSetting();
            if (string.Equals(scope.Mode, "AllData", StringComparison.OrdinalIgnoreCase))
                return true;

            var employerAllowed = IsEmployerExplicitlyAllowed(businessUser, employerCompanyId);
            var agencyAllowed = !string.IsNullOrWhiteSpace(agencyName)
                                && scope.AgencyNames.Any(agency =>
                                    string.Equals(agency, agencyName.Trim(), StringComparison.OrdinalIgnoreCase));

            return scope.Mode switch
            {
                "SelectedAgencies" => agencyAllowed,
                "SelectedEmployers" => employerAllowed,
                "SelectedAgenciesAndEmployers" => employerAllowed,
                _ => true
            };
        }

        public static bool CanEditEmployer(string? employerCompanyId, string? employerName, string? agencyName)
        {
            var businessUser = _currentProfileService?.CurrentBusinessUser;
            if (businessUser == null)
                return true;

            if (!businessUser.IsActive)
                return false;

            var roleKey = NormalizeRoleKey(businessUser.RoleKey);
            if (string.Equals(roleKey, "owner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleKey, "admin", StringComparison.OrdinalIgnoreCase))
                return true;

            var scope = businessUser.AccessScope ?? new AppSettingsService.UserAccessScopeSetting();
            if (string.Equals(scope.Mode, "AllData", StringComparison.OrdinalIgnoreCase))
                return true;

            var employerAccessLevel = GetEmployerAccessLevel(businessUser, employerCompanyId);
            if (!string.IsNullOrWhiteSpace(employerAccessLevel))
                return AccessLevelRank(employerAccessLevel) >= AccessLevelRank("Edit");

            var agencyAllowed = !string.IsNullOrWhiteSpace(agencyName)
                                && scope.AgencyNames.Any(agency =>
                                    string.Equals(agency, agencyName.Trim(), StringComparison.OrdinalIgnoreCase));

            return string.Equals(scope.Mode, "SelectedAgencies", StringComparison.OrdinalIgnoreCase) && agencyAllowed;
        }

        public static bool RequireCanEditCompany(EmployerCompany? company, string actionName)
        {
            if (CanEditCompany(company))
                return true;

            var companyName = company?.Name ?? string.Empty;
            LoggingService.LogWarning(
                "PolicyService.RequireCanEditCompany",
                $"Employer edit denied. action=\"{actionName}\" company=\"{companyName}\".");
            ToastService.Instance.Warning($"Немає права редагувати: {companyName}");
            return false;
        }

        private static bool IsMultiUserPermissionEnabled => Settings?.ExperimentalMultiUser == true;

        private static bool IsEmployerExplicitlyAllowed(AppSettingsService.BusinessUserSetting user, string? employerCompanyId)
        {
            if (string.IsNullOrWhiteSpace(employerCompanyId))
                return false;

            var normalizedId = employerCompanyId.Trim();
            if (user.AccessScope?.EmployerCompanyIds.Any(id =>
                    string.Equals(id, normalizedId, StringComparison.OrdinalIgnoreCase)) == true)
                return true;

            return user.EmployerPermissions.Any(permission =>
                !string.Equals(permission.AccessLevel, "None", StringComparison.OrdinalIgnoreCase)
                && string.Equals(permission.EmployerCompanyId, normalizedId, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetEmployerAccessLevel(AppSettingsService.BusinessUserSetting user, string? employerCompanyId)
        {
            if (string.IsNullOrWhiteSpace(employerCompanyId))
                return string.Empty;

            var normalizedId = employerCompanyId.Trim();
            var permission = user.EmployerPermissions.FirstOrDefault(item =>
                string.Equals(item.EmployerCompanyId, normalizedId, StringComparison.OrdinalIgnoreCase));
            if (permission != null)
                return permission.AccessLevel;

            return user.AccessScope?.EmployerCompanyIds.Any(id =>
                string.Equals(id, normalizedId, StringComparison.OrdinalIgnoreCase)) == true
                ? "View"
                : string.Empty;
        }

        private static bool HasBusinessUserPermission(AppSettingsService.BusinessUserSetting user, string permissionKey)
        {
            if (!user.IsActive)
                return false;

            var roleKey = NormalizeRoleKey(user.RoleKey);
            if (string.Equals(roleKey, "owner", StringComparison.OrdinalIgnoreCase)
                || string.Equals(roleKey, "admin", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var (moduleKey, requiredAccess) = ParsePermissionKey(permissionKey);
            if (string.IsNullOrWhiteSpace(moduleKey))
                return true;

            var permission = user.ModulePermissions.FirstOrDefault(item =>
                string.Equals(item.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase));
            if (permission == null)
                return false;

            return AccessLevelRank(permission.AccessLevel) >= AccessLevelRank(requiredAccess);
        }

        private static string ResolvePermissionKey(string actionName, string requiredAccess)
        {
            var normalized = (actionName ?? string.Empty).Trim().ToLowerInvariant();
            var moduleKey = normalized switch
            {
                var value when value.Contains("зарплат", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("salary", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("аванс", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("витрат", StringComparison.OrdinalIgnoreCase) => "salary",
                var value when value.Contains("звіт", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("report", StringComparison.OrdinalIgnoreCase) => "reports",
                var value when value.Contains("фірм", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("компан", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("company", StringComparison.OrdinalIgnoreCase) => "companies",
                var value when value.Contains("документ", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("шаблон", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("document", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("template", StringComparison.OrdinalIgnoreCase) => "documents",
                var value when value.Contains("налаш", StringComparison.OrdinalIgnoreCase)
                               || value.Contains("settings", StringComparison.OrdinalIgnoreCase) => "settings",
                _ => "employees"
            };

            return $"{moduleKey}:{requiredAccess}";
        }

        private static (string ModuleKey, string RequiredAccess) ParsePermissionKey(string permissionKey)
        {
            var parts = permissionKey.Split(':', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return (string.Empty, "View");

            return (parts[0], parts.Length > 1 ? parts[1] : "View");
        }

        private static int AccessLevelRank(string? accessLevel)
        {
            return (accessLevel ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "export" => 3,
                "edit" => 2,
                "view" => 1,
                _ => 0
            };
        }

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

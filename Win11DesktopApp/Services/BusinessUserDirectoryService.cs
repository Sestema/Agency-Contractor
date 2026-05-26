using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Win11DesktopApp.Services;

public sealed class BusinessUserDirectoryService
{
    public const string FileName = ".agency-business-users.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppSettingsService _appSettingsService;
    private readonly CurrentProfileService _currentProfileService;
    private readonly FolderService _folderService;

    public BusinessUserDirectoryService(
        AppSettingsService appSettingsService,
        CurrentProfileService currentProfileService,
        FolderService folderService)
    {
        _appSettingsService = appSettingsService;
        _currentProfileService = currentProfileService;
        _folderService = folderService;
    }

    public bool CanSyncToRootFolder =>
        IsOwnerWorkspaceSession();

    public static bool IsOwnerWorkspaceSession(CurrentProfileService currentProfileService) =>
        currentProfileService.CurrentBusinessUser == null;

    private bool IsOwnerWorkspaceSession() =>
        IsOwnerWorkspaceSession(_currentProfileService);
    public static bool HasRootUserStore(string rootFolderPath)
    {
        if (string.IsNullOrWhiteSpace(rootFolderPath))
            return false;

        return File.Exists(GetStorePath(rootFolderPath));
    }

    public BusinessUserDirectorySyncResult SyncToRootFolder()
    {
        if (!_appSettingsService.Settings.ExperimentalMultiUser)
            return BusinessUserDirectorySyncResult.Skip("feature_disabled");

        if (!CanSyncToRootFolder)
            return BusinessUserDirectorySyncResult.Skip("owner_only");

        var rootFolderPath = _appSettingsService.Settings.RootFolderPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootFolderPath))
            return BusinessUserDirectorySyncResult.Skip("root_missing");

        if (!Directory.Exists(rootFolderPath))
            return BusinessUserDirectorySyncResult.Failed("root_not_found");

        if (!_folderService.TryGetWorkspacePassport(rootFolderPath, out var passport)
            || passport == null
            || string.IsNullOrWhiteSpace(passport.WorkspaceId))
        {
            return BusinessUserDirectorySyncResult.Failed("workspace_passport_missing");
        }

        try
        {
            var document = new BusinessUserDirectoryDocument
            {
                Version = 1,
                WorkspaceId = passport.WorkspaceId,
                OwnerClientId = passport.OwnerClientId,
                UpdatedAtUtc = DateTime.UtcNow,
                Users = _appSettingsService.Settings.BusinessUsers
                    .Select(CloneUser)
                    .ToList()
            };

            var path = GetStorePath(rootFolderPath);
            SafeFileService.WriteJsonAtomic(path, document, JsonOptions);
            return BusinessUserDirectorySyncResult.Succeeded(path);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("BusinessUserDirectoryService.SyncToRootFolder", ex.Message);
            return BusinessUserDirectorySyncResult.Failed(ex.Message);
        }
    }

    public BusinessUserDirectoryImportResult ImportFromRootFolder(string? rootFolderPath = null)
    {
        var path = rootFolderPath?.Trim() ?? _appSettingsService.Settings.RootFolderPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return BusinessUserDirectoryImportResult.Failed("root_missing");

        var storePath = GetStorePath(path);
        if (!File.Exists(storePath))
            return BusinessUserDirectoryImportResult.Failed("store_missing");

        try
        {
            var json = SafeFileService.ReadAllTextShared(storePath);
            var document = JsonSerializer.Deserialize<BusinessUserDirectoryDocument>(json, JsonOptions);
            if (document?.Users == null || document.Users.Count == 0)
                return BusinessUserDirectoryImportResult.Failed("store_empty");

            if (_folderService.TryGetWorkspacePassport(path, out var passport)
                && passport != null
                && !string.IsNullOrWhiteSpace(passport.WorkspaceId)
                && !string.IsNullOrWhiteSpace(document.WorkspaceId)
                && !string.Equals(passport.WorkspaceId, document.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                return BusinessUserDirectoryImportResult.Failed("workspace_mismatch");
            }

            _appSettingsService.Settings.BusinessUsers = document.Users
                .Select(CloneUser)
                .ToList();

            var currentUserId = _appSettingsService.Settings.CurrentBusinessUserId;
            if (!string.IsNullOrWhiteSpace(currentUserId)
                && !_appSettingsService.Settings.BusinessUsers.Any(user =>
                    user.IsActive
                    && string.Equals(user.UserId, currentUserId, StringComparison.OrdinalIgnoreCase)))
            {
                _appSettingsService.Settings.CurrentBusinessUserId = string.Empty;
            }

            _appSettingsService.SaveSettings();
            return BusinessUserDirectoryImportResult.Succeeded(document.Users.Count);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("BusinessUserDirectoryService.ImportFromRootFolder", ex.Message);
            return BusinessUserDirectoryImportResult.Failed(ex.Message);
        }
    }

    private static string GetStorePath(string rootFolderPath) =>
        Path.Combine(rootFolderPath, FileName);

    private static AppSettingsService.BusinessUserSetting CloneUser(AppSettingsService.BusinessUserSetting user) =>
        new()
        {
            UserId = user.UserId,
            Login = user.Login,
            FirstName = user.FirstName,
            LastName = user.LastName,
            RoleKey = user.RoleKey,
            IsActive = user.IsActive,
            PasswordHash = user.PasswordHash,
            PasswordSalt = user.PasswordSalt,
            MustChangePassword = user.MustChangePassword,
            CreatedAtUtc = user.CreatedAtUtc,
            LastLoginAtUtc = user.LastLoginAtUtc,
            AccessScope = user.AccessScope == null
                ? new AppSettingsService.UserAccessScopeSetting()
                : new AppSettingsService.UserAccessScopeSetting
                {
                    Mode = user.AccessScope.Mode,
                    AgencyNames = user.AccessScope.AgencyNames?.ToList() ?? new List<string>(),
                    EmployerCompanyIds = user.AccessScope.EmployerCompanyIds?.ToList() ?? new List<string>()
                },
            ModulePermissions = user.ModulePermissions?
                .Select(permission => new AppSettingsService.ModulePermissionSetting
                {
                    ModuleKey = permission.ModuleKey,
                    AccessLevel = permission.AccessLevel
                })
                .ToList() ?? new List<AppSettingsService.ModulePermissionSetting>(),
            EmployerPermissions = user.EmployerPermissions?
                .Select(permission => new AppSettingsService.EmployerPermissionSetting
                {
                    EmployerCompanyId = permission.EmployerCompanyId,
                    AccessLevel = permission.AccessLevel
                })
                .ToList() ?? new List<AppSettingsService.EmployerPermissionSetting>()
        };

    private sealed class BusinessUserDirectoryDocument
    {
        public int Version { get; set; } = 1;
        public string WorkspaceId { get; set; } = string.Empty;
        public string OwnerClientId { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public List<AppSettingsService.BusinessUserSetting> Users { get; set; } = new();
    }
}

public sealed class BusinessUserDirectorySyncResult
{
    public bool Success { get; init; }
    public bool WasSkipped { get; init; }
    public string Message { get; init; } = string.Empty;

    public static BusinessUserDirectorySyncResult Succeeded(string path) =>
        new() { Success = true, Message = path };

    public static BusinessUserDirectorySyncResult Skip(string reason) =>
        new() { WasSkipped = true, Message = reason };

    public static BusinessUserDirectorySyncResult Failed(string message) =>
        new() { Message = message };
}

public sealed class BusinessUserDirectoryImportResult
{
    public bool Success { get; init; }
    public int ImportedCount { get; init; }
    public string Message { get; init; } = string.Empty;

    public static BusinessUserDirectoryImportResult Succeeded(int count) =>
        new() { Success = true, ImportedCount = count };

    public static BusinessUserDirectoryImportResult Failed(string message) =>
        new() { Message = message };
}

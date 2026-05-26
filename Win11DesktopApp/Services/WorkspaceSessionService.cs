using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services;

public sealed class WorkspaceSessionService
{
    private readonly AppSettingsService _appSettingsService;
    private readonly FolderService _folderService;
    private readonly CurrentProfileService _currentProfileService;

    public WorkspaceSessionService(
        AppSettingsService appSettingsService,
        FolderService folderService,
        CurrentProfileService currentProfileService)
    {
        _appSettingsService = appSettingsService;
        _folderService = folderService;
        _currentProfileService = currentProfileService;
    }

    public bool CanReportSessions =>
        _appSettingsService.Settings.ExperimentalMultiUser;

    public async Task<WorkspaceSessionHeartbeatResult> ReportSessionAsync(string? ownerClientId = null)
    {
        if (!CanReportSessions)
            return WorkspaceSessionHeartbeatResult.Skipped("feature_disabled");

        var rootFolderPath = _appSettingsService.Settings.RootFolderPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootFolderPath))
            return WorkspaceSessionHeartbeatResult.Skipped("root_missing");

        var passportResult = _folderService.EnsureWorkspacePassport(rootFolderPath, allowCreate: IsOwnerActor());
        if (passportResult.HasConflict)
            return WorkspaceSessionHeartbeatResult.Failed("workspace_passport_conflict");

        if (!passportResult.Success || passportResult.Passport == null)
            return WorkspaceSessionHeartbeatResult.Failed(passportResult.Message);

        var passport = passportResult.Passport;
        var resolvedOwnerClientId = ResolveOwnerClientId(ownerClientId, passport);
        if (string.IsNullOrWhiteSpace(resolvedOwnerClientId))
            return WorkspaceSessionHeartbeatResult.Skipped("owner_client_missing");

        if (IsOwnerActor())
            _folderService.TryBindWorkspaceOwner(rootFolderPath, resolvedOwnerClientId);

        var actor = BuildActorContext();
        var response = await TelemetryService.ReportWorkspaceDeviceSessionAsync(
            resolvedOwnerClientId,
            passport.WorkspaceId,
            actor.ActorKind,
            actor.LocalUserId,
            actor.DisplayName).ConfigureAwait(false);

        if (response == null)
            return WorkspaceSessionHeartbeatResult.Failed("gateway_unavailable");

        if (!response.Ok)
        {
            if (!string.IsNullOrWhiteSpace(response.TenantId))
                _folderService.TryBindWorkspaceTenant(rootFolderPath, response.TenantId);

            return new WorkspaceSessionHeartbeatResult
            {
                Message = response.Error ?? "workspace_session_failed",
                ErrorCode = response.Error,
                MaxDevices = response.MaxDevices,
                ActiveDevices = response.ActiveDevices
            };
        }

        if (!string.IsNullOrWhiteSpace(response.TenantId))
            _folderService.TryBindWorkspaceTenant(rootFolderPath, response.TenantId);

        return WorkspaceSessionHeartbeatResult.Succeeded(response.ActiveDevices, response.MaxDevices);
    }

    public async Task<WorkspaceDeviceStatusResult?> RefreshWorkspaceStatusAsync(string? ownerClientId = null)
    {
        if (!CanReportSessions || !IsOwnerActor())
            return null;

        var rootFolderPath = _appSettingsService.Settings.RootFolderPath?.Trim() ?? string.Empty;
        if (!_folderService.TryGetWorkspacePassport(rootFolderPath, out var passport) || passport == null)
            return null;

        var resolvedOwnerClientId = ResolveOwnerClientId(ownerClientId, passport);
        if (string.IsNullOrWhiteSpace(resolvedOwnerClientId))
            return null;

        return await TelemetryService.GetWorkspaceDeviceStatusAsync(
            resolvedOwnerClientId,
            passport.WorkspaceId).ConfigureAwait(false);
    }

    public Task EndSessionAsync()
    {
        if (!CanReportSessions)
            return Task.CompletedTask;

        var rootFolderPath = _appSettingsService.Settings.RootFolderPath?.Trim() ?? string.Empty;
        if (!_folderService.TryGetWorkspacePassport(rootFolderPath, out var passport) || passport == null)
            return Task.CompletedTask;

        var ownerClientId = ResolveOwnerClientId(null, passport);
        if (string.IsNullOrWhiteSpace(ownerClientId))
            return Task.CompletedTask;

        return TelemetryService.EndWorkspaceDeviceSessionAsync(ownerClientId, passport.WorkspaceId);
    }

    private string? ResolveOwnerClientId(string? ownerClientId, FolderService.WorkspacePassport passport)
    {
        if (!string.IsNullOrWhiteSpace(ownerClientId))
            return ownerClientId.Trim();

        if (!string.IsNullOrWhiteSpace(passport.OwnerClientId))
            return passport.OwnerClientId.Trim();

        if (IsOwnerActor())
            return TelemetryService.GetCurrentClientId();

        return null;
    }

    private bool IsOwnerActor() => _currentProfileService.CurrentBusinessUser == null;

    private (string ActorKind, string LocalUserId, string DisplayName) BuildActorContext()
    {
        var member = _currentProfileService.CurrentBusinessUser;
        if (member != null)
        {
            var displayName = string.Join(" ", new[] { member.FirstName, member.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = member.Login;

            return ("member", member.UserId, displayName);
        }

        var profile = _currentProfileService.CurrentProfile;
        var ownerName = profile == null
            ? string.Empty
            : string.Join(" ", new[] { profile.FirstName, profile.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))).Trim();

        return ("owner", string.Empty, ownerName);
    }
}

public sealed class WorkspaceSessionHeartbeatResult
{
    public bool Success { get; init; }
    public bool WasSkipped { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public int? MaxDevices { get; init; }
    public int? ActiveDevices { get; init; }

    public static WorkspaceSessionHeartbeatResult Succeeded(int? activeDevices, int? maxDevices) =>
        new() { Success = true, ActiveDevices = activeDevices, MaxDevices = maxDevices };

    public static WorkspaceSessionHeartbeatResult Skipped(string reason) =>
        new() { WasSkipped = true, Message = reason };

    public static WorkspaceSessionHeartbeatResult Failed(string message) =>
        new() { Message = message };
}

public sealed class WorkspaceDeviceStatusResult
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
    public int MaxDevices { get; init; }
    public int DevicesOnline { get; init; }
    public List<WorkspaceDeviceUserStatus> Users { get; init; } = new();
    public List<WorkspaceDeviceSessionStatus> Sessions { get; init; } = new();
}

public sealed class WorkspaceDeviceUserStatus
{
    public string LocalUserId { get; init; } = string.Empty;
    public string ActorKind { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public DateTime? LastSeenAtUtc { get; init; }
    public int DevicesOnline { get; init; }
    public int DevicesTotal { get; init; }
}

public sealed class WorkspaceDeviceSessionStatus
{
    public string WorkspaceId { get; init; } = string.Empty;
    public string MachineId { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string WindowsUser { get; init; } = string.Empty;
    public string ActorKind { get; init; } = string.Empty;
    public string LocalUserId { get; init; } = string.Empty;
    public string ActorDisplayName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public DateTime? LastSeenAtUtc { get; init; }
    public bool IsOnline { get; init; }
}

public sealed class WorkspaceDeviceSessionGatewayResponse
{
    public bool Ok { get; init; }
    public string Error { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public int MaxDevices { get; init; }
    public int ActiveDevices { get; init; }
}

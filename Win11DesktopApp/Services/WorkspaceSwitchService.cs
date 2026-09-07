using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Win11DesktopApp.Services
{
    public sealed class WorkspaceItem
    {
        public string FolderPath { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string WorkspaceId { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public bool IsAvailable { get; init; }
        public string StatusText { get; init; } = string.Empty;
        public bool CanSwitch => !IsActive && IsAvailable;
        public bool CanRemove => !IsActive;
        public bool CanReplace => true;
        public bool HasWarningStatus => !IsActive && !IsAvailable;
    }

    public sealed class WorkspaceSwitchResult
    {
        public bool Success { get; init; }
        public bool NeedsDiscardConfirm { get; init; }
        public bool NeedsSwitch { get; init; }
        public string Message { get; init; } = string.Empty;

        public static WorkspaceSwitchResult Ok(string message = "") => new() { Success = true, Message = message };
        public static WorkspaceSwitchResult Fail(string message) => new() { Success = false, Message = message };
        public static WorkspaceSwitchResult SwitchRequired(string message) =>
            new() { Success = true, NeedsSwitch = true, Message = message };
        public static WorkspaceSwitchResult DiscardRequired() => new()
        {
            Success = false,
            NeedsDiscardConfirm = true,
            Message = Application.Current?.TryFindResource("WorkspaceSalaryDiscardRequired") as string
                ?? "WorkspaceSalaryDiscardRequired"
        };
    }

    public sealed class WorkspaceSwitchService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly NavigationService _navigationService;
        private readonly CurrentProfileService _currentProfileService;

        public WorkspaceSwitchService(
            AppSettingsService appSettingsService,
            FolderService folderService,
            NavigationService navigationService,
            CurrentProfileService currentProfileService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _currentProfileService = currentProfileService ?? throw new ArgumentNullException(nameof(currentProfileService));
        }

        public bool AllowCreatePassport =>
            _currentProfileService.CurrentBusinessUser == null
            || _currentProfileService.CurrentProfile != null;

        public event EventHandler? WorkspacesChanged;

        public string CurrentDisplayName
        {
            get
            {
                var current = _appSettingsService.Settings.RootFolderPath;
                var item = GetWorkspaces().FirstOrDefault(w => w.IsActive);
                if (item != null && !string.IsNullOrWhiteSpace(item.DisplayName))
                    return item.DisplayName;
                return AppSettingsService.SafeWorkspaceDisplayName(current);
            }
        }

        public bool IsPostgresStorageMode =>
            string.Equals(
                _appSettingsService.Settings.DatabaseStorageMode,
                DatabaseStorageModes.Postgres,
                StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<WorkspaceItem> GetWorkspaces()
        {
            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            var current = settings.RootFolderPath;

            return settings.Workspaces
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
                .Select(entry =>
                {
                    var exists = Directory.Exists(entry.Path);
                    var isActive = AppSettingsService.PathsEqual(entry.Path, current);
                    var displayName = AppSettingsService.SafeWorkspaceDisplayName(entry.Path);
                    if (!string.Equals(entry.DisplayName, displayName, StringComparison.Ordinal))
                        entry.DisplayName = displayName;
                    return new WorkspaceItem
                    {
                        FolderPath = entry.Path,
                        DisplayName = displayName,
                        WorkspaceId = entry.WorkspaceId ?? string.Empty,
                        IsActive = isActive,
                        IsAvailable = exists,
                        StatusText = isActive
                            ? Res("WorkspaceActiveBadge")
                            : exists
                                ? string.Empty
                                : Res("WorkspaceUnavailableMissingFolder")
                    };
                })
                .ToList();
        }

        public bool TryPickFolder(string? initialDirectory, out string folderPath)
        {
            folderPath = string.Empty;
            var dialog = new OpenFolderDialog();
            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
                dialog.InitialDirectory = initialDirectory;

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return false;

            folderPath = dialog.FolderName;
            return true;
        }

        public WorkspaceSwitchResult TryAddWorkspace(string folderPath, bool allowCreatePassport)
        {
            var prepared = TryReadNewWorkspace(folderPath, allowCreatePassport, ignoreExistingPath: null);
            if (!prepared.Success)
                return WorkspaceSwitchResult.Fail(prepared.Message);

            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            settings.Workspaces.Add(new AppSettingsService.WorkspaceSetting
            {
                Path = prepared.NormalizedPath,
                WorkspaceId = prepared.WorkspaceId,
                DisplayName = prepared.DisplayName,
                LastOpenedAtUtc = DateTime.UtcNow
            });
            _appSettingsService.SaveSettings();
            RaiseWorkspacesChanged();
            return WorkspaceSwitchResult.Ok(Res("WorkspaceAdded"));
        }

        public WorkspaceSwitchResult TryReplaceWorkspace(
            string oldFolderPath,
            string newFolderPath,
            bool allowCreatePassport,
            bool commitActiveReplacement = false)
        {
            if (AppSettingsService.PathsEqual(oldFolderPath, newFolderPath))
                return WorkspaceSwitchResult.Fail(Res("WorkspaceSameFolder"));

            var prepared = TryReadNewWorkspace(newFolderPath, allowCreatePassport, ignoreExistingPath: oldFolderPath);
            if (!prepared.Success)
                return WorkspaceSwitchResult.Fail(prepared.Message);

            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            var oldEntry = settings.Workspaces.FirstOrDefault(entry =>
                AppSettingsService.PathsEqual(entry.Path, oldFolderPath));
            if (oldEntry == null)
                return WorkspaceSwitchResult.Fail(Res("WorkspaceAddFailed"));

            var wasActive = AppSettingsService.PathsEqual(oldFolderPath, settings.RootFolderPath);
            if (wasActive && !commitActiveReplacement)
                return WorkspaceSwitchResult.SwitchRequired(prepared.DisplayName);

            oldEntry.Path = prepared.NormalizedPath;
            oldEntry.WorkspaceId = prepared.WorkspaceId;
            oldEntry.DisplayName = prepared.DisplayName;
            oldEntry.LastOpenedAtUtc = DateTime.UtcNow;
            _appSettingsService.SaveSettings();
            RaiseWorkspacesChanged();

            return wasActive
                ? WorkspaceSwitchResult.SwitchRequired(prepared.DisplayName)
                : WorkspaceSwitchResult.Ok(Res("WorkspaceReplaced"));
        }

        public WorkspaceSwitchResult TryRemoveWorkspace(string folderPath)
        {
            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            if (AppSettingsService.PathsEqual(folderPath, settings.RootFolderPath))
                return WorkspaceSwitchResult.Fail(Res("WorkspaceCannotRemoveActive"));

            var removed = settings.Workspaces.RemoveAll(entry => AppSettingsService.PathsEqual(entry.Path, folderPath));
            if (removed == 0)
                return WorkspaceSwitchResult.Fail(Res("WorkspaceAddFailed"));

            _appSettingsService.SaveSettings();
            RaiseWorkspacesChanged();
            return WorkspaceSwitchResult.Ok(Res("WorkspaceRemoved"));
        }

        public bool CanSwitchTo(string folderPath, out string blockReason)
        {
            blockReason = string.Empty;
            if (AppSettingsService.PathsEqual(folderPath, _appSettingsService.Settings.RootFolderPath))
            {
                blockReason = Res("WorkspaceSameFolder");
                return false;
            }

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                blockReason = Res("WorkspaceUnavailableMissingFolder");
                return false;
            }

            return true;
        }

        public string BuildConfirmMessage(string displayName)
        {
            var message = string.Format(Res("WorkspaceConfirmMessageFmt"), displayName);
            if (IsPostgresStorageMode)
                message += Environment.NewLine + Environment.NewLine + Res("WorkspacePostgresHint");
            return message;
        }

        public string BuildReplaceConfirmMessage(string displayName)
        {
            var message = string.Format(Res("WorkspaceReplaceConfirmMessageFmt"), displayName);
            if (IsPostgresStorageMode)
                message += Environment.NewLine + Environment.NewLine + Res("WorkspacePostgresHint");
            return message;
        }

        public async Task<WorkspaceSwitchResult> PrepareSwitchAsync(string folderPath, bool discardUnsavedSalary = false)
        {
            if (!CanSwitchTo(folderPath, out var reason))
                return WorkspaceSwitchResult.Fail(reason);

            var salaryGuard = TryGetSalaryGuard();
            if (!discardUnsavedSalary && salaryGuard != null && salaryGuard.HasUnsavedSalaryChanges())
            {
                if (salaryGuard.CanPersistUnsavedSalaryChanges)
                {
                    var saved = await salaryGuard.PersistUnsavedSalaryChangesAsync().ConfigureAwait(true);
                    if (!saved)
                        return WorkspaceSwitchResult.Fail(Res("WorkspaceSalarySaveFailed"));
                }
                else
                {
                    return WorkspaceSwitchResult.DiscardRequired();
                }
            }

            WritePendingSwitch(folderPath);
            await _appSettingsService.SaveSettingsImmediate().ConfigureAwait(true);
            return WorkspaceSwitchResult.Ok();
        }

        public void WritePendingSwitch(string folderPath)
        {
            _appSettingsService.BeginWorkspaceHandoff();
            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            var normalized = AppSettingsService.NormalizeWorkspacePath(folderPath);
            settings.PendingWorkspacePath = normalized;

            settings.SelectedCompanyId = string.Empty;
            settings.HiddenCompanyIds = new List<string>();
            settings.HiddenTags = new List<string>();
            settings.EmployeeSortField = "Name";
            settings.EmployeeSortAscending = true;
            settings.EncryptedBusinessUserSessionToken = string.Empty;
            settings.CurrentBusinessUserId = string.Empty;
            settings.RememberedBusinessUserId = string.Empty;

            var match = settings.Workspaces.FirstOrDefault(entry => AppSettingsService.PathsEqual(entry.Path, normalized));
            if (match != null)
                match.LastOpenedAtUtc = DateTime.UtcNow;
        }

        public bool TryRestartApplication(out string error)
        {
            error = string.Empty;
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    error = Res("SettingsRestartPathMissing");
                    return false;
                }

                Process.Start(Program.CreateRestartStartInfo(exePath));

                Application.Current?.Shutdown();
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WorkspaceSwitchService.Restart", ex.Message);
                error = string.Format(Res("SettingsRestartFailedFmt"), ex.Message);
                return false;
            }
        }

        public ISalaryWorkspaceGuard? TryGetSalaryGuard()
        {
            return _navigationService.CurrentView as ISalaryWorkspaceGuard;
        }

        private void RaiseWorkspacesChanged() => WorkspacesChanged?.Invoke(this, EventArgs.Empty);

        private WorkspacePrepareResult TryReadNewWorkspace(
            string folderPath,
            bool allowCreatePassport,
            string? ignoreExistingPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return WorkspacePrepareResult.Fail(Res("WorkspaceUnavailableMissingFolder"));

            var normalized = AppSettingsService.NormalizeWorkspacePath(folderPath);
            var passportResult = _folderService.EnsureWorkspacePassport(normalized, allowCreatePassport);
            if (passportResult.HasConflict)
                return WorkspacePrepareResult.Fail(Res("WorkspacePassportConflictError"));
            if (!passportResult.Success || passportResult.Passport == null)
            {
                return WorkspacePrepareResult.Fail(allowCreatePassport
                    ? (string.IsNullOrWhiteSpace(passportResult.Message)
                        ? Res("WorkspaceAddFailed")
                        : passportResult.Message)
                    : Res("WorkspacePassportMissingError"));
            }

            var settings = _appSettingsService.Settings;
            settings.Workspaces ??= new List<AppSettingsService.WorkspaceSetting>();
            var workspaceId = passportResult.Passport.WorkspaceId ?? string.Empty;

            if (settings.Workspaces.Any(entry =>
                !AppSettingsService.PathsEqual(entry.Path, ignoreExistingPath)
                && (AppSettingsService.PathsEqual(entry.Path, normalized)
                    || (!string.IsNullOrWhiteSpace(workspaceId)
                        && !string.IsNullOrWhiteSpace(entry.WorkspaceId)
                        && string.Equals(entry.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase)))))
            {
                return WorkspacePrepareResult.Fail(Res("WorkspaceAlreadyAdded"));
            }

            var displayName = AppSettingsService.SafeWorkspaceDisplayName(normalized);

            return WorkspacePrepareResult.Ok(normalized, workspaceId, displayName);
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private sealed class WorkspacePrepareResult
        {
            public bool Success { get; init; }
            public string Message { get; init; } = string.Empty;
            public string NormalizedPath { get; init; } = string.Empty;
            public string WorkspaceId { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;

            public static WorkspacePrepareResult Ok(string path, string workspaceId, string displayName) => new()
            {
                Success = true,
                NormalizedPath = path,
                WorkspaceId = workspaceId,
                DisplayName = displayName
            };

            public static WorkspacePrepareResult Fail(string message) => new()
            {
                Success = false,
                Message = message
            };
        }
    }

    public interface ISalaryWorkspaceGuard
    {
        bool HasUnsavedSalaryChanges();
        bool CanPersistUnsavedSalaryChanges { get; }
        Task<bool> PersistUnsavedSalaryChangesAsync();
    }
}

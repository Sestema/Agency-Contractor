using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public class ActivityLogService
    {
        private readonly FolderService _folderService;
        private readonly IActivityLogStorage? _activityLogStorage;
        private readonly CurrentProfileService _currentProfileService;
        private bool _useLocalDb;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private const int MaxEntries = 5000;

        public ActivityLogService(
            FolderService folderService,
            IActivityLogStorage? activityLogStorage = null,
            CurrentProfileService? currentProfileService = null)
        {
            _folderService = folderService;
            _activityLogStorage = activityLogStorage;
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _useLocalDb = _activityLogStorage != null;
        }

        private string LogFilePath
        {
            get
            {
                var root = _folderService.RootPath;
                return string.IsNullOrEmpty(root) ? "" : Path.Combine(root, "activity_log.json");
            }
        }

        public void Log(string actionType, string category, string firmName, string employeeName,
            string description, string oldValue = "", string newValue = "", string details = "",
            string employeeFolder = "", string relatedOperationId = "", string entityType = "",
            string entityId = "", string oldValuesJson = "", string newValuesJson = "", string sessionId = "")
        {
            try
            {
                var path = LogFilePath;
                if (string.IsNullOrEmpty(path)) return;

                var profile = _currentProfileService.CurrentProfile;
                var actorName = _currentProfileService.CurrentActorDisplayName;
                var entry = new ActivityLogEntry
                {
                    ActionType = actionType,
                    Category = category,
                    FirmName = firmName,
                    EmployeeName = employeeName,
                    EmployeeFolder = employeeFolder,
                    Description = description,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Details = details,
                    RelatedOperationId = relatedOperationId,
                    ActorName = actorName,
                    TenantId = profile?.TenantId ?? string.Empty,
                    ActorUserId = _currentProfileService.CurrentBusinessUser?.UserId ?? profile?.Id ?? string.Empty,
                    SessionId = sessionId,
                    MachineId = LicenseService.GetMachineId(),
                    EntityType = entityType,
                    EntityId = entityId,
                    OldValuesJson = oldValuesJson,
                    NewValuesJson = newValuesJson
                };

                if (_useLocalDb && _activityLogStorage != null)
                {
                    _activityLogStorage.InsertActivityLog(entry);
                    return;
                }

                var entries = LoadEntries(path);
                entries.Insert(0, entry);
                if (entries.Count > MaxEntries)
                    entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
                SaveEntries(path, entries);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.Log", ex.Message);
            }
        }

        public List<ActivityLogEntry> GetAll()
        {
            try
            {
                var path = LogFilePath;
                if (string.IsNullOrEmpty(path)) return new();

                if (_useLocalDb && _activityLogStorage != null)
                {
                    var dbEntries = _activityLogStorage.GetAllActivityLogs();
                    return dbEntries;
                }

                return LoadEntries(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.GetAll", ex.Message);
                return new();
            }
        }

        public void RemoveEmployeeEntries(string originalFolder, string deletedFolder, string employeeName, string firmName)
        {
            try
            {
                var path = LogFilePath;
                if (string.IsNullOrEmpty(path))
                    return;

                if (_useLocalDb && _activityLogStorage != null)
                {
                    _activityLogStorage.RemoveActivityLogEntries(originalFolder, deletedFolder, employeeName, firmName);
                    return;
                }

                if (!File.Exists(path))
                    return;

                var entries = LoadEntries(path);
                entries.RemoveAll(entry =>
                    (!string.IsNullOrWhiteSpace(originalFolder) &&
                     string.Equals(entry.EmployeeFolder, originalFolder, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(deletedFolder) &&
                        string.Equals(entry.EmployeeFolder, deletedFolder, StringComparison.OrdinalIgnoreCase))
                    || (string.IsNullOrWhiteSpace(entry.EmployeeFolder)
                        && string.Equals(entry.EmployeeName, employeeName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(entry.FirmName, firmName, StringComparison.OrdinalIgnoreCase)));

                SaveEntries(path, entries);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.RemoveEmployeeEntries", ex.Message);
            }
        }

        public void ClearAll()
        {
            try
            {
                if (_useLocalDb && _activityLogStorage != null)
                {
                    _activityLogStorage.ClearActivityLogs();
                    return;
                }

                var path = LogFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    SafeFileService.DeleteFile(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.ClearAll", ex.Message);
            }
        }

        private static List<ActivityLogEntry> LoadEntries(string path)
        {
            if (!File.Exists(path))
                return new();

            try
            {
                return SafeFileService.ReadJsonOrDefault(path, new List<ActivityLogEntry>(), _jsonOptions, Encoding.UTF8);
            }
            catch (JsonException)
            {
                var tolerantOptions = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true
                };

                var entries = SafeFileService.ReadJson<List<ActivityLogEntry>>(path, tolerantOptions, Encoding.UTF8)
                    ?? new List<ActivityLogEntry>();

                SafeFileService.WriteJsonAtomic(path, entries, _jsonOptions, Encoding.UTF8);
                LoggingService.LogWarning("ActivityLogService.LoadEntries",
                    $"activity_log.json was auto-repaired during load. Entries preserved: {entries.Count}.");
                return entries;
            }
        }

        private static void SaveEntries(string path, List<ActivityLogEntry> entries)
        {
            SafeFileService.WriteJsonAtomic(path, entries, _jsonOptions);
        }
    }
}

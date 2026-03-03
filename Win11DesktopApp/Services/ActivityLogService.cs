using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public class ActivityLogService
    {
        private readonly FolderService _folderService;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private const int MaxEntries = 5000;

        public ActivityLogService(FolderService folderService)
        {
            _folderService = folderService;
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
            string employeeFolder = "")
        {
            try
            {
                var path = LogFilePath;
                if (string.IsNullOrEmpty(path)) return;

                var entries = LoadEntries(path);
                entries.Insert(0, new ActivityLogEntry
                {
                    ActionType = actionType,
                    Category = category,
                    FirmName = firmName,
                    EmployeeName = employeeName,
                    EmployeeFolder = employeeFolder,
                    Description = description,
                    OldValue = oldValue,
                    NewValue = newValue,
                    Details = details
                });

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
                return LoadEntries(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.GetAll", ex.Message);
                return new();
            }
        }

        public void ClearAll()
        {
            try
            {
                var path = LogFilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ActivityLogService.ClearAll", ex.Message);
            }
        }

        private static List<ActivityLogEntry> LoadEntries(string path)
        {
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<ActivityLogEntry>>(json) ?? new();
        }

        private static void SaveEntries(string path, List<ActivityLogEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries, _jsonOptions);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class RecentlyDeletedService
    {
        private const string EmployeesFolderName = "Employees";
        private const string ManifestFileName = "manifest.json";
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly FolderService _folderService;
        private readonly EmployeeService _employeeService;
        private readonly CurrentProfileService _currentProfileService;
        private readonly FinanceService? _financeService;
        private readonly ActivityLogService? _activityLogService;
        private readonly LocalDbService? _localDbService;
        private readonly EmployeeIndexDbService? _employeeIndexDbService;

        public RecentlyDeletedService(
            FolderService folderService,
            EmployeeService employeeService,
            CurrentProfileService? currentProfileService = null,
            FinanceService? financeService = null,
            ActivityLogService? activityLogService = null,
            LocalDbService? localDbService = null,
            EmployeeIndexDbService? employeeIndexDbService = null)
        {
            _folderService = folderService;
            _employeeService = employeeService;
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _financeService = financeService;
            _activityLogService = activityLogService;
            _localDbService = localDbService;
            _employeeIndexDbService = employeeIndexDbService;
        }

        public void EnsureStorage()
        {
            var root = _folderService.RootPath;
            if (string.IsNullOrWhiteSpace(root))
                return;

            _folderService.EnsureRecentlyDeletedFolder();
            Directory.CreateDirectory(GetEmployeesFolderPath());

            var manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath))
                SafeFileService.WriteJsonAtomic(manifestPath, new List<RecentlyDeletedItem>(), JsonOptions, Encoding.UTF8);
        }

        public List<RecentlyDeletedItem> GetAllItems()
        {
            EnsureStorage();

            var manifest = LoadManifest();
            var filtered = manifest
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && Directory.Exists(item.DeletedEmployeeFolder))
                .OrderByDescending(item => item.DeletedAtUtc)
                .ToList();

            if (filtered.Count != manifest.Count)
                SaveManifest(filtered);

            return filtered;
        }

        public RecentlyDeletedItem? FindItem(string? employeeFolder, string? firmName, string? fullName)
        {
            var manifest = GetAllItems();

            if (!string.IsNullOrWhiteSpace(employeeFolder))
            {
                var byFolder = manifest.FirstOrDefault(item =>
                    string.Equals(item.OriginalEmployeeFolder, employeeFolder, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.DeletedEmployeeFolder, employeeFolder, StringComparison.OrdinalIgnoreCase));
                if (byFolder != null)
                    return byFolder;
            }

            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            return manifest.FirstOrDefault(item =>
                string.Equals(item.FullName, fullName, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(firmName) ||
                 string.Equals(item.FirmName, firmName, StringComparison.OrdinalIgnoreCase)));
        }

        public RecentlyDeletedOperationResult MoveEmployeeToRecentlyDeleted(EmployeeSummary employee)
        {
            try
            {
                if (employee == null || string.IsNullOrWhiteSpace(employee.EmployeeFolder))
                    return Fail("Employee folder is missing.");

                if (!Directory.Exists(employee.EmployeeFolder))
                    return Fail("Employee folder was not found.");

                EnsureStorage();

                var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                if (data == null)
                    return Fail("Employee profile could not be loaded.");

                var deletedFolder = BuildDeletedFolderPath(data, employee.EmployeeFolder);
                MoveDirectory(employee.EmployeeFolder, deletedFolder);
                _employeeService.SyncEmployeeIndexForFolder(deletedFolder, employee.FirmName);

                var item = new RecentlyDeletedItem
                {
                    UniqueId = data.UniqueId ?? string.Empty,
                    FullName = string.IsNullOrWhiteSpace(employee.FullName)
                        ? $"{data.FirstName} {data.LastName}".Trim()
                        : employee.FullName,
                    FirmName = employee.FirmName ?? string.Empty,
                    PositionTitle = employee.PositionTitle ?? string.Empty,
                    StartDate = data.StartDate ?? string.Empty,
                    OriginalEmployeeFolder = employee.EmployeeFolder,
                    DeletedEmployeeFolder = deletedFolder,
                    PhotoPath = ResolvePhotoPath(deletedFolder, data),
                    HasPhoto = !string.IsNullOrWhiteSpace(ResolvePhotoPath(deletedFolder, data)),
                    DeletedBy = GetCurrentActorName(),
                    DeletedAtUtc = DateTime.UtcNow,
                    PurgeAfterUtc = DateTime.UtcNow.AddDays(30)
                };

                var manifest = LoadManifest();
                manifest.RemoveAll(existing =>
                    string.Equals(existing.OriginalEmployeeFolder, item.OriginalEmployeeFolder, StringComparison.OrdinalIgnoreCase));
                manifest.Add(item);
                SaveManifest(manifest);

                return new RecentlyDeletedOperationResult
                {
                    Success = true,
                    Item = item
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("RecentlyDeletedService.MoveEmployeeToRecentlyDeleted", ex);
                return Fail(ex.Message);
            }
        }

        public RecentlyDeletedOperationResult RestoreEmployee(string itemId)
        {
            try
            {
                var manifest = LoadManifest();
                var item = manifest.FirstOrDefault(x => string.Equals(x.Id, itemId, StringComparison.Ordinal));
                if (item == null)
                    return Fail("Item was not found.");

                if (!Directory.Exists(item.DeletedEmployeeFolder))
                    return Fail("Deleted employee folder was not found.");

                var restorePath = ResolveRestorePath(item);
                if (string.IsNullOrWhiteSpace(restorePath))
                    return Fail("Restore destination could not be resolved.");

                var parent = Path.GetDirectoryName(restorePath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                MoveDirectory(item.DeletedEmployeeFolder, restorePath);
                _employeeService.SyncEmployeeIndexForFolder(restorePath, item.FirmName);

                manifest.Remove(item);
                SaveManifest(manifest);

                item.OriginalEmployeeFolder = restorePath;
                item.PhotoPath = string.Empty;
                item.HasPhoto = false;

                return new RecentlyDeletedOperationResult
                {
                    Success = true,
                    Item = item
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("RecentlyDeletedService.RestoreEmployee", ex);
                return Fail(ex.Message);
            }
        }

        public async Task<RecentlyDeletedOperationResult> ArchiveEmployeeAsync(string itemId, string endDate)
        {
            try
            {
                var manifest = LoadManifest();
                var item = manifest.FirstOrDefault(x => string.Equals(x.Id, itemId, StringComparison.Ordinal));
                if (item == null)
                    return Fail("Item was not found.");

                if (!Directory.Exists(item.DeletedEmployeeFolder))
                    return Fail("Deleted employee folder was not found.");

                var result = await _employeeService.ArchiveEmployeeFromPathAsync(item.DeletedEmployeeFolder, item.FirmName, endDate);
                if (!result.Success)
                    return Fail("Archive operation failed.");

                manifest.Remove(item);
                SaveManifest(manifest);

                item.PhotoPath = string.Empty;
                item.HasPhoto = false;

                return new RecentlyDeletedOperationResult
                {
                    Success = true,
                    Item = item
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("RecentlyDeletedService.ArchiveEmployeeAsync", ex);
                return Fail(ex.Message);
            }
        }

        public RecentlyDeletedOperationResult DeletePermanently(string itemId)
        {
            try
            {
                var manifest = LoadManifest();
                var item = manifest.FirstOrDefault(x => string.Equals(x.Id, itemId, StringComparison.Ordinal));
                if (item == null)
                    return Fail("Item was not found.");

                _financeService?.RemoveEmployeeReferences(item.OriginalEmployeeFolder, item.DeletedEmployeeFolder, item.UniqueId);
                _activityLogService?.RemoveEmployeeEntries(item.OriginalEmployeeFolder, item.DeletedEmployeeFolder, item.FullName, item.FirmName);
                _localDbService?.DeleteEmployeeHistory(item.UniqueId);
                _employeeIndexDbService?.DeleteEmployeeIndex(item.UniqueId);
                DeleteDirectory(item.DeletedEmployeeFolder);
                manifest.Remove(item);
                SaveManifest(manifest);

                return new RecentlyDeletedOperationResult
                {
                    Success = true,
                    Item = item
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("RecentlyDeletedService.DeletePermanently", ex);
                return Fail(ex.Message);
            }
        }

        public int PurgeExpired()
        {
            try
            {
                var manifest = LoadManifest();
                if (manifest.Count == 0)
                    return 0;

                var now = DateTime.UtcNow;
                var removed = 0;

                foreach (var item in manifest.ToList())
                {
                    var isMissing = !Directory.Exists(item.DeletedEmployeeFolder);
                    var isExpired = item.PurgeAfterUtc <= now;
                    if (!isMissing && !isExpired)
                        continue;

                    if (!isMissing)
                    {
                        _financeService?.RemoveEmployeeReferences(item.OriginalEmployeeFolder, item.DeletedEmployeeFolder, item.UniqueId);
                        _activityLogService?.RemoveEmployeeEntries(item.OriginalEmployeeFolder, item.DeletedEmployeeFolder, item.FullName, item.FirmName);
                        _localDbService?.DeleteEmployeeHistory(item.UniqueId);
                        _employeeIndexDbService?.DeleteEmployeeIndex(item.UniqueId);
                        DeleteDirectory(item.DeletedEmployeeFolder);
                    }

                    manifest.Remove(item);
                    removed++;
                }

                if (removed > 0)
                    SaveManifest(manifest);

                return removed;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("RecentlyDeletedService.PurgeExpired", ex);
                return 0;
            }
        }

        private string GetRootFolderPath()
        {
            return _folderService.GetRecentlyDeletedFolder();
        }

        private string GetEmployeesFolderPath()
        {
            var root = GetRootFolderPath();
            return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, EmployeesFolderName);
        }

        private string GetManifestPath()
        {
            var root = GetRootFolderPath();
            return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, ManifestFileName);
        }

        private List<RecentlyDeletedItem> LoadManifest()
        {
            var manifestPath = GetManifestPath();
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
                return new List<RecentlyDeletedItem>();

            return SafeFileService.ReadJsonOrDefault(manifestPath, new List<RecentlyDeletedItem>(), JsonOptions, Encoding.UTF8);
        }

        private void SaveManifest(List<RecentlyDeletedItem> items)
        {
            var manifestPath = GetManifestPath();
            if (string.IsNullOrWhiteSpace(manifestPath))
                return;

            SafeFileService.WriteJsonAtomic(manifestPath, items, JsonOptions, Encoding.UTF8);
        }

        private string BuildDeletedFolderPath(EmployeeData data, string originalEmployeeFolder)
        {
            var deletedRoot = GetEmployeesFolderPath();
            var baseFolderName = Path.GetFileName(originalEmployeeFolder);
            if (string.IsNullOrWhiteSpace(baseFolderName))
                baseFolderName = FolderService.NormalizeFolderName($"{data.FirstName}_{data.LastName}");

            var uniqueTail = string.IsNullOrWhiteSpace(data.UniqueId)
                ? Guid.NewGuid().ToString("N")[..8]
                : data.UniqueId[..Math.Min(8, data.UniqueId.Length)];
            var preferredName = $"{baseFolderName}__{uniqueTail}";

            var target = Path.Combine(deletedRoot, preferredName);
            var suffix = 1;
            while (Directory.Exists(target))
            {
                target = Path.Combine(deletedRoot, $"{preferredName}_{suffix}");
                suffix++;
            }

            return target;
        }

        private string ResolveRestorePath(RecentlyDeletedItem item)
        {
            var preferred = item.OriginalEmployeeFolder;
            if (!string.IsNullOrWhiteSpace(preferred) && !Directory.Exists(preferred))
                return preferred;

            var firmFolder = _folderService.GetEmployeesFolder(item.FirmName);
            if (string.IsNullOrWhiteSpace(firmFolder))
                return string.Empty;

            var baseName = Path.GetFileName(item.OriginalEmployeeFolder);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = Path.GetFileName(item.DeletedEmployeeFolder);
            if (string.IsNullOrWhiteSpace(baseName))
                return string.Empty;

            var candidate = Path.Combine(firmFolder, baseName);
            if (!Directory.Exists(candidate))
                return candidate;

            for (var i = 1; i < 100; i++)
            {
                var suffixed = Path.Combine(firmFolder, $"{baseName}.{i}");
                if (!Directory.Exists(suffixed))
                    return suffixed;
            }

            return string.Empty;
        }

        private static string ResolvePhotoPath(string employeeFolder, EmployeeData data)
        {
            if (!string.IsNullOrWhiteSpace(data.Files.Photo))
            {
                var jsonPath = Path.Combine(employeeFolder, data.Files.Photo);
                if (File.Exists(jsonPath))
                    return jsonPath;
            }

            var fallback = Path.Combine(employeeFolder, $"{data.FirstName} {data.LastName} - Photo.jpg");
            return File.Exists(fallback) ? fallback : string.Empty;
        }

        private static void MoveDirectory(string source, string destination)
        {
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            try
            {
                RetryHelper.Execute(() => Directory.Move(source, destination));
            }
            catch (IOException)
            {
                CopyDirectory(source, destination);
                EmployeeService.TryCleanupDeferredDirectory(source);
            }
            catch (UnauthorizedAccessException)
            {
                CopyDirectory(source, destination);
                EmployeeService.TryCleanupDeferredDirectory(source);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                SafeFileService.CopyFile(file, destFile);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            RetryHelper.Execute(() =>
            {
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(path, true);
            });
        }

        private string GetCurrentActorName()
        {
            var profile = _currentProfileService.CurrentProfile;
            if (profile == null)
                return string.Empty;

            return $"{profile.FirstName} {profile.LastName}".Trim();
        }

        private static RecentlyDeletedOperationResult Fail(string message)
        {
            return new RecentlyDeletedOperationResult
            {
                Success = false,
                Message = message
            };
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class PendingCleanupItem
    {
        public string Path { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public int Attempts { get; set; }
    }

    public static class PendingCleanupService
    {
        private static readonly SemaphoreSlim _sync = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        private static string StoragePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appData, "AgencyContractor");
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, "pending_cleanup.json");
            }
        }

        public static async Task EnqueueAsync(string targetPath, string reason)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            var fullPath = Path.GetFullPath(targetPath);

            await _sync.WaitAsync();
            try
            {
                var items = await LoadCoreAsync();
                var existing = items.FirstOrDefault(i =>
                    string.Equals(i.Path, fullPath, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    items.Add(new PendingCleanupItem
                    {
                        Path = fullPath,
                        Reason = reason,
                        CreatedAtUtc = DateTime.UtcNow,
                        Attempts = 0
                    });
                }
                else
                {
                    existing.Reason = reason;
                }

                await SaveCoreAsync(items);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PendingCleanupService.Enqueue", ex.Message);
            }
            finally
            {
                _sync.Release();
            }
        }

        public static async Task RemoveAsync(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return;

            var fullPath = Path.GetFullPath(targetPath);

            await _sync.WaitAsync();
            try
            {
                var items = await LoadCoreAsync();
                items.RemoveAll(i => string.Equals(i.Path, fullPath, StringComparison.OrdinalIgnoreCase));
                await SaveCoreAsync(items);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PendingCleanupService.Remove", ex.Message);
            }
            finally
            {
                _sync.Release();
            }
        }

        public static async Task ProcessPendingCleanupAsync(Func<string, bool> cleanupFunc)
        {
            List<PendingCleanupItem> items;

            await _sync.WaitAsync();
            try
            {
                items = await LoadCoreAsync();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PendingCleanupService.Load", ex.Message);
                return;
            }
            finally
            {
                _sync.Release();
            }

            if (items.Count == 0)
                return;

            var remaining = new List<PendingCleanupItem>();
            foreach (var item in items)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(item.Path) || !Directory.Exists(item.Path))
                        continue;

                    var cleaned = cleanupFunc(item.Path);
                    if (!cleaned && Directory.Exists(item.Path))
                    {
                        item.Attempts++;
                        remaining.Add(item);
                        continue;
                    }

                    LoggingService.LogInfo("PendingCleanupService",
                        $"Cleanup completed for '{item.Path}' ({item.Reason}).");
                }
                catch (Exception ex)
                {
                    item.Attempts++;
                    remaining.Add(item);
                    LoggingService.LogWarning("PendingCleanupService.Process",
                        $"Deferred cleanup failed for '{item.Path}': {ex.Message}");
                }
            }

            await _sync.WaitAsync();
            try
            {
                await SaveCoreAsync(remaining);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PendingCleanupService.Save", ex.Message);
            }
            finally
            {
                _sync.Release();
            }
        }

        private static async Task<List<PendingCleanupItem>> LoadCoreAsync()
        {
            var path = StoragePath;
            if (!File.Exists(path))
                return new List<PendingCleanupItem>();

            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<PendingCleanupItem>>(json) ?? new List<PendingCleanupItem>();
        }

        private static async Task SaveCoreAsync(List<PendingCleanupItem> items)
        {
            var path = StoragePath;
            if (items.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var json = JsonSerializer.Serialize(items, _jsonOptions);
            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, path, true);
        }
    }
}

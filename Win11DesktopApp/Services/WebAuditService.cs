using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public sealed class WebAuditService
    {
        private const long MaxAuditFileBytes = 10 * 1024 * 1024; // 10 MB
        private const int RetentionDays = 30;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private readonly FolderService _folderService;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private DateTime _lastCleanupUtc = DateTime.MinValue;

        public WebAuditService(FolderService folderService)
        {
            _folderService = folderService;
        }

        public async Task LogAsync(string action, string? remoteIp, string path, int statusCode)
        {
            try
            {
                var root = _folderService.RootPath;
                if (string.IsNullOrWhiteSpace(root))
                    return;

                var logFolder = Path.Combine(root, "logs");
                Directory.CreateDirectory(logFolder);

                var entry = new
                {
                    timestamp = DateTime.UtcNow,
                    action,
                    remoteIp = remoteIp ?? string.Empty,
                    path,
                    statusCode
                };

                var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
                await _writeLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    CleanupOldAuditFilesIfDue(logFolder);
                    var logPath = ResolveWritableLogPath(logFolder);
                    await File.AppendAllTextAsync(logPath, line, Encoding.UTF8).ConfigureAwait(false);
                }
                finally
                {
                    _writeLock.Release();
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebAuditService", ex.Message);
            }
        }

        private static string ResolveWritableLogPath(string logFolder)
        {
            var dayStamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var activePath = Path.Combine(logFolder, $"web-audit-{dayStamp}.jsonl");

            try
            {
                if (File.Exists(activePath) && new FileInfo(activePath).Length >= MaxAuditFileBytes)
                    RotateActiveLog(activePath, dayStamp);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebAuditService.Rotate", ex.Message);
            }

            return activePath;
        }

        private static void RotateActiveLog(string activePath, string dayStamp)
        {
            var folder = Path.GetDirectoryName(activePath) ?? ".";
            var part = 1;
            string archivedPath;
            do
            {
                archivedPath = Path.Combine(folder, $"web-audit-{dayStamp}.{part}.jsonl");
                part++;
            }
            while (File.Exists(archivedPath));

            SafeFileService.MoveFile(activePath, archivedPath);
        }

        private void CleanupOldAuditFilesIfDue(string logFolder)
        {
            var now = DateTime.UtcNow;
            if (now - _lastCleanupUtc < TimeSpan.FromHours(6))
                return;

            _lastCleanupUtc = now;
            try
            {
                var cutoff = now.AddDays(-RetentionDays);
                foreach (var file in Directory.EnumerateFiles(logFolder, "web-audit-*.jsonl"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            SafeFileService.DeleteFile(file);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("WebAuditService.CleanupFile", $"{file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("WebAuditService.Cleanup", ex.Message);
            }
        }
    }
}

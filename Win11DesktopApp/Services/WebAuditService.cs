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
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private readonly FolderService _folderService;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

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
                var logPath = Path.Combine(logFolder, $"web-audit-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
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
    }
}

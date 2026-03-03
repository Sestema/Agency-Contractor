using System;
using System.IO;

namespace Win11DesktopApp.Services
{
    public static class LoggingService
    {
        private static readonly object _lock = new();
        private static string? _logPath;
        private const int MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

        public static void Initialize(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return;
            _logPath = Path.Combine(rootPath, "error.log");
        }

        public static void LogError(string source, Exception ex)
        {
            Log("ERROR", source, ex.ToString());
        }

        public static void LogError(string source, string message)
        {
            Log("ERROR", source, message);
        }

        public static void LogWarning(string source, string message)
        {
            Log("WARN", source, message);
        }

        public static void LogInfo(string source, string message)
        {
            Log("INFO", source, message);
        }

        private static void Log(string level, string source, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{level}] {source}: {message}");

            if (string.IsNullOrEmpty(_logPath)) return;

            try
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] [{source}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logPath, line);
                }
            }
            catch
            {
                // Last resort — cannot log, just ignore
            }
        }

        private static void RotateIfNeeded()
        {
            if (_logPath == null || !File.Exists(_logPath)) return;
            try
            {
                var info = new FileInfo(_logPath);
                if (info.Length > MaxLogSizeBytes)
                {
                    var backup = _logPath + ".old";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(_logPath, backup);
                }
            }
            catch { }
        }
    }
}

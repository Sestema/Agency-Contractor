using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Win11DesktopApp.Services
{
    public static class LoggingService
    {
        public sealed class LogEntry
        {
            public string Timestamp { get; init; } = string.Empty;
            public string Severity { get; init; } = string.Empty;
            public string Module { get; init; } = string.Empty;
            public string Message { get; init; } = string.Empty;
            public string? Details { get; init; }
        }

        private static readonly object _lock = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
        private static string? _logPath;
        private const int MaxLogSizeBytes = 5 * 1024 * 1024; // 5 MB

        public static void Initialize(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return;
            Directory.CreateDirectory(rootPath);
            _logPath = Path.Combine(rootPath, "error.log");
        }

        public static void LogError(string source, Exception ex)
        {
            Log("ERROR", source, ex.Message, ex.ToString());
        }

        public static void LogError(string source, string message)
        {
            Log("ERROR", source, message, null);
        }

        public static void LogWarning(string source, string message)
        {
            Log("WARN", source, message, null);
        }

        public static void LogInfo(string source, string message)
        {
            Log("INFO", source, message, null);
        }

        public static string GetLogPath()
        {
            return _logPath ?? string.Empty;
        }

        public static string GetRecentLogText(int maxLines = 120)
        {
            try
            {
                return string.Join(Environment.NewLine,
                    GetRecentEntries(maxLines).Select(FormatEntryForDisplay));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadLog error: {ex.Message}");
                return string.Empty;
            }
        }

        public static IReadOnlyList<LogEntry> GetRecentEntries(int maxEntries = 120)
        {
            if (string.IsNullOrWhiteSpace(_logPath) || !File.Exists(_logPath))
                return Array.Empty<LogEntry>();

            try
            {
                lock (_lock)
                {
                    var lines = File.ReadAllLines(_logPath);
                    var entries = new List<LogEntry>(Math.Min(maxEntries, lines.Length));
                    foreach (var line in lines.Reverse())
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (TryParseLogLine(line, out var entry))
                            entries.Add(entry);
                        else
                            entries.Add(new LogEntry
                            {
                                Timestamp = string.Empty,
                                Severity = "INFO",
                                Module = "LegacyLog",
                                Message = line.Trim()
                            });

                        if (entries.Count >= maxEntries)
                            break;
                    }

                    entries.Reverse();
                    return entries;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ReadLog entries error: {ex.Message}");
                return Array.Empty<LogEntry>();
            }
        }

        private static void Log(string level, string source, string message, string? details)
        {
            System.Diagnostics.Debug.WriteLine($"[{level}] {source}: {message}");

            if (string.IsNullOrEmpty(_logPath)) return;

            try
            {
                lock (_lock)
                {
                    RotateIfNeeded();
                    var entry = new LogEntry
                    {
                        Timestamp = DateTime.UtcNow.ToString("o"),
                        Severity = level,
                        Module = source,
                        Message = message,
                        Details = string.IsNullOrWhiteSpace(details) ? null : details
                    };
                    var line = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"LogRotation error: {ex.Message}"); }
        }

        private static bool TryParseLogLine(string line, out LogEntry entry)
        {
            entry = null!;

            if (TryParseJsonLogLine(line, out entry))
                return true;

            if (TryParseLegacyLogLine(line, out entry))
                return true;

            return false;
        }

        private static bool TryParseJsonLogLine(string line, out LogEntry entry)
        {
            try
            {
                entry = JsonSerializer.Deserialize<LogEntry>(line, _jsonOptions)!;
                return entry != null
                    && !string.IsNullOrWhiteSpace(entry.Severity)
                    && !string.IsNullOrWhiteSpace(entry.Module);
            }
            catch
            {
                entry = null!;
                return false;
            }
        }

        private static bool TryParseLegacyLogLine(string line, out LogEntry entry)
        {
            entry = null!;
            if (!line.StartsWith("[", StringComparison.Ordinal))
                return false;

            var firstClose = line.IndexOf(']');
            if (firstClose <= 1 || firstClose + 2 >= line.Length)
                return false;

            var secondOpen = line.IndexOf('[', firstClose + 1);
            var secondClose = secondOpen >= 0 ? line.IndexOf(']', secondOpen + 1) : -1;
            var thirdOpen = secondClose >= 0 ? line.IndexOf('[', secondClose + 1) : -1;
            var thirdClose = thirdOpen >= 0 ? line.IndexOf(']', thirdOpen + 1) : -1;
            if (secondOpen < 0 || secondClose < 0 || thirdOpen < 0 || thirdClose < 0)
                return false;

            entry = new LogEntry
            {
                Timestamp = line[1..firstClose].Trim(),
                Severity = line[(secondOpen + 1)..secondClose].Trim(),
                Module = line[(thirdOpen + 1)..thirdClose].Trim(),
                Message = line[(thirdClose + 1)..].Trim()
            };
            return true;
        }

        private static string FormatEntryForDisplay(LogEntry entry)
        {
            var timestamp = string.IsNullOrWhiteSpace(entry.Timestamp)
                ? "unknown-time"
                : entry.Timestamp;
            var baseLine = $"[{timestamp}] [{entry.Severity}] [{entry.Module}] {entry.Message}";
            if (string.IsNullOrWhiteSpace(entry.Details) || string.Equals(entry.Details, entry.Message, StringComparison.Ordinal))
                return baseLine;

            return $"{baseLine}{Environment.NewLine}{entry.Details}";
        }
    }
}

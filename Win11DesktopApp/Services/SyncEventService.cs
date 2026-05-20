using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services;

public sealed class SyncEventReceivedEventArgs : EventArgs
{
    public SyncEventReceivedEventArgs(SyncEventRecord record)
    {
        Record = record;
    }

    public SyncEventRecord Record { get; }
}

public sealed class SyncEventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeFolder { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string FirmName { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string MachineName { get; set; } = Environment.MachineName;
    public string SourceMachineId { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ChangeScope { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string Details { get; set; } = string.Empty;
}

public sealed class SyncEventService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan CleanupAfter = TimeSpan.FromDays(3);
    private static readonly TimeSpan SalaryPublishMinInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SalaryNotificationMinInterval = TimeSpan.FromSeconds(30);
    /// <summary>Seconds between TCP keepalive probes so dead VPN / sleep states are detected faster.</summary>
    private const int PostgresConnectionKeepAliveSeconds = 30;

    private readonly FolderService _folderService;
    private readonly CurrentProfileService _currentProfileService;
    private readonly AppNotificationService _notificationService;
    private readonly AppSettingsService? _settingsService;
    private readonly AppDataStorageFactory? _storageFactory;
    private readonly HashSet<string> _processedIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastSalaryPublishUtcByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _lastSalaryNotificationUtcByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public event EventHandler<SyncEventReceivedEventArgs>? SyncEventReceived;

    public SyncEventService(
        FolderService folderService,
        CurrentProfileService currentProfileService,
        AppNotificationService notificationService,
        AppSettingsService? settingsService = null,
        AppDataStorageFactory? storageFactory = null)
    {
        _folderService = folderService;
        _currentProfileService = currentProfileService;
        _notificationService = notificationService;
        _settingsService = settingsService;
        _storageFactory = storageFactory;
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_worker != null && !_worker.IsCompleted)
                return;

            _cts = new CancellationTokenSource();
            _worker = Task.Run(() => UsePostgresTransport
                ? PostgresListenLoopAsync(_cts.Token)
                : PollLoopAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
        }
    }

    public void PublishEmployeeCreated(string firmName, string employeeId, string employeeName)
    {
        Publish(new SyncEventRecord
        {
            Type = "EmployeeCreated",
            EmployeeId = employeeId ?? string.Empty,
            EmployeeName = employeeName ?? string.Empty,
            FirmName = firmName ?? string.Empty,
            ActorName = GetCurrentActorName(),
            MachineName = Environment.MachineName,
            SourceMachineId = LicenseService.GetMachineId(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public void PublishSalaryChanged(int year, int month, string firmName)
    {
        if (!ShouldPublishSalaryChanged(year, month, firmName))
            return;

        Publish(new SyncEventRecord
        {
            Type = "SalaryChanged",
            FirmName = firmName ?? string.Empty,
            Year = year,
            Month = month,
            ActorName = GetCurrentActorName(),
            MachineName = Environment.MachineName,
            SourceMachineId = LicenseService.GetMachineId(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public void PublishSalaryEntryChanged(int year, int month, SalaryEntry entry)
    {
        if (entry == null)
            return;

        Publish(new SyncEventRecord
        {
            Type = "SalaryEntryChanged",
            ChangeScope = "Entry",
            EmployeeId = entry.EmployeeId ?? string.Empty,
            EmployeeFolder = entry.EmployeeFolder ?? string.Empty,
            EmployeeName = entry.FullName ?? string.Empty,
            FirmName = entry.FirmName ?? string.Empty,
            Year = year,
            Month = month,
            ActorName = GetCurrentActorName(),
            MachineName = Environment.MachineName,
            SourceMachineId = LicenseService.GetMachineId(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private bool ShouldPublishSalaryChanged(int year, int month, string firmName)
    {
        var key = BuildSalarySyncKey(year, month, firmName);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_lastSalaryPublishUtcByKey.TryGetValue(key, out var last)
                && now - last < SalaryPublishMinInterval)
            {
                return false;
            }

            _lastSalaryPublishUtcByKey[key] = now;
            return true;
        }
    }

    public void PublishCompanyChanged(string operation, string firmName)
    {
        Publish(new SyncEventRecord
        {
            Type = "CompanyChanged",
            FirmName = firmName ?? string.Empty,
            Operation = operation ?? string.Empty,
            ActorName = GetCurrentActorName(),
            MachineName = Environment.MachineName,
            SourceMachineId = LicenseService.GetMachineId(),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    public string SyncEventsFolderPath => _folderService.GetSyncEventsFolder();

    public string GetStatusSummary()
    {
        if (UsePostgresTransport)
            return "Працює через PostgreSQL push. Старі файлові SyncEvents у новому режимі не створюються.";

        try
        {
            var root = _folderService.GetSyncEventsFolder();
            if (string.IsNullOrWhiteSpace(root))
                return "Папка даних не вибрана.";

            var inbox = _folderService.GetSyncEventsInboxFolder();
            var read = _folderService.GetSyncEventsReadFolder();
            Directory.CreateDirectory(inbox);
            Directory.CreateDirectory(read);

            var inboxCount = Directory.EnumerateFiles(inbox, "*.json").Count();
            var readCount = Directory.EnumerateFiles(read, "*.read").Count();
            var last = Directory.EnumerateFiles(inbox, "*.json")
                .Select(File.GetLastWriteTime)
                .DefaultIfEmpty()
                .Max();
            var lastText = last == default ? "ще немає" : last.ToString("dd.MM HH:mm");
            return $"Працює. Подій: {inboxCount}, прочитано цим ПК: {readCount}, остання: {lastText}";
        }
        catch (Exception ex)
        {
            return "Помилка SyncEvents: " + ex.Message;
        }
    }

    public void Publish(SyncEventRecord record)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(record.Id))
                record.Id = Guid.NewGuid().ToString("N");
            if (record.CreatedAtUtc == default)
                record.CreatedAtUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(record.MachineName))
                record.MachineName = Environment.MachineName;
            if (string.IsNullOrWhiteSpace(record.SourceMachineId))
                record.SourceMachineId = LicenseService.GetMachineId();
            if (string.IsNullOrWhiteSpace(record.ActorName))
                record.ActorName = GetCurrentActorName();

            if (UsePostgresTransport)
            {
                PublishPostgres(record);
                return;
            }

            var inbox = _folderService.GetSyncEventsInboxFolder();
            if (string.IsNullOrWhiteSpace(inbox))
                return;

            Directory.CreateDirectory(inbox);
            var safeType = MakeSafeFilePart(record.Type);
            var fileName = $"{record.CreatedAtUtc:yyyyMMdd_HHmmss_fff}_{safeType}_{record.Id}.json";
            var path = Path.Combine(inbox, fileName);
            SafeFileService.WriteJsonAtomic(path, record, JsonOptions, Encoding.UTF8);

            MarkRead(record.Id);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("SyncEventService.Publish", ex.Message);
        }
    }

    private bool UsePostgresTransport => _storageFactory?.IsPostgresRuntimeActiveAtStartup == true;

    private async Task PostgresListenLoopAsync(CancellationToken token)
    {
        var consecutiveFailures = 0;
        while (!token.IsCancellationRequested)
        {
            await using var connection = OpenPostgresConnection();
            void Handler(object? _, NpgsqlNotificationEventArgs args) =>
                HandlePostgresNotification(args.Payload);

            try
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                connection.Notification += Handler;

                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "LISTEN agency_sync_events;";
                    await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                consecutiveFailures = 0;
                LoggingService.LogInfo("SyncEventService",
                    "PostgreSQL push listener connected (LISTEN agency_sync_events).");

                while (!token.IsCancellationRequested)
                    await connection.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                connection.Notification -= Handler;
                return;
            }
            catch (Exception ex)
            {
                try
                {
                    connection.Notification -= Handler;
                }
                catch
                {
                    // ignore unsubscribe errors during broken connection
                }

                consecutiveFailures++;
                var delay = ComputePostgresListenBackoff(consecutiveFailures);
                var kind = RetryHelper.IsTransientPostgres(ex) ? "transient" : "non-transient";
                LoggingService.LogWarning("SyncEventService.PostgresListen",
                    $"{kind} ({ex.GetType().Name}): {ex.Message}. Reconnecting in ~{delay.TotalSeconds:F0}s (attempt {consecutiveFailures}).");

                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
            }
        }
    }

    /// <summary>1s → 2s → … → capped at 60s between LISTEN reconnect attempts.</summary>
    private static TimeSpan ComputePostgresListenBackoff(int failureCount)
    {
        if (failureCount <= 0)
            return TimeSpan.FromSeconds(1);

        var exponent = Math.Min(failureCount - 1, 6);
        var seconds = Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(60, seconds));
    }

    private void PublishPostgres(SyncEventRecord record)
    {
        try
        {
            var payload = JsonSerializer.Serialize(record, JsonOptions);
            RetryHelper.ExecutePostgres(() =>
            {
                using var connection = OpenPostgresConnection();
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT pg_notify('agency_sync_events', @payload);";
                command.Parameters.AddWithValue("payload", payload);
                command.ExecuteNonQuery();
            });
            RememberProcessed(record.Id);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("SyncEventService.PublishPostgres", ex.Message);
        }
    }

    private void HandlePostgresNotification(string payload)
    {
        try
        {
            var record = JsonSerializer.Deserialize<SyncEventRecord>(payload, JsonOptions);
            if (record == null || string.IsNullOrWhiteSpace(record.Id))
                return;

            if (IsOwnEvent(record) || !RememberProcessed(record.Id))
                return;

            HandleEvent(record);
        }
        catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException)
        {
            LoggingService.LogWarning("SyncEventService.PostgresNotification", ex.Message);
        }
    }

    private NpgsqlConnection OpenPostgresConnection()
    {
        var settings = _settingsService?.Settings
            ?? throw new InvalidOperationException("PostgreSQL settings are not available.");
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = string.IsNullOrWhiteSpace(settings.PostgresHost) ? "localhost" : settings.PostgresHost.Trim(),
            Port = settings.PostgresPort <= 0 ? 5432 : settings.PostgresPort,
            Database = string.IsNullOrWhiteSpace(settings.PostgresDatabase) ? "agency_db" : settings.PostgresDatabase.Trim(),
            Username = string.IsNullOrWhiteSpace(settings.PostgresUsername) ? "postgres" : settings.PostgresUsername.Trim(),
            Password = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword),
            Timeout = 10,
            CommandTimeout = 30,
            Pooling = true,
            KeepAlive = PostgresConnectionKeepAliveSeconds
        };

        return new NpgsqlConnection(builder.ConnectionString);
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(InitialDelay, token).ConfigureAwait(false);
            while (!token.IsCancellationRequested)
            {
                ProcessIncomingEvents();
                CleanupOldEvents();
                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LoggingService.LogError("SyncEventService.PollLoop", ex);
        }
    }

    private void ProcessIncomingEvents()
    {
        var inbox = _folderService.GetSyncEventsInboxFolder();
        if (string.IsNullOrWhiteSpace(inbox) || !Directory.Exists(inbox))
            return;

        foreach (var path in Directory.EnumerateFiles(inbox, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            SyncEventRecord? record = null;
            try
            {
                record = SafeFileService.ReadJsonShared<SyncEventRecord>(path, JsonOptions, Encoding.UTF8);
                if (record == null || string.IsNullOrWhiteSpace(record.Id))
                    continue;

                if (IsOwnEvent(record) || HasRead(record.Id) || !RememberProcessed(record.Id))
                {
                    MarkRead(record.Id);
                    continue;
                }

                HandleEvent(record);
                MarkRead(record.Id);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                LoggingService.LogWarning("SyncEventService.ProcessIncomingEvents",
                    $"{Path.GetFileName(path)}: {ex.Message}");
                if (record != null && !string.IsNullOrWhiteSpace(record.Id))
                    MarkRead(record.Id);
            }
        }
    }

    private bool RememberProcessed(string id)
    {
        lock (_processedIds)
        {
            if (_processedIds.Contains(id))
                return false;

            _processedIds.Add(id);
            if (_processedIds.Count > 1000)
                _processedIds.Remove(_processedIds.First());
            return true;
        }
    }

    private void HandleEvent(SyncEventRecord record)
    {
        if (string.Equals(record.Type, "EmployeeCreated", StringComparison.OrdinalIgnoreCase))
        {
            var actor = string.IsNullOrWhiteSpace(record.ActorName) ? record.MachineName : record.ActorName;
            var title = "Новий працівник";
            var message = $"{actor} додав(ла) працівника {record.EmployeeName} у фірму {record.FirmName}.";
            _notificationService.Info(title, message);
        }
        else if (string.Equals(record.Type, "SalaryChanged", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(record.Type, "SalaryEntryChanged", StringComparison.OrdinalIgnoreCase))
        {
            if (ShouldShowSalaryNotification(record))
            {
                var actor = string.IsNullOrWhiteSpace(record.ActorName) ? record.MachineName : record.ActorName;
                var target = string.Equals(record.Type, "SalaryEntryChanged", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(record.EmployeeName)
                        ? $"{record.EmployeeName} ({record.FirmName})"
                        : $"за {record.Month:D2}.{record.Year:D4}";
                _notificationService.Info("Оновлено зарплати", $"{actor} змінив(ла) зарплати: {target}.");
            }
        }
        else if (string.Equals(record.Type, "CompanyChanged", StringComparison.OrdinalIgnoreCase))
        {
            var actor = string.IsNullOrWhiteSpace(record.ActorName) ? record.MachineName : record.ActorName;
            _notificationService.Info(
                "Оновлено фірми",
                $"{actor} змінив(ла) список фірм" + (string.IsNullOrWhiteSpace(record.FirmName) ? "." : $": {record.FirmName}."));
        }

        SyncEventReceived?.Invoke(this, new SyncEventReceivedEventArgs(record));
    }

    private bool ShouldShowSalaryNotification(SyncEventRecord record)
    {
        var key = BuildSalarySyncKey(record.Year, record.Month, record.FirmName);
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            if (_lastSalaryNotificationUtcByKey.TryGetValue(key, out var last)
                && now - last < SalaryNotificationMinInterval)
            {
                return false;
            }

            _lastSalaryNotificationUtcByKey[key] = now;
            return true;
        }
    }

    private static string BuildSalarySyncKey(int year, int month, string? firmName)
    {
        var firm = string.IsNullOrWhiteSpace(firmName) ? "*" : firmName.Trim();
        return $"{year:D4}-{month:D2}|{firm}";
    }

    private bool IsOwnEvent(SyncEventRecord record)
    {
        var machineId = LicenseService.GetMachineId();
        return (!string.IsNullOrWhiteSpace(record.SourceMachineId)
                && string.Equals(record.SourceMachineId, machineId, StringComparison.OrdinalIgnoreCase))
            || string.Equals(record.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasRead(string eventId)
    {
        var path = GetReadMarkerPath(eventId);
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private void MarkRead(string eventId)
    {
        try
        {
            var path = GetReadMarkerPath(eventId);
            if (string.IsNullOrWhiteSpace(path) || File.Exists(path))
                return;

            SafeFileService.WriteTextAtomic(path, DateTime.UtcNow.ToString("O"), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            LoggingService.LogWarning("SyncEventService.MarkRead", ex.Message);
        }
    }

    private string GetReadMarkerPath(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            return string.Empty;

        var folder = _folderService.GetSyncEventsReadFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return string.Empty;

        Directory.CreateDirectory(folder);
        var machine = MakeSafeFilePart(Environment.MachineName);
        return Path.Combine(folder, $"{eventId}_{machine}.read");
    }

    private void CleanupOldEvents()
    {
        try
        {
            var cutoff = DateTime.UtcNow - CleanupAfter;
            DeleteOldFiles(_folderService.GetSyncEventsInboxFolder(), "*.json", cutoff);
            DeleteOldFiles(_folderService.GetSyncEventsReadFolder(), "*.read", cutoff);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("SyncEventService.CleanupOldEvents", ex.Message);
        }
    }

    private static void DeleteOldFiles(string folder, string pattern, DateTime cutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        foreach (var path in Directory.EnumerateFiles(folder, pattern))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                    SafeFileService.DeleteFile(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                LoggingService.LogWarning("SyncEventService.DeleteOldFiles", ex.Message);
            }
        }
    }

    private string GetCurrentActorName()
    {
        var profile = _currentProfileService.CurrentProfile;
        return profile == null ? string.Empty : $"{profile.FirstName} {profile.LastName}".Trim();
    }

    private static string MakeSafeFilePart(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "event" : cleaned;
    }
}

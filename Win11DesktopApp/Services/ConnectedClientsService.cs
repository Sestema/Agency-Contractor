using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class ConnectedClientInfo
    {
        public string ClientId { get; init; } = string.Empty;
        public string MachineName { get; init; } = string.Empty;
        public string WindowsUser { get; init; } = string.Empty;
        public string ProfileName { get; init; } = string.Empty;
        public string IpAddress { get; init; } = string.Empty;
        public string AppVersion { get; init; } = string.Empty;
        public string RootFolderPath { get; init; } = string.Empty;
        public DateTime StartedAtUtc { get; init; }
        public DateTime LastSeenAtUtc { get; init; }
        public DateTime? ClosedAtUtc { get; init; }
    }

    public sealed class ConnectedClientsService : IDisposable
    {
        private readonly AppSettingsService _settingsService;
        private readonly AppDataStorageFactory _storageFactory;
        private readonly CurrentProfileService _currentProfileService;
        private readonly SemaphoreSlim _heartbeatLock = new(1, 1);
        private CancellationTokenSource? _heartbeatCts;
        private Task? _heartbeatTask;
        private bool _isInitialized;
        private bool _isDisposed;

        public ConnectedClientsService(
            AppSettingsService settingsService,
            AppDataStorageFactory storageFactory,
            CurrentProfileService currentProfileService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
            _currentProfileService = currentProfileService ?? throw new ArgumentNullException(nameof(currentProfileService));
        }

        public bool IsAvailable => _storageFactory.IsPostgresRuntimeActiveAtStartup;

        public void Start()
        {
            if (!IsAvailable || _heartbeatTask != null)
                return;

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
        }

        public void Stop()
        {
            if (_heartbeatCts == null)
                return;

            try
            {
                _heartbeatCts.Cancel();
                _heartbeatTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ConnectedClients.Stop", ex.Message);
            }

            try
            {
                MarkClosed();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ConnectedClients.MarkClosed", ex.Message);
            }

            _heartbeatCts.Dispose();
            _heartbeatCts = null;
            _heartbeatTask = null;
        }

        public IReadOnlyList<ConnectedClientInfo> GetClients()
        {
            if (!IsAvailable)
                return Array.Empty<ConnectedClientInfo>();

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT client_id, machine_name, windows_user, profile_name, ip_address, app_version, root_folder_path,
       started_at_utc, last_seen_at_utc, closed_at_utc
FROM app.connected_clients
WHERE last_seen_at_utc >= @oldest
ORDER BY last_seen_at_utc DESC, machine_name ASC;";
            command.Parameters.AddWithValue("oldest", DateTime.UtcNow.AddDays(-7));

            using var reader = command.ExecuteReader();
            var result = new List<ConnectedClientInfo>();
            while (reader.Read())
            {
                result.Add(new ConnectedClientInfo
                {
                    ClientId = reader.GetString(0),
                    MachineName = reader.GetString(1),
                    WindowsUser = reader.GetString(2),
                    ProfileName = reader.GetString(3),
                    IpAddress = reader.GetString(4),
                    AppVersion = reader.GetString(5),
                    RootFolderPath = reader.GetString(6),
                    StartedAtUtc = reader.GetDateTime(7),
                    LastSeenAtUtc = reader.GetDateTime(8),
                    ClosedAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9)
                });
            }

            return result
                .GroupBy(client => BuildClientGroupKey(client.MachineName, client.WindowsUser, client.RootFolderPath))
                .Select(group => group.OrderByDescending(client => client.LastSeenAtUtc).First())
                .ToList();
        }

        public void RefreshNow()
        {
            if (!IsAvailable)
                return;

            UpsertHeartbeat();
        }

        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UpsertHeartbeat();
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("ConnectedClients.Heartbeat", ex.Message);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void UpsertHeartbeat()
        {
            _heartbeatLock.Wait();
            try
            {
                EnsureInitialized();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO app.connected_clients (
    client_id, machine_name, windows_user, profile_name, ip_address, app_version, root_folder_path,
    started_at_utc, last_seen_at_utc, closed_at_utc
) VALUES (
    @clientId, @machineName, @windowsUser, @profileName, @ipAddress, @appVersion, @rootFolderPath,
    @startedAtUtc, @lastSeenAtUtc, NULL
)
ON CONFLICT (client_id) DO UPDATE SET
    machine_name = EXCLUDED.machine_name,
    windows_user = EXCLUDED.windows_user,
    profile_name = EXCLUDED.profile_name,
    ip_address = EXCLUDED.ip_address,
    app_version = EXCLUDED.app_version,
    root_folder_path = EXCLUDED.root_folder_path,
    last_seen_at_utc = EXCLUDED.last_seen_at_utc,
    closed_at_utc = NULL;";
                AddIdentityParameters(command);
                command.Parameters.AddWithValue("startedAtUtc", DateTime.UtcNow);
                command.Parameters.AddWithValue("lastSeenAtUtc", DateTime.UtcNow);
                command.ExecuteNonQuery();
            }
            finally
            {
                _heartbeatLock.Release();
            }
        }

        private void MarkClosed()
        {
            if (!IsAvailable)
                return;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE app.connected_clients
SET last_seen_at_utc = @nowUtc,
    closed_at_utc = @nowUtc
WHERE client_id = @clientId;";
            command.Parameters.AddWithValue("clientId", BuildStableClientId());
            command.Parameters.AddWithValue("nowUtc", DateTime.UtcNow);
            command.ExecuteNonQuery();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE IF NOT EXISTS app.connected_clients (
    client_id TEXT PRIMARY KEY,
    machine_name TEXT NOT NULL,
    windows_user TEXT NOT NULL,
    profile_name TEXT NOT NULL DEFAULT '',
    ip_address TEXT NOT NULL,
    app_version TEXT NOT NULL,
    root_folder_path TEXT NOT NULL,
    started_at_utc TIMESTAMPTZ NOT NULL,
    last_seen_at_utc TIMESTAMPTZ NOT NULL,
    closed_at_utc TIMESTAMPTZ NULL
);

CREATE INDEX IF NOT EXISTS idx_connected_clients_last_seen
ON app.connected_clients(last_seen_at_utc DESC);";
            command.ExecuteNonQuery();
            EnsureColumn(connection, "profile_name", "TEXT NOT NULL DEFAULT ''");
            _isInitialized = true;
        }

        private static void EnsureColumn(NpgsqlConnection connection, string columnName, string definition)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE app.connected_clients ADD COLUMN IF NOT EXISTS {columnName} {definition};";
            command.ExecuteNonQuery();
        }

        private void AddIdentityParameters(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("clientId", BuildStableClientId());
            command.Parameters.AddWithValue("machineName", Environment.MachineName);
            command.Parameters.AddWithValue("windowsUser", Environment.UserName);
            command.Parameters.AddWithValue("profileName", GetCurrentProfileName());
            command.Parameters.AddWithValue("ipAddress", GetLocalNetworkIpAddress());
            command.Parameters.AddWithValue("appVersion", AppSettingsService.CurrentAppVersion);
            command.Parameters.AddWithValue("rootFolderPath", _settingsService.Settings.RootFolderPath ?? string.Empty);
        }

        private string BuildStableClientId()
        {
            var settings = _settingsService.Settings;
            return string.Join("|",
                "v2",
                Environment.MachineName.Trim().ToLowerInvariant(),
                Environment.UserName.Trim().ToLowerInvariant(),
                (settings.PostgresHost ?? string.Empty).Trim().ToLowerInvariant(),
                settings.PostgresPort.ToString(),
                (settings.PostgresDatabase ?? string.Empty).Trim().ToLowerInvariant());
        }

        private static string BuildClientGroupKey(string machineName, string windowsUser, string rootFolderPath)
        {
            return string.Join("|",
                machineName.Trim().ToLowerInvariant(),
                windowsUser.Trim().ToLowerInvariant(),
                rootFolderPath.Trim().ToLowerInvariant());
        }

        private string GetCurrentProfileName()
        {
            var profile = _currentProfileService.CurrentProfile;
            if (profile == null)
                return string.Empty;

            return $"{profile.FirstName} {profile.LastName}".Trim();
        }

        private NpgsqlConnection OpenConnection()
        {
            var settings = _settingsService.Settings;
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(settings.PostgresHost) ? "localhost" : settings.PostgresHost.Trim(),
                Port = settings.PostgresPort <= 0 ? 5432 : settings.PostgresPort,
                Database = string.IsNullOrWhiteSpace(settings.PostgresDatabase) ? "agency_db" : settings.PostgresDatabase.Trim(),
                Username = string.IsNullOrWhiteSpace(settings.PostgresUsername) ? "postgres" : settings.PostgresUsername.Trim(),
                Password = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword),
                Timeout = 10,
                CommandTimeout = 30,
                Pooling = true
            };

            var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private static string GetLocalNetworkIpAddress()
        {
            try
            {
                var candidates = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(item => item.OperationalStatus == OperationalStatus.Up
                        && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address)
                    .Where(address => !IPAddress.IsLoopback(address))
                    .ToList();

                return candidates.FirstOrDefault(IsPrivateIPv4Address)?.ToString()
                    ?? candidates.FirstOrDefault()?.ToString()
                    ?? "127.0.0.1";
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ConnectedClients.GetLocalNetworkIpAddress", ex.Message);
                return "127.0.0.1";
            }
        }

        private static bool IsPrivateIPv4Address(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4
                && (bytes[0] == 10
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Stop();
            _heartbeatLock.Dispose();
            _isDisposed = true;
        }
    }
}

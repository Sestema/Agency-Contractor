using System;
using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public interface ICoreDatabaseStorage
    {
        string DatabasePath { get; }
        bool Exists { get; }
        DatabaseRoot? LoadDatabase();
        void SaveDatabase(DatabaseRoot database);
    }

    public sealed class SqliteCoreDatabaseStorage : ICoreDatabaseStorage
    {
        private readonly CoreDbService _coreDbService;

        public SqliteCoreDatabaseStorage(CoreDbService coreDbService)
        {
            _coreDbService = coreDbService;
        }

        public string DatabasePath => _coreDbService.DatabasePath;

        public bool Exists => _coreDbService.Exists;

        public DatabaseRoot? LoadDatabase()
            => _coreDbService.LoadDatabase();

        public void SaveDatabase(DatabaseRoot database)
            => _coreDbService.SaveDatabase(database);
    }

    public sealed class PostgresCoreDatabaseStorage : ICoreDatabaseStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public PostgresCoreDatabaseStorage(AppSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public string DatabasePath
        {
            get
            {
                var settings = _settingsService.Settings;
                return $"postgresql://{settings.PostgresHost}:{settings.PostgresPort}/{settings.PostgresDatabase}/core.app_database";
            }
        }

        public bool Exists
        {
            get
            {
                try
                {
                    EnsureInitialized();
                    using var connection = OpenConnection();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT COUNT(1) FROM core.app_database WHERE id = 1;";
                    var result = command.ExecuteScalar();
                    return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("PostgresCoreDatabaseStorage.Exists", ex.Message);
                    return false;
                }
            }
        }

        public DatabaseRoot? LoadDatabase()
        {
            EnsureInitialized();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM core.app_database WHERE id = 1;";

            var result = command.ExecuteScalar();
            if (result is not string json || string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<DatabaseRoot>(json);
        }

        public void SaveDatabase(DatabaseRoot database)
        {
            EnsureInitialized();

            var json = JsonSerializer.Serialize(database, JsonOptions);
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO core.app_database (id, version, payload_json, updated_at)
VALUES (1, @version, @payload_json, @updated_at)
ON CONFLICT(id) DO UPDATE SET
    version = EXCLUDED.version,
    payload_json = EXCLUDED.payload_json,
    updated_at = EXCLUDED.updated_at;";
            command.Parameters.AddWithValue("version", database.Version ?? string.Empty);
            command.Parameters.AddWithValue("payload_json", json);
            command.Parameters.AddWithValue("updated_at", now);
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        private void EnsureInitialized()
        {
            if (_isInitialized)
                return;

            lock (_initLock)
            {
                if (_isInitialized)
                    return;

                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE SCHEMA IF NOT EXISTS core;

CREATE TABLE IF NOT EXISTS core.schema_version (
    id INTEGER PRIMARY KEY,
    version INTEGER NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS core.app_database (
    id INTEGER PRIMARY KEY,
    version TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

INSERT INTO core.schema_version (id, version, updated_at)
VALUES (1, 1, @updated_at)
ON CONFLICT(id) DO UPDATE SET
    version = CASE
        WHEN core.schema_version.version < EXCLUDED.version THEN EXCLUDED.version
        ELSE core.schema_version.version
    END,
    updated_at = EXCLUDED.updated_at;";
                command.Parameters.AddWithValue("updated_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();

                _isInitialized = true;
            }
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
    }
}

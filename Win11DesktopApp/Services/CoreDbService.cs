using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Primary SQLite storage for the core application database.
    /// Keeps the existing DatabaseRoot shape stable while moving persistence
    /// away from whole-file JSON writes.
    /// </summary>
    public sealed class CoreDbService
    {
        private const int CurrentSchemaVersion = 1;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public CoreDbService(FolderService folderService)
        {
            _folderService = folderService;
        }

        public string DatabasePath => _folderService.CoreDbPath;
        public bool IsAvailable => !string.IsNullOrWhiteSpace(DatabasePath);
        public bool Exists => IsAvailable && File.Exists(DatabasePath);

        public void EnsureInitialized()
        {
            if (_isInitialized && Exists)
                return;

            lock (_initLock)
            {
                if (_isInitialized && Exists)
                    return;

                if (!IsAvailable)
                    return;

                var sqliteFolder = _folderService.GetSqliteFolder();
                if (string.IsNullOrWhiteSpace(sqliteFolder))
                    return;

                Directory.CreateDirectory(sqliteFolder);

                using var connection = OpenConnection();
                CreateSchema(connection);
                _isInitialized = true;
            }
        }

        public SqliteConnection OpenConnection()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("Core SQLite path is not available.");

            var connection = new SqliteConnection($"Data Source={DatabasePath};Cache=Shared");
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
                command.ExecuteNonQuery();
            }

            return connection;
        }

        public DatabaseRoot? LoadDatabase()
        {
            EnsureInitialized();
            if (!Exists)
                return null;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload_json FROM app_database WHERE id = 1;";

            var result = command.ExecuteScalar();
            if (result is not string json || string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<DatabaseRoot>(json);
        }

        public void SaveDatabase(DatabaseRoot database)
        {
            EnsureInitialized();
            if (!IsAvailable)
                return;

            var json = JsonSerializer.Serialize(database, JsonOptions);
            var now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app_database (id, version, payload_json, updated_at)
VALUES (1, $version, $payload_json, $updated_at)
ON CONFLICT(id) DO UPDATE SET
    version = excluded.version,
    payload_json = excluded.payload_json,
    updated_at = excluded.updated_at;";
            command.Parameters.AddWithValue("$version", database.Version ?? string.Empty);
            command.Parameters.AddWithValue("$payload_json", json);
            command.Parameters.AddWithValue("$updated_at", now);
            command.ExecuteNonQuery();

            transaction.Commit();
        }

        private static void CreateSchema(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS schema_version (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    version INTEGER NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_database (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    version TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

INSERT INTO schema_version (id, version, updated_at)
VALUES (1, $schema_version, $updated_at)
ON CONFLICT(id) DO UPDATE SET
    version = CASE
        WHEN schema_version.version < excluded.version THEN excluded.version
        ELSE schema_version.version
    END,
    updated_at = excluded.updated_at;";
            command.Parameters.AddWithValue("$schema_version", CurrentSchemaVersion);
            command.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }
}

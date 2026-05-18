using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class AppStatisticsSnapshot
    {
        public int TotalEmployeesCreated { get; set; }
        public int GeneratedDocumentsCount { get; set; }
        public int TotalProgramRunMinutes { get; set; }
    }

    public sealed class AppStatisticsService
    {
        private const string StatisticsFileName = "app_statistics.json";
        private const int CurrentSchemaVersion = 1;
        private readonly FolderService _folderService;
        private readonly AppSettingsService? _settingsService;
        private readonly AppDataStorageFactory? _storageFactory;
        private readonly string _machineName;
        private readonly string _userName;
        private readonly string _machineKey;
        private readonly object _lock = new();
        private bool _isInitialized;
        private DateTime? _sessionStartedUtc;

        public AppStatisticsService(
            FolderService folderService,
            AppSettingsService? settingsService = null,
            AppDataStorageFactory? storageFactory = null)
            : this(folderService, Environment.MachineName, Environment.UserName, settingsService, storageFactory)
        {
        }

        internal AppStatisticsService(
            FolderService folderService,
            string machineName,
            string userName,
            AppSettingsService? settingsService = null,
            AppDataStorageFactory? storageFactory = null)
        {
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
            _settingsService = settingsService;
            _storageFactory = storageFactory;
            _machineName = string.IsNullOrWhiteSpace(machineName) ? "unknown-machine" : machineName;
            _userName = string.IsNullOrWhiteSpace(userName) ? "unknown-user" : userName;
            _machineKey = $"{_machineName}|{_userName}";
        }

        public void StartSession()
        {
            lock (_lock)
            {
                _sessionStartedUtc ??= DateTime.UtcNow;
            }
        }

        public void StopSession()
        {
            lock (_lock)
            {
                if (_sessionStartedUtc == null)
                    return;

                var elapsedMinutes = (int)Math.Max(0, Math.Round((DateTime.UtcNow - _sessionStartedUtc.Value).TotalMinutes));
                _sessionStartedUtc = null;
                if (elapsedMinutes <= 0)
                    return;

                var stats = LoadUnlocked();
                stats.TotalProgramRunMinutes += elapsedMinutes;
                SaveUnlocked(stats);
            }
        }

        public void RecordEmployeeCreated()
        {
            Update(stats => stats.TotalEmployeesCreated++);
        }

        public void RecordDocumentGenerated()
        {
            Update(stats => stats.GeneratedDocumentsCount++);
        }

        public AppStatisticsSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                var stats = LoadUnlocked();
                var currentSessionMinutes = _sessionStartedUtc == null
                    ? 0
                    : (int)Math.Max(0, Math.Round((DateTime.UtcNow - _sessionStartedUtc.Value).TotalMinutes));

                return new AppStatisticsSnapshot
                {
                    TotalEmployeesCreated = stats.TotalEmployeesCreated,
                    GeneratedDocumentsCount = stats.GeneratedDocumentsCount,
                    TotalProgramRunMinutes = stats.TotalProgramRunMinutes + currentSessionMinutes
                };
            }
        }

        private void Update(Action<AppStatisticsSnapshot> update)
        {
            lock (_lock)
            {
                var stats = LoadUnlocked();
                update(stats);
                SaveUnlocked(stats);
            }
        }

        private AppStatisticsSnapshot LoadUnlocked()
        {
            if (UsePostgresStorage)
                return LoadPostgresUnlocked();

            EnsureInitializedUnlocked();
            var path = StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new AppStatisticsSnapshot();

            try
            {
                using var connection = OpenConnection(path);
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT total_employees_created, generated_documents_count, total_program_run_minutes
FROM app_statistics
WHERE machine_key = $machine_key;";
                command.Parameters.AddWithValue("$machine_key", _machineKey);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return new AppStatisticsSnapshot();

                return new AppStatisticsSnapshot
                {
                    TotalEmployeesCreated = reader.GetInt32(0),
                    GeneratedDocumentsCount = reader.GetInt32(1),
                    TotalProgramRunMinutes = reader.GetInt32(2)
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.Load", ex.Message);
                return new AppStatisticsSnapshot();
            }
        }

        private void SaveUnlocked(AppStatisticsSnapshot stats)
        {
            if (UsePostgresStorage)
            {
                SavePostgresUnlocked(stats);
                return;
            }

            EnsureInitializedUnlocked();
            var path = StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                using var connection = OpenConnection(path);
                UpsertSnapshot(connection, stats);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.Save", ex.Message);
            }
        }

        private void EnsureInitializedUnlocked()
        {
            if (_isInitialized)
                return;

            if (UsePostgresStorage)
            {
                EnsurePostgresInitializedUnlocked();
                _isInitialized = true;
                return;
            }

            var path = StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(path))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);

            using var connection = OpenConnection(path);
            CreateSchema(connection);
            MigrateMachineSpecificSqliteFiles(connection, path);
            MigrateLegacyJsonIfNeeded(connection);
            _isInitialized = true;
        }

        private SqliteConnection OpenConnection(string path)
        {
            var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            command.ExecuteNonQuery();
            return connection;
        }

        private static void CreateSchema(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS schema_version (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    version INTEGER NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_statistics (
    machine_key TEXT PRIMARY KEY,
    machine_name TEXT NOT NULL,
    user_name TEXT NOT NULL,
    total_employees_created INTEGER NOT NULL DEFAULT 0,
    generated_documents_count INTEGER NOT NULL DEFAULT 0,
    total_program_run_minutes INTEGER NOT NULL DEFAULT 0,
    updated_at_utc TEXT NOT NULL
);

INSERT INTO schema_version (id, version, updated_at_utc)
VALUES (1, $schema_version, $updated_at_utc)
ON CONFLICT(id) DO UPDATE SET
    version = CASE
        WHEN schema_version.version < excluded.version THEN excluded.version
        ELSE schema_version.version
    END,
    updated_at_utc = excluded.updated_at_utc;";
            command.Parameters.AddWithValue("$schema_version", CurrentSchemaVersion);
            command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private void UpsertSnapshot(SqliteConnection connection, AppStatisticsSnapshot stats)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app_statistics (
    machine_key, machine_name, user_name,
    total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
) VALUES (
    $machine_key, $machine_name, $user_name,
    $total_employees_created, $generated_documents_count, $total_program_run_minutes, $updated_at_utc
)
ON CONFLICT(machine_key) DO UPDATE SET
    machine_name = excluded.machine_name,
    user_name = excluded.user_name,
    total_employees_created = excluded.total_employees_created,
    generated_documents_count = excluded.generated_documents_count,
    total_program_run_minutes = excluded.total_program_run_minutes,
    updated_at_utc = excluded.updated_at_utc;";
            command.Parameters.AddWithValue("$machine_key", _machineKey);
            command.Parameters.AddWithValue("$machine_name", _machineName);
            command.Parameters.AddWithValue("$user_name", _userName);
            command.Parameters.AddWithValue("$total_employees_created", stats.TotalEmployeesCreated);
            command.Parameters.AddWithValue("$generated_documents_count", stats.GeneratedDocumentsCount);
            command.Parameters.AddWithValue("$total_program_run_minutes", stats.TotalProgramRunMinutes);
            command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private void MigrateLegacyJsonIfNeeded(SqliteConnection connection)
        {
            var legacyPath = LegacyStatisticsPath;
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
                return;

            try
            {
                var legacy = SafeFileService.ReadJsonOrDefault(legacyPath, new AppStatisticsSnapshot());
                UpsertSnapshot(connection, legacy);

                if (TryVerifySnapshot(connection, legacy))
                {
                    SafeFileService.DeleteFile(legacyPath);
                    LoggingService.LogInfo("AppStatisticsService.Migration", $"Migrated and deleted legacy app statistics JSON: {legacyPath}");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.Migration", ex.Message);
            }
        }

        private void MigrateMachineSpecificSqliteFiles(SqliteConnection targetConnection, string targetPath)
        {
            var statisticsFolder = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(statisticsFolder) || !Directory.Exists(statisticsFolder))
                return;

            foreach (var sourcePath in Directory.EnumerateFiles(statisticsFolder, "app_statistics_*.db"))
            {
                if (PathsEqual(sourcePath, targetPath))
                    continue;

                try
                {
                    var rows = ReadStatisticsRows(sourcePath);
                    if (rows.Count == 0)
                    {
                        SafeFileService.DeleteFile(sourcePath);
                        continue;
                    }

                    foreach (var row in rows)
                        UpsertStatisticsRow(targetConnection, row);

                    if (RowsExist(targetConnection, rows))
                    {
                        SafeFileService.DeleteFile(sourcePath);
                        LoggingService.LogInfo("AppStatisticsService.Migration", $"Migrated and deleted machine-specific app statistics DB: {sourcePath}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("AppStatisticsService.Migration", $"Failed to migrate {sourcePath}: {ex.Message}");
                }
            }
        }

        private static List<StatisticsRow> ReadStatisticsRows(string sourcePath)
        {
            var rows = new List<StatisticsRow>();
            using var sourceConnection = new SqliteConnection($"Data Source={sourcePath};Cache=Shared;Pooling=False");
            sourceConnection.Open();

            using var command = sourceConnection.CreateCommand();
            command.CommandText = @"
SELECT machine_key, machine_name, user_name,
       total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
FROM app_statistics;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new StatisticsRow(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                    reader.IsDBNull(6) ? DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) : reader.GetString(6)));
            }

            return rows;
        }

        private static void UpsertStatisticsRow(SqliteConnection connection, StatisticsRow row)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app_statistics (
    machine_key, machine_name, user_name,
    total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
) VALUES (
    $machine_key, $machine_name, $user_name,
    $total_employees_created, $generated_documents_count, $total_program_run_minutes, $updated_at_utc
)
ON CONFLICT(machine_key) DO UPDATE SET
    machine_name = excluded.machine_name,
    user_name = excluded.user_name,
    total_employees_created = excluded.total_employees_created,
    generated_documents_count = excluded.generated_documents_count,
    total_program_run_minutes = excluded.total_program_run_minutes,
    updated_at_utc = excluded.updated_at_utc;";
            command.Parameters.AddWithValue("$machine_key", row.MachineKey);
            command.Parameters.AddWithValue("$machine_name", row.MachineName);
            command.Parameters.AddWithValue("$user_name", row.UserName);
            command.Parameters.AddWithValue("$total_employees_created", row.TotalEmployeesCreated);
            command.Parameters.AddWithValue("$generated_documents_count", row.GeneratedDocumentsCount);
            command.Parameters.AddWithValue("$total_program_run_minutes", row.TotalProgramRunMinutes);
            command.Parameters.AddWithValue("$updated_at_utc", row.UpdatedAtUtc);
            command.ExecuteNonQuery();
        }

        private static bool RowsExist(SqliteConnection connection, IReadOnlyList<StatisticsRow> rows)
        {
            foreach (var row in rows)
            {
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT total_employees_created, generated_documents_count, total_program_run_minutes
FROM app_statistics
WHERE machine_key = $machine_key;";
                command.Parameters.AddWithValue("$machine_key", row.MachineKey);

                using var reader = command.ExecuteReader();
                if (!reader.Read()
                    || reader.GetInt32(0) != row.TotalEmployeesCreated
                    || reader.GetInt32(1) != row.GeneratedDocumentsCount
                    || reader.GetInt32(2) != row.TotalProgramRunMinutes)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryVerifySnapshot(SqliteConnection connection, AppStatisticsSnapshot expected)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT total_employees_created, generated_documents_count, total_program_run_minutes
FROM app_statistics
WHERE machine_key = $machine_key;";
            command.Parameters.AddWithValue("$machine_key", _machineKey);

            using var reader = command.ExecuteReader();
            return reader.Read()
                && reader.GetInt32(0) == expected.TotalEmployeesCreated
                && reader.GetInt32(1) == expected.GeneratedDocumentsCount
                && reader.GetInt32(2) == expected.TotalProgramRunMinutes;
        }

        private bool UsePostgresStorage => _storageFactory?.IsPostgresRuntimeActiveAtStartup == true;

        private AppStatisticsSnapshot LoadPostgresUnlocked()
        {
            EnsureInitializedUnlocked();

            try
            {
                using var connection = OpenPostgresConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT total_employees_created, generated_documents_count, total_program_run_minutes
FROM app.app_statistics
WHERE machine_key = @machine_key;";
                command.Parameters.AddWithValue("machine_key", _machineKey);

                using var reader = command.ExecuteReader();
                if (!reader.Read())
                    return new AppStatisticsSnapshot();

                return new AppStatisticsSnapshot
                {
                    TotalEmployeesCreated = reader.GetInt32(0),
                    GeneratedDocumentsCount = reader.GetInt32(1),
                    TotalProgramRunMinutes = reader.GetInt32(2)
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.LoadPostgres", ex.Message);
                return new AppStatisticsSnapshot();
            }
        }

        private void SavePostgresUnlocked(AppStatisticsSnapshot stats)
        {
            EnsureInitializedUnlocked();

            try
            {
                using var connection = OpenPostgresConnection();
                UpsertPostgresSnapshot(connection, stats);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.SavePostgres", ex.Message);
            }
        }

        private void EnsurePostgresInitializedUnlocked()
        {
            try
            {
                using var connection = OpenPostgresConnection();
                CreatePostgresSchema(connection);
                MigrateSqliteStatisticsToPostgresIfNeeded(connection);
                MigrateLegacyJsonToPostgresIfNeeded(connection);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.EnsurePostgresInitialized", ex.Message);
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
                Pooling = true
            };

            var connection = new NpgsqlConnection(builder.ConnectionString);
            connection.Open();
            return connection;
        }

        private static void CreatePostgresSchema(NpgsqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE SCHEMA IF NOT EXISTS app;

CREATE TABLE IF NOT EXISTS app.app_statistics (
    machine_key TEXT PRIMARY KEY,
    machine_name TEXT NOT NULL,
    user_name TEXT NOT NULL,
    total_employees_created INTEGER NOT NULL DEFAULT 0,
    generated_documents_count INTEGER NOT NULL DEFAULT 0,
    total_program_run_minutes INTEGER NOT NULL DEFAULT 0,
    updated_at_utc TEXT NOT NULL
);";
            command.ExecuteNonQuery();
        }

        private void UpsertPostgresSnapshot(NpgsqlConnection connection, AppStatisticsSnapshot stats)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app.app_statistics (
    machine_key, machine_name, user_name,
    total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
) VALUES (
    @machine_key, @machine_name, @user_name,
    @total_employees_created, @generated_documents_count, @total_program_run_minutes, @updated_at_utc
)
ON CONFLICT(machine_key) DO UPDATE SET
    machine_name = EXCLUDED.machine_name,
    user_name = EXCLUDED.user_name,
    total_employees_created = EXCLUDED.total_employees_created,
    generated_documents_count = EXCLUDED.generated_documents_count,
    total_program_run_minutes = EXCLUDED.total_program_run_minutes,
    updated_at_utc = EXCLUDED.updated_at_utc;";
            command.Parameters.AddWithValue("machine_key", _machineKey);
            command.Parameters.AddWithValue("machine_name", _machineName);
            command.Parameters.AddWithValue("user_name", _userName);
            command.Parameters.AddWithValue("total_employees_created", stats.TotalEmployeesCreated);
            command.Parameters.AddWithValue("generated_documents_count", stats.GeneratedDocumentsCount);
            command.Parameters.AddWithValue("total_program_run_minutes", stats.TotalProgramRunMinutes);
            command.Parameters.AddWithValue("updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static void UpsertPostgresStatisticsRow(NpgsqlConnection connection, StatisticsRow row)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app.app_statistics (
    machine_key, machine_name, user_name,
    total_employees_created, generated_documents_count, total_program_run_minutes, updated_at_utc
) VALUES (
    @machine_key, @machine_name, @user_name,
    @total_employees_created, @generated_documents_count, @total_program_run_minutes, @updated_at_utc
)
ON CONFLICT(machine_key) DO UPDATE SET
    machine_name = EXCLUDED.machine_name,
    user_name = EXCLUDED.user_name,
    total_employees_created = GREATEST(app.app_statistics.total_employees_created, EXCLUDED.total_employees_created),
    generated_documents_count = GREATEST(app.app_statistics.generated_documents_count, EXCLUDED.generated_documents_count),
    total_program_run_minutes = GREATEST(app.app_statistics.total_program_run_minutes, EXCLUDED.total_program_run_minutes),
    updated_at_utc = EXCLUDED.updated_at_utc;";
            command.Parameters.AddWithValue("machine_key", row.MachineKey);
            command.Parameters.AddWithValue("machine_name", row.MachineName);
            command.Parameters.AddWithValue("user_name", row.UserName);
            command.Parameters.AddWithValue("total_employees_created", row.TotalEmployeesCreated);
            command.Parameters.AddWithValue("generated_documents_count", row.GeneratedDocumentsCount);
            command.Parameters.AddWithValue("total_program_run_minutes", row.TotalProgramRunMinutes);
            command.Parameters.AddWithValue("updated_at_utc", row.UpdatedAtUtc);
            command.ExecuteNonQuery();
        }

        private void MigrateSqliteStatisticsToPostgresIfNeeded(NpgsqlConnection connection)
        {
            var path = StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                foreach (var row in ReadStatisticsRows(path))
                    UpsertPostgresStatisticsRow(connection, row);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.PostgresMigration", ex.Message);
            }
        }

        private void MigrateLegacyJsonToPostgresIfNeeded(NpgsqlConnection connection)
        {
            var legacyPath = LegacyStatisticsPath;
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
                return;

            try
            {
                var legacy = SafeFileService.ReadJsonOrDefault(legacyPath, new AppStatisticsSnapshot());
                UpsertPostgresSnapshot(connection, legacy);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("AppStatisticsService.PostgresJsonMigration", ex.Message);
            }
        }

        internal string StatisticsDbPath
        {
            get
            {
                var sqliteFolder = _folderService.GetSqliteFolder();
                if (string.IsNullOrWhiteSpace(sqliteFolder))
                    return string.Empty;

                return Path.Combine(
                    sqliteFolder,
                    "Statistics",
                    "app_statistics.db");
            }
        }

        private string LegacyStatisticsPath
        {
            get
            {
                var root = _folderService.RootPath;
                return string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, StatisticsFileName);
            }
        }

        private static bool PathsEqual(string left, string right)
            => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

        private sealed record StatisticsRow(
            string MachineKey,
            string MachineName,
            string UserName,
            int TotalEmployeesCreated,
            int GeneratedDocumentsCount,
            int TotalProgramRunMinutes,
            string UpdatedAtUtc);
    }
}

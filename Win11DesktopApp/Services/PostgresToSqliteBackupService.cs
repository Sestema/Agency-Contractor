using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresToSqliteBackupResult
    {
        public bool Success { get; init; }
        public int CoreRecordsCopied { get; init; }
        public int AppRecordsCopied { get; init; }
        public int SalaryEntriesCopied { get; init; }
        public int SalaryExpensesCopied { get; init; }
        public int SalaryDatabasesCreated { get; init; }
        public string BackupFolderPath { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;

        public string ToDisplayMessage()
        {
            if (!Success)
                return $"SQLite backup не створено: {ErrorMessage}";

            return $"SQLite backup готовий. Core: {CoreRecordsCopied}, app.db: {AppRecordsCopied}, зарплати: {SalaryEntriesCopied}, витрати: {SalaryExpensesCopied}, місячних баз: {SalaryDatabasesCreated}. Резервна копія старих SQLite файлів: {BackupFolderPath}";
        }
    }

    public sealed class PostgresToSqliteBackupService
    {
        private readonly AppSettingsService _settingsService;
        private readonly CoreDbService _coreDbService;
        private readonly LocalDbService _localDbService;
        private readonly SalaryDbService _salaryDbService;
        private readonly EmployeeIndexDbService _employeeIndexDbService;
        private readonly AppStatisticsService _appStatisticsService;
        private readonly AppDataStorageFactory _storageFactory;

        public PostgresToSqliteBackupService(
            AppSettingsService settingsService,
            CoreDbService coreDbService,
            LocalDbService localDbService,
            SalaryDbService salaryDbService,
            EmployeeIndexDbService employeeIndexDbService,
            AppStatisticsService appStatisticsService,
            AppDataStorageFactory storageFactory)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _coreDbService = coreDbService ?? throw new ArgumentNullException(nameof(coreDbService));
            _localDbService = localDbService ?? throw new ArgumentNullException(nameof(localDbService));
            _salaryDbService = salaryDbService ?? throw new ArgumentNullException(nameof(salaryDbService));
            _employeeIndexDbService = employeeIndexDbService ?? throw new ArgumentNullException(nameof(employeeIndexDbService));
            _appStatisticsService = appStatisticsService ?? throw new ArgumentNullException(nameof(appStatisticsService));
            _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        }

        public async Task<PostgresToSqliteBackupResult> CreateBackupAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_storageFactory.IsPostgresRuntimeActiveAtStartup)
            {
                return new PostgresToSqliteBackupResult
                {
                    ErrorMessage = "Цей запуск програми не працює через PostgreSQL."
                };
            }

            try
            {
                progress?.Report("Готую резервну копію старих SQLite файлів...");
                var backupFolder = CreateExistingSqliteFilesBackup();

                await using var postgres = OpenPostgresConnection();
                await postgres.OpenAsync(cancellationToken).ConfigureAwait(false);

                progress?.Report("Оновлюю core.db з PostgreSQL...");
                var coreRecords = await ExportCoreAsync(postgres, cancellationToken).ConfigureAwait(false);

                progress?.Report("Оновлюю app.db з PostgreSQL...");
                var appRecords = await ExportAppAsync(postgres, cancellationToken).ConfigureAwait(false);

                progress?.Report("Оновлюю employee_index.db з PostgreSQL...");
                appRecords += await ExportEmployeeIndexAsync(postgres, cancellationToken).ConfigureAwait(false);

                progress?.Report("Оновлюю app_statistics.db з PostgreSQL...");
                appRecords += await ExportAppStatisticsAsync(postgres, cancellationToken).ConfigureAwait(false);

                progress?.Report("Оновлюю місячні SQLite бази зарплат...");
                var salary = await ExportSalaryAsync(postgres, cancellationToken).ConfigureAwait(false);

                return new PostgresToSqliteBackupResult
                {
                    Success = true,
                    CoreRecordsCopied = coreRecords,
                    AppRecordsCopied = appRecords,
                    SalaryEntriesCopied = salary.entries,
                    SalaryExpensesCopied = salary.expenses,
                    SalaryDatabasesCreated = salary.databases,
                    BackupFolderPath = backupFolder
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PostgresToSqliteBackupResult
                {
                    ErrorMessage = "Операцію скасовано."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresToSqliteBackupService.CreateBackupAsync", ex);
                return new PostgresToSqliteBackupResult
                {
                    ErrorMessage = PostgresErrorMessageService.ToUserMessage(ex)
                };
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        private async Task<int> ExportCoreAsync(NpgsqlConnection postgres, CancellationToken cancellationToken)
        {
            _coreDbService.EnsureInitialized();
            using var sqlite = _coreDbService.OpenConnection();
            using var transaction = sqlite.BeginTransaction();
            ClearSqliteTables(sqlite, transaction, "schema_version", "app_database");

            var count = 0;
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "core.schema_version", "schema_version", new[] { "id", "version", "updated_at" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "core.app_database", "app_database", new[] { "id", "version", "payload_json", "updated_at" }, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return count;
        }

        private async Task<int> ExportAppAsync(NpgsqlConnection postgres, CancellationToken cancellationToken)
        {
            _localDbService.EnsureInitialized();
            using var sqlite = _localDbService.OpenConnection();
            using var transaction = sqlite.BeginTransaction();
            ClearSqliteTables(sqlite, transaction,
                "schema_version",
                "migration_journal",
                "activity_log",
                "archive_log",
                "employee_history",
                "salary_history",
                "advances",
                "salary_reports",
                "custom_salary_fields",
                "accommodations");

            var count = 0;
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.schema_version", "schema_version", new[] { "version" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.migration_journal", "migration_journal", new[] { "id", "stage", "status", "records_found", "records_imported", "folders_scanned", "folders_skipped", "started_at", "completed_at", "error_message" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.activity_log", "activity_log", new[] { "id", "timestamp", "action_type", "category", "firm_name", "employee_name", "employee_folder", "description", "old_value", "new_value", "details", "related_operation_id", "actor_name" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.archive_log", "archive_log", new[] { "id", "operation_id", "employee_name", "firm_name", "employee_folder", "action", "date", "timestamp", "is_reverted", "reverted_at", "reverted_by_operation_id" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.employee_history", "employee_history", new[] { "id", "employee_id", "employee_folder", "firm_name", "timestamp", "event_type", "action", "field", "old_value", "new_value", "description", "actor_name" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.salary_history", "salary_history", new[] { "id", "employee_id", "employee_folder", "year", "month", "firm_name", "full_name", "paid_at", "hours_worked", "hourly_rate", "gross_salary", "advance", "net_salary", "note", "custom_values_json", "custom_fields_json" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.advances", "advances", new[] { "id", "employee_id", "employee_folder", "employee_name", "company_id", "date", "amount", "month", "note" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.salary_reports", "salary_reports", new[] { "id", "company_id", "company_name", "year", "month", "notes", "created_at", "updated_at", "entries_json" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.custom_salary_fields", "custom_salary_fields", new[] { "id", "name", "operation", "firm_name", "order_index" }, cancellationToken).ConfigureAwait(false);
            count += await CopyPostgresTableToSqliteAsync(postgres, sqlite, transaction, "app.accommodations", "accommodations", new[] { "id", "employee_folder", "employee_name", "company_id", "year", "month", "amount", "address" }, cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return count;
        }

        private async Task<int> ExportEmployeeIndexAsync(NpgsqlConnection postgres, CancellationToken cancellationToken)
        {
            EnsureEmployeeIndexSqliteSchema();
            using var sqlite = _employeeIndexDbService.OpenConnection();
            using var transaction = sqlite.BeginTransaction();
            ClearSqliteTables(sqlite, transaction, "employee_index");
            var count = await CopyPostgresTableToSqliteAsync(
                postgres,
                sqlite,
                transaction,
                "app.employee_index",
                "employee_index",
                new[]
                {
                    "unique_id", "full_name", "first_name", "last_name", "firm_name", "employee_folder", "employee_type", "status",
                    "start_date", "end_date", "contract_type", "position_title", "position_number", "phone", "email",
                    "passport_number", "visa_number", "insurance_number", "passport_expiry", "visa_expiry", "insurance_expiry",
                    "work_permit_name", "work_permit_expiry", "bank_account_number", "bank_name",
                    "is_archived", "archived_from_firm", "photo_path", "has_photo", "has_passport", "has_visa", "has_insurance", "updated_at"
                },
                cancellationToken).ConfigureAwait(false);
            transaction.Commit();
            return count;
        }

        private async Task<int> ExportAppStatisticsAsync(NpgsqlConnection postgres, CancellationToken cancellationToken)
        {
            EnsureAppStatisticsSqliteSchema();
            using var sqlite = new SqliteConnection($"Data Source={_appStatisticsService.StatisticsDbPath};Cache=Shared;Pooling=False");
            sqlite.Open();
            using var transaction = sqlite.BeginTransaction();
            ClearSqliteTables(sqlite, transaction, "app_statistics");
            var count = await CopyPostgresTableToSqliteAsync(
                postgres,
                sqlite,
                transaction,
                "app.app_statistics",
                "app_statistics",
                new[] { "machine_key", "machine_name", "user_name", "total_employees_created", "generated_documents_count", "total_program_run_minutes", "updated_at_utc" },
                cancellationToken,
                insertOrReplace: true).ConfigureAwait(false);
            transaction.Commit();
            return count;
        }

        private async Task<(int entries, int expenses, int databases)> ExportSalaryAsync(NpgsqlConnection postgres, CancellationToken cancellationToken)
        {
            var months = new List<(int year, int month)>();
            await using (var monthCommand = postgres.CreateCommand())
            {
                monthCommand.CommandText = @"
SELECT source_year, source_month FROM salary.salary_entries
UNION
SELECT source_year, source_month FROM salary.salary_expenses
ORDER BY source_year, source_month;";
                await using var reader = await monthCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    months.Add((reader.GetInt32(0), reader.GetInt32(1)));
            }

            var entries = 0;
            var expenses = 0;
            foreach (var (year, month) in months)
            {
                using var sqlite = _salaryDbService.OpenMonthConnection(year, month);
                using var transaction = sqlite.BeginTransaction();
                ClearSqliteTables(sqlite, transaction, "salary_entries", "salary_expenses");

                entries += await CopyPostgresTableToSqliteAsync(
                    postgres,
                    sqlite,
                    transaction,
                    "salary.salary_entries",
                    "salary_entries",
                    new[] { "id", "firm_name", "year", "month", "employee_id", "employee_folder", "full_name", "hours_worked", "hourly_rate", "advance", "saved_net_salary", "status", "note", "color_tag", "custom_values", "updated_at" },
                    cancellationToken,
                    "WHERE source_year = @sourceYear AND source_month = @sourceMonth",
                    new Dictionary<string, object> { ["sourceYear"] = year, ["sourceMonth"] = month },
                    insertOrReplace: true).ConfigureAwait(false);

                expenses += await CopyPostgresTableToSqliteAsync(
                    postgres,
                    sqlite,
                    transaction,
                    "salary.salary_expenses",
                    "salary_expenses",
                    new[] { "id", "firm_name", "year", "month", "name", "amount" },
                    cancellationToken,
                    "WHERE source_year = @sourceYear AND source_month = @sourceMonth",
                    new Dictionary<string, object> { ["sourceYear"] = year, ["sourceMonth"] = month },
                    insertOrReplace: true).ConfigureAwait(false);

                transaction.Commit();
            }

            return (entries, expenses, months.Count);
        }

        private async Task<int> CopyPostgresTableToSqliteAsync(
            NpgsqlConnection postgres,
            SqliteConnection sqlite,
            SqliteTransaction transaction,
            string sourceTable,
            string targetTable,
            IReadOnlyList<string> columns,
            CancellationToken cancellationToken,
            string whereSql = "",
            IReadOnlyDictionary<string, object>? parameters = null,
            bool insertOrReplace = false)
        {
            await using var select = postgres.CreateCommand();
            select.CommandText = $"SELECT {string.Join(", ", columns.Select(QuotePostgresIdentifier))} FROM {sourceTable} {whereSql};";
            if (parameters != null)
            {
                foreach (var pair in parameters)
                    select.Parameters.AddWithValue(pair.Key, pair.Value);
            }

            await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var copied = 0;
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                using var insert = sqlite.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = BuildSqliteInsertSql(targetTable, columns, insertOrReplace);
                for (var i = 0; i < columns.Count; i++)
                    insert.Parameters.AddWithValue($"@p{i}", reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));
                insert.ExecuteNonQuery();
                copied++;
            }

            return copied;
        }

        private string CreateExistingSqliteFilesBackup()
        {
            var sqliteFolder = Path.GetDirectoryName(_localDbService.DatabasePath);
            if (string.IsNullOrWhiteSpace(sqliteFolder))
                throw new InvalidOperationException("SQLite folder is not available.");

            Directory.CreateDirectory(sqliteFolder);
            var backupFolder = Path.Combine(
                sqliteFolder,
                "Backups",
                $"postgres_to_sqlite_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(backupFolder);

            CopyFileFamily(_coreDbService.DatabasePath, backupFolder);
            CopyFileFamily(_localDbService.DatabasePath, backupFolder);
            CopyFileFamily(_employeeIndexDbService.DatabasePath, backupFolder);
            CopyFileFamily(_appStatisticsService.StatisticsDbPath, backupFolder);

            var salaryFolder = _salaryDbService.SalaryDbFolder;
            if (Directory.Exists(salaryFolder))
            {
                var salaryBackupFolder = Path.Combine(backupFolder, "Vyplaty");
                Directory.CreateDirectory(salaryBackupFolder);
                foreach (var file in Directory.EnumerateFiles(salaryFolder, "salary_*.db*"))
                    File.Copy(file, Path.Combine(salaryBackupFolder, Path.GetFileName(file)), overwrite: true);
            }

            return backupFolder;
        }

        private static void CopyFileFamily(string databasePath, string backupFolder)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                return;

            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            {
                if (File.Exists(path))
                    File.Copy(path, Path.Combine(backupFolder, Path.GetFileName(path)), overwrite: true);
            }
        }

        private void EnsureEmployeeIndexSqliteSchema()
        {
            var databasePath = _employeeIndexDbService.DatabasePath;
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new InvalidOperationException("Employee index SQLite path is not available.");

            var folder = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            using var connection = new SqliteConnection($"Data Source={databasePath};Cache=Shared;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS _meta (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS employee_index (
    unique_id TEXT PRIMARY KEY,
    full_name TEXT,
    first_name TEXT,
    last_name TEXT,
    firm_name TEXT,
    employee_folder TEXT,
    employee_type TEXT,
    status TEXT,
    start_date TEXT,
    end_date TEXT,
    contract_type TEXT,
    position_title TEXT,
    position_number TEXT,
    phone TEXT,
    email TEXT,
    passport_number TEXT,
    visa_number TEXT,
    insurance_number TEXT,
    passport_expiry TEXT,
    visa_expiry TEXT,
    insurance_expiry TEXT,
    work_permit_name TEXT,
    work_permit_expiry TEXT,
    bank_account_number TEXT,
    bank_name TEXT,
    is_archived INTEGER NOT NULL DEFAULT 0,
    archived_from_firm TEXT,
    photo_path TEXT,
    has_photo INTEGER NOT NULL DEFAULT 0,
    has_passport INTEGER NOT NULL DEFAULT 0,
    has_visa INTEGER NOT NULL DEFAULT 0,
    has_insurance INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_ei_firm ON employee_index(firm_name);
CREATE INDEX IF NOT EXISTS idx_ei_archived ON employee_index(is_archived);
CREATE INDEX IF NOT EXISTS idx_ei_folder ON employee_index(employee_folder);
CREATE INDEX IF NOT EXISTS idx_ei_full_name ON employee_index(full_name);

INSERT INTO _meta(version)
SELECT 1
WHERE NOT EXISTS (SELECT 1 FROM _meta);";
            command.ExecuteNonQuery();
        }

        private void EnsureAppStatisticsSqliteSchema()
        {
            var databasePath = _appStatisticsService.StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new InvalidOperationException("App statistics SQLite path is not available.");

            var folder = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            using var connection = new SqliteConnection($"Data Source={databasePath};Cache=Shared;Pooling=False");
            connection.Open();
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
VALUES (1, 1, $updated_at_utc)
ON CONFLICT(id) DO UPDATE SET
    version = CASE
        WHEN schema_version.version < excluded.version THEN excluded.version
        ELSE schema_version.version
    END,
    updated_at_utc = excluded.updated_at_utc;";
            command.Parameters.AddWithValue("$updated_at_utc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        private static void ClearSqliteTables(SqliteConnection connection, SqliteTransaction transaction, params string[] tableNames)
        {
            foreach (var tableName in tableNames)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"DELETE FROM {QuoteSqliteIdentifier(tableName)};";
                command.ExecuteNonQuery();
            }
        }

        private NpgsqlConnection OpenPostgresConnection()
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
                CommandTimeout = 60,
                Pooling = true
            };

            return new NpgsqlConnection(builder.ConnectionString);
        }

        private static string BuildSqliteInsertSql(string targetTable, IReadOnlyList<string> columns, bool insertOrReplace)
        {
            var verb = insertOrReplace ? "INSERT OR REPLACE" : "INSERT";
            var columnSql = string.Join(", ", columns.Select(QuoteSqliteIdentifier));
            var parameterSql = string.Join(", ", columns.Select((_, index) => $"@p{index}"));
            return $"{verb} INTO {QuoteSqliteIdentifier(targetTable)} ({columnSql}) VALUES ({parameterSql});";
        }

        private static string QuoteSqliteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        private static string QuotePostgresIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

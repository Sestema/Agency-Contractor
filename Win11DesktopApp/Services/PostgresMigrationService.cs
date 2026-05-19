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
    public sealed class PostgresMigrationRequest
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 5432;
        public string Database { get; init; } = "agency_db";
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public int TimeoutSeconds { get; init; } = 10;
    }

    public sealed class PostgresMigrationResult
    {
        public bool Success { get; init; }
        public string Database { get; init; } = string.Empty;
        public int CoreRecordsCopied { get; init; }
        public int AppRecordsCopied { get; init; }
        public int SalaryEntriesCopied { get; init; }
        public int SalaryExpensesCopied { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;

        public string ToDisplayMessage()
        {
            if (!Success)
                return $"Міграцію не виконано: {ErrorMessage}";

            return $"Міграція готова. База \"{Database}\" підготовлена. Core: {CoreRecordsCopied}, app.db: {AppRecordsCopied}, зарплати: {SalaryEntriesCopied}, витрати: {SalaryExpensesCopied}.";
        }
    }

    public sealed class PostgresMigrationService
    {
        private static readonly string[] EmployeeIndexColumns =
        {
            "unique_id", "full_name", "first_name", "last_name", "firm_name", "employee_folder", "employee_type", "status",
            "start_date", "end_date", "contract_type", "position_title", "position_number", "phone", "email",
            "passport_number", "visa_number", "insurance_number", "passport_expiry", "visa_expiry", "insurance_expiry",
            "work_permit_name", "work_permit_expiry", "bank_account_number", "bank_name",
            "is_archived", "archived_from_firm", "photo_path", "has_photo", "has_passport", "has_visa", "has_insurance", "updated_at"
        };
        private static readonly string[] AppStatisticsColumns =
        {
            "machine_key", "machine_name", "user_name", "total_employees_created", "generated_documents_count", "total_program_run_minutes", "updated_at_utc"
        };

        private readonly AppSettingsService _settingsService;
        private readonly CoreDbService _coreDbService;
        private readonly LocalDbService _localDbService;
        private readonly SalaryDbService _salaryDbService;
        private readonly EmployeeIndexDbService? _employeeIndexDbService;
        private readonly AppStatisticsService? _appStatisticsService;

        public PostgresMigrationService(
            AppSettingsService settingsService,
            CoreDbService coreDbService,
            LocalDbService localDbService,
            SalaryDbService salaryDbService,
            EmployeeIndexDbService? employeeIndexDbService = null,
            AppStatisticsService? appStatisticsService = null)
        {
            _settingsService = settingsService;
            _coreDbService = coreDbService;
            _localDbService = localDbService;
            _salaryDbService = salaryDbService;
            _employeeIndexDbService = employeeIndexDbService;
            _appStatisticsService = appStatisticsService;
        }

        public async Task<PostgresMigrationResult> MigrateAsync(
            PostgresMigrationRequest request,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var databaseName = NormalizeDatabaseName(request.Database);
            try
            {
                progress?.Report("Перевіряю PostgreSQL і створюю базу, якщо її ще немає...");
                await EnsureDatabaseExistsAsync(request, databaseName, cancellationToken).ConfigureAwait(false);

                await using var postgres = new NpgsqlConnection(BuildConnectionString(request, databaseName));
                await postgres.OpenAsync(cancellationToken).ConfigureAwait(false);

                progress?.Report("Створюю таблиці PostgreSQL...");
                await CreateSchemaAsync(postgres, cancellationToken).ConfigureAwait(false);

                await using var transaction = await postgres.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ClearTargetTablesAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    progress?.Report("Переношу core.db...");
                    var coreRecords = await CopyCoreDatabaseAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    progress?.Report("Переношу app.db...");
                    var appRecords = await CopyLocalDatabaseAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    progress?.Report("Переношу employee_index.db...");
                    appRecords += await CopyEmployeeIndexDatabaseAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    progress?.Report("Переношу app_statistics.db...");
                    appRecords += await CopyAppStatisticsDatabaseAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    progress?.Report("Переношу місячні бази зарплат...");
                    var salaryRecords = await CopySalaryDatabasesAsync(postgres, transaction, cancellationToken).ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                    _settingsService.Settings.PostgresConnectionString = BuildConnectionStringWithoutPassword(request, databaseName);
                    _settingsService.Settings.PostgresHost = string.IsNullOrWhiteSpace(request.Host) ? "localhost" : request.Host.Trim();
                    _settingsService.Settings.PostgresPort = request.Port <= 0 ? 5432 : request.Port;
                    _settingsService.Settings.PostgresDatabase = databaseName;
                    _settingsService.Settings.PostgresUsername = request.Username?.Trim() ?? string.Empty;
                    _settingsService.Settings.EncryptedPostgresPassword = LocalSecretProtection.Protect(request.Password ?? string.Empty);
                    _settingsService.Settings.PostgresMigrationCompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                    _settingsService.SaveSettings();

                    return new PostgresMigrationResult
                    {
                        Success = true,
                        Database = databaseName,
                        CoreRecordsCopied = coreRecords,
                        AppRecordsCopied = appRecords,
                        SalaryEntriesCopied = salaryRecords.entries,
                        SalaryExpensesCopied = salaryRecords.expenses
                    };
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    throw;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PostgresMigrationResult
                {
                    Database = databaseName,
                    ErrorMessage = "Міграцію скасовано."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresMigrationService.MigrateAsync", ex);
                return new PostgresMigrationResult
                {
                    Database = databaseName,
                    ErrorMessage = PostgresErrorMessageService.ToUserMessage(ex)
                };
            }
            finally
            {
                SqliteConnection.ClearAllPools();
            }
        }

        private async Task<int> CopyCoreDatabaseAsync(NpgsqlConnection postgres, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            _coreDbService.EnsureInitialized();
            if (!_coreDbService.Exists)
                return 0;

            using var sqlite = _coreDbService.OpenConnection();
            var count = 0;
            count += await CopyTableAsync(sqlite, postgres, transaction, "schema_version", "core.schema_version", new[] { "id", "version", "updated_at" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "app_database", "core.app_database", new[] { "id", "version", "payload_json", "updated_at" }, cancellationToken).ConfigureAwait(false);
            return count;
        }

        private async Task<int> CopyLocalDatabaseAsync(NpgsqlConnection postgres, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            _localDbService.EnsureInitialized();
            if (!File.Exists(_localDbService.DatabasePath))
                return 0;

            using var sqlite = _localDbService.OpenConnection();
            var count = 0;
            count += await CopyTableAsync(sqlite, postgres, transaction, "schema_version", "app.schema_version", new[] { "version" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "migration_journal", "app.migration_journal", new[] { "id", "stage", "status", "records_found", "records_imported", "folders_scanned", "folders_skipped", "started_at", "completed_at", "error_message" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "activity_log", "app.activity_log", new[] { "id", "timestamp", "action_type", "category", "firm_name", "employee_name", "employee_folder", "description", "old_value", "new_value", "details", "related_operation_id", "actor_name" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "archive_log", "app.archive_log", new[] { "id", "operation_id", "employee_name", "firm_name", "employee_folder", "action", "date", "timestamp", "is_reverted", "reverted_at", "reverted_by_operation_id" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "employee_history", "app.employee_history", new[] { "id", "employee_id", "employee_folder", "firm_name", "timestamp", "event_type", "action", "field", "old_value", "new_value", "description", "actor_name" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "salary_history", "app.salary_history", new[] { "id", "employee_id", "employee_folder", "year", "month", "firm_name", "full_name", "paid_at", "hours_worked", "hourly_rate", "gross_salary", "advance", "net_salary", "note", "custom_values_json", "custom_fields_json" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "advances", "app.advances", new[] { "id", "employee_id", "employee_folder", "employee_name", "company_id", "date", "amount", "month", "note" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "salary_reports", "app.salary_reports", new[] { "id", "company_id", "company_name", "year", "month", "notes", "created_at", "updated_at", "entries_json" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "custom_salary_fields", "app.custom_salary_fields", new[] { "id", "name", "operation", "firm_name", "order_index" }, cancellationToken).ConfigureAwait(false);
            count += await CopyTableAsync(sqlite, postgres, transaction, "accommodations", "app.accommodations", new[] { "id", "employee_folder", "employee_name", "company_id", "year", "month", "amount", "address" }, cancellationToken).ConfigureAwait(false);
            return count;
        }

        private async Task<(int entries, int expenses)> CopySalaryDatabasesAsync(NpgsqlConnection postgres, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            var entries = 0;
            var expenses = 0;

            foreach (var monthDb in _salaryDbService.EnumerateMonthDatabases())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var sqlite = _salaryDbService.OpenMonthConnection(monthDb.year, monthDb.month);
                entries += await CopySalaryTableAsync(sqlite, postgres, transaction, "salary_entries", "salary.salary_entries", monthDb.year, monthDb.month, monthDb.path, new[] { "id", "firm_name", "year", "month", "employee_id", "employee_folder", "full_name", "hours_worked", "hourly_rate", "advance", "saved_net_salary", "status", "note", "color_tag", "custom_values", "updated_at" }, cancellationToken).ConfigureAwait(false);
                expenses += await CopySalaryTableAsync(sqlite, postgres, transaction, "salary_expenses", "salary.salary_expenses", monthDb.year, monthDb.month, monthDb.path, new[] { "id", "firm_name", "year", "month", "name", "amount" }, cancellationToken).ConfigureAwait(false);
            }

            return (entries, expenses);
        }

        private async Task<int> CopyEmployeeIndexDatabaseAsync(NpgsqlConnection postgres, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            if (_employeeIndexDbService == null)
                return 0;

            _employeeIndexDbService.EnsureInitialized();
            if (!_employeeIndexDbService.IsAvailable || !File.Exists(_employeeIndexDbService.DatabasePath))
                return 0;

            using var sqlite = _employeeIndexDbService.OpenConnection();
            return await CopyTableAsync(sqlite, postgres, transaction, "employee_index", "app.employee_index", EmployeeIndexColumns, cancellationToken).ConfigureAwait(false);
        }

        private async Task<int> CopyAppStatisticsDatabaseAsync(NpgsqlConnection postgres, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            if (_appStatisticsService == null)
                return 0;

            var path = _appStatisticsService.StatisticsDbPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            using var sqlite = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False");
            sqlite.Open();
            return await CopyTableAsync(sqlite, postgres, transaction, "app_statistics", "app.app_statistics", AppStatisticsColumns, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<int> CopyTableAsync(
            SqliteConnection sqlite,
            NpgsqlConnection postgres,
            NpgsqlTransaction transaction,
            string sourceTable,
            string targetTable,
            IReadOnlyList<string> columns,
            CancellationToken cancellationToken)
        {
            using var select = sqlite.CreateCommand();
            select.CommandText = $"SELECT {string.Join(", ", columns.Select(QuoteSqliteIdentifier))} FROM {QuoteSqliteIdentifier(sourceTable)};";
            using var reader = select.ExecuteReader();

            var copied = 0;
            while (reader.Read())
            {
                await InsertRowAsync(postgres, transaction, targetTable, columns, reader, cancellationToken).ConfigureAwait(false);
                copied++;
            }

            return copied;
        }

        private static async Task<int> CopySalaryTableAsync(
            SqliteConnection sqlite,
            NpgsqlConnection postgres,
            NpgsqlTransaction transaction,
            string sourceTable,
            string targetTable,
            int sourceYear,
            int sourceMonth,
            string sourcePath,
            IReadOnlyList<string> columns,
            CancellationToken cancellationToken)
        {
            using var select = sqlite.CreateCommand();
            select.CommandText = $"SELECT {string.Join(", ", columns.Select(QuoteSqliteIdentifier))} FROM {QuoteSqliteIdentifier(sourceTable)};";
            using var reader = select.ExecuteReader();

            var targetColumns = new[] { "source_year", "source_month", "source_db_path" }.Concat(columns).ToArray();
            var copied = 0;
            while (reader.Read())
            {
                await using var insert = postgres.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = BuildInsertSql(targetTable, targetColumns);
                insert.Parameters.AddWithValue("p0", sourceYear);
                insert.Parameters.AddWithValue("p1", sourceMonth);
                insert.Parameters.AddWithValue("p2", sourcePath ?? string.Empty);
                for (var i = 0; i < columns.Count; i++)
                    insert.Parameters.AddWithValue($"p{i + 3}", reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));

                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                copied++;
            }

            return copied;
        }

        private static async Task InsertRowAsync(
            NpgsqlConnection postgres,
            NpgsqlTransaction transaction,
            string targetTable,
            IReadOnlyList<string> columns,
            SqliteDataReader reader,
            CancellationToken cancellationToken)
        {
            await using var insert = postgres.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = BuildInsertSql(targetTable, columns);
            for (var i = 0; i < columns.Count; i++)
                insert.Parameters.AddWithValue($"p{i}", reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i));

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string BuildInsertSql(string targetTable, IReadOnlyList<string> columns)
        {
            var columnSql = string.Join(", ", columns.Select(QuoteQualifiedIdentifierPart));
            var parameterSql = string.Join(", ", columns.Select((_, index) => $"@p{index}"));
            return $"INSERT INTO {targetTable} ({columnSql}) VALUES ({parameterSql});";
        }

        private static async Task EnsureDatabaseExistsAsync(PostgresMigrationRequest request, string databaseName, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(BuildConnectionString(request, "postgres"));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM pg_database WHERE datname = @database LIMIT 1;";
                exists.Parameters.AddWithValue("database", databaseName);
                if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) != null)
                    return;
            }

            await using var create = connection.CreateCommand();
            create.CommandText = $"CREATE DATABASE {QuotePostgresIdentifier(databaseName)};";
            await create.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task CreateSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE SCHEMA IF NOT EXISTS core;
CREATE SCHEMA IF NOT EXISTS app;
CREATE SCHEMA IF NOT EXISTS salary;

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

CREATE TABLE IF NOT EXISTS app.schema_version (
    version INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS app.migration_journal (
    id INTEGER PRIMARY KEY,
    stage TEXT NOT NULL,
    status TEXT NOT NULL,
    records_found INTEGER NOT NULL DEFAULT 0,
    records_imported INTEGER NOT NULL DEFAULT 0,
    folders_scanned INTEGER NOT NULL DEFAULT 0,
    folders_skipped INTEGER NOT NULL DEFAULT 0,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    error_message TEXT
);

CREATE TABLE IF NOT EXISTS app.activity_log (
    id TEXT PRIMARY KEY,
    timestamp TEXT NOT NULL,
    action_type TEXT NOT NULL,
    category TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    description TEXT NOT NULL,
    old_value TEXT NOT NULL,
    new_value TEXT NOT NULL,
    details TEXT NOT NULL,
    related_operation_id TEXT NOT NULL,
    actor_name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app.archive_log (
    id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    operation_id TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    action TEXT NOT NULL,
    date TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    is_reverted INTEGER NOT NULL DEFAULT 0,
    reverted_at TEXT,
    reverted_by_operation_id TEXT
);

CREATE TABLE IF NOT EXISTS app.employee_history (
    id INTEGER GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    employee_id TEXT NOT NULL,
    employee_folder TEXT NOT NULL,
    firm_name TEXT,
    timestamp TEXT NOT NULL,
    event_type TEXT NOT NULL,
    action TEXT NOT NULL,
    field TEXT NOT NULL,
    old_value TEXT NOT NULL,
    new_value TEXT NOT NULL,
    description TEXT NOT NULL,
    actor_name TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app.salary_history (
    id TEXT PRIMARY KEY,
    employee_id TEXT,
    employee_folder TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    full_name TEXT NOT NULL,
    paid_at TEXT NOT NULL,
    hours_worked TEXT NOT NULL DEFAULT '0',
    hourly_rate TEXT NOT NULL DEFAULT '0',
    gross_salary TEXT NOT NULL DEFAULT '0',
    advance TEXT NOT NULL DEFAULT '0',
    net_salary TEXT NOT NULL DEFAULT '0',
    note TEXT NOT NULL DEFAULT '',
    custom_values_json TEXT NOT NULL DEFAULT '{}',
    custom_fields_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS app.advances (
    id TEXT PRIMARY KEY,
    employee_id TEXT NOT NULL DEFAULT '',
    employee_folder TEXT NOT NULL,
    employee_name TEXT NOT NULL DEFAULT '',
    company_id TEXT NOT NULL,
    date TEXT NOT NULL,
    amount TEXT NOT NULL DEFAULT '0',
    month TEXT NOT NULL,
    note TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS app.salary_reports (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    company_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    notes TEXT NOT NULL DEFAULT '',
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL,
    entries_json TEXT NOT NULL DEFAULT '[]'
);

CREATE TABLE IF NOT EXISTS app.custom_salary_fields (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    operation INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    order_index INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS app.accommodations (
    id TEXT PRIMARY KEY,
    employee_folder TEXT NOT NULL,
    employee_name TEXT NOT NULL DEFAULT '',
    company_id TEXT NOT NULL DEFAULT '',
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    amount TEXT NOT NULL DEFAULT '0',
    address TEXT NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS app.app_statistics (
    machine_key TEXT PRIMARY KEY,
    machine_name TEXT NOT NULL,
    user_name TEXT NOT NULL,
    total_employees_created INTEGER NOT NULL DEFAULT 0,
    generated_documents_count INTEGER NOT NULL DEFAULT 0,
    total_program_run_minutes INTEGER NOT NULL DEFAULT 0,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app.employee_index (
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

CREATE INDEX IF NOT EXISTS idx_pg_ei_firm ON app.employee_index(firm_name);
CREATE INDEX IF NOT EXISTS idx_pg_ei_archived ON app.employee_index(is_archived);
CREATE INDEX IF NOT EXISTS idx_pg_ei_folder ON app.employee_index(employee_folder);
CREATE INDEX IF NOT EXISTS idx_pg_ei_full_name ON app.employee_index(full_name);

CREATE TABLE IF NOT EXISTS salary.salary_entries (
    source_year INTEGER NOT NULL,
    source_month INTEGER NOT NULL,
    source_db_path TEXT NOT NULL,
    id INTEGER NOT NULL,
    firm_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    employee_id TEXT,
    employee_folder TEXT,
    full_name TEXT,
    hours_worked TEXT NOT NULL DEFAULT '0',
    hourly_rate TEXT NOT NULL DEFAULT '0',
    advance TEXT NOT NULL DEFAULT '0',
    saved_net_salary TEXT NOT NULL DEFAULT '0',
    status TEXT NOT NULL DEFAULT 'pending',
    note TEXT DEFAULT '',
    color_tag TEXT DEFAULT '',
    custom_values TEXT DEFAULT '{}',
    updated_at TEXT,
    PRIMARY KEY (source_year, source_month, id)
);

CREATE TABLE IF NOT EXISTS salary.salary_expenses (
    source_year INTEGER NOT NULL,
    source_month INTEGER NOT NULL,
    source_db_path TEXT NOT NULL,
    id TEXT NOT NULL,
    firm_name TEXT NOT NULL,
    year INTEGER NOT NULL,
    month INTEGER NOT NULL,
    name TEXT DEFAULT '',
    amount TEXT NOT NULL DEFAULT '0',
    PRIMARY KEY (source_year, source_month, id)
);";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task ClearTargetTablesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
TRUNCATE TABLE
    core.schema_version,
    core.app_database,
    app.schema_version,
    app.migration_journal,
    app.activity_log,
    app.archive_log,
    app.employee_history,
    app.salary_history,
    app.advances,
    app.salary_reports,
    app.custom_salary_fields,
    app.accommodations,
    app.app_statistics,
    app.employee_index,
    salary.salary_entries,
    salary.salary_expenses;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string BuildConnectionString(PostgresMigrationRequest request, string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(request.Host) ? "localhost" : request.Host.Trim(),
                Port = request.Port <= 0 ? 5432 : request.Port,
                Database = databaseName,
                Username = request.Username?.Trim() ?? string.Empty,
                Password = request.Password ?? string.Empty,
                Timeout = request.TimeoutSeconds <= 0 ? 10 : request.TimeoutSeconds,
                CommandTimeout = request.TimeoutSeconds <= 0 ? 10 : request.TimeoutSeconds,
                Pooling = true
            };

            return builder.ConnectionString;
        }

        private static string BuildConnectionStringWithoutPassword(PostgresMigrationRequest request, string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(request.Host) ? "localhost" : request.Host.Trim(),
                Port = request.Port <= 0 ? 5432 : request.Port,
                Database = databaseName,
                Username = request.Username?.Trim() ?? string.Empty,
                Timeout = request.TimeoutSeconds <= 0 ? 10 : request.TimeoutSeconds,
                CommandTimeout = request.TimeoutSeconds <= 0 ? 10 : request.TimeoutSeconds,
                Pooling = true
            };

            return builder.ConnectionString;
        }

        private static string NormalizeDatabaseName(string? databaseName)
        {
            var normalized = string.IsNullOrWhiteSpace(databaseName) ? "agency_db" : databaseName.Trim();
            if (normalized.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
                throw new InvalidOperationException("Назва PostgreSQL бази може містити тільки літери, цифри, '_' або '-'.");

            if (string.Equals(normalized, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "template0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "template1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Для програми треба окрема база, наприклад agency_db. Службову базу PostgreSQL використовувати не можна.");
            }

            return normalized;
        }

        private static string QuoteSqliteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        private static string QuoteQualifiedIdentifierPart(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
        private static string QuotePostgresIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresFinanceReportsStorage : IFinanceReportsStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresFinanceReportsStorage(AppSettingsService settingsService, FolderService folderService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        }

        public MonthlySalaryReport? GetSalaryReport(string companyId, int year, int month)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM app.salary_reports
WHERE lower(company_id) = lower(@companyId)
  AND year = @year
  AND month = @month
LIMIT 1;";
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("year", year);
            command.Parameters.AddWithValue("month", month);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadSalaryReport(reader) : null;
        }

        public void UpsertSalaryReport(MonthlySalaryReport report)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertSalaryReport(connection, transaction, report);
            transaction.Commit();
        }

        public List<MonthlySalaryReport> GetSalaryReportsForCompany(string companyId)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM app.salary_reports
WHERE lower(company_id) = lower(@companyId)
ORDER BY year DESC, month DESC, updated_at DESC, id DESC;";
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);

            using var reader = command.ExecuteReader();
            var result = new List<MonthlySalaryReport>();
            while (reader.Read())
                result.Add(ReadSalaryReport(reader));

            return result;
        }

        public List<string> GetAvailableReportMonths(string companyId)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT DISTINCT year, month
FROM app.salary_reports
WHERE lower(company_id) = lower(@companyId)
ORDER BY year DESC, month DESC;";
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);

            using var reader = command.ExecuteReader();
            var result = new List<string>();
            while (reader.Read())
            {
                var resultYear = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var resultMonth = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                result.Add($"{resultYear:D4}-{resultMonth:D2}");
            }

            return result;
        }

        public void RemoveCustomFieldReferencesFromReports(string fieldId)
        {
            if (string.IsNullOrWhiteSpace(fieldId))
                return;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var reports = LoadAllSalaryReports(connection);
            var updatedAt = DateTime.Now;

            foreach (var report in reports)
            {
                var changed = false;
                foreach (var entry in report.Entries)
                {
                    if (entry.CustomValues.Remove(fieldId))
                        changed = true;
                }

                if (!changed)
                    continue;

                report.UpdatedAt = updatedAt;
                UpsertSalaryReport(connection, transaction, report);
            }

            transaction.Commit();
        }

        public void RemoveEmployeeEntriesFromReports(string employeeId, string originalFolder, string deletedFolder)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            var reports = LoadAllSalaryReports(connection);
            var updatedAt = DateTime.Now;

            foreach (var report in reports)
            {
                var removed = report.Entries.RemoveAll(entry => MatchesReportEntry(entry, employeeId, originalFolder, deletedFolder));
                if (removed <= 0)
                    continue;

                report.UpdatedAt = updatedAt;
                UpsertSalaryReport(connection, transaction, report);
            }

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
CREATE SCHEMA IF NOT EXISTS app;

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

CREATE UNIQUE INDEX IF NOT EXISTS idx_pg_salary_reports_company_period ON app.salary_reports(company_id, year, month);
CREATE INDEX IF NOT EXISTS idx_pg_salary_reports_company_updated ON app.salary_reports(company_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_pg_salary_reports_period ON app.salary_reports(year, month);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private void UpsertSalaryReport(NpgsqlConnection connection, NpgsqlTransaction transaction, MonthlySalaryReport report)
        {
            var normalized = CloneReport(report);
            if (string.IsNullOrWhiteSpace(normalized.Id))
                normalized.Id = Guid.NewGuid().ToString();
            if (normalized.CreatedAt == default)
                normalized.CreatedAt = DateTime.Now;
            if (normalized.UpdatedAt == default)
                normalized.UpdatedAt = normalized.CreatedAt;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app.salary_reports (
    id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
) VALUES (
    @id, @companyId, @companyName, @year, @month, @notes, @createdAt, @updatedAt, @entriesJson
)
ON CONFLICT(company_id, year, month) DO UPDATE SET
    id = EXCLUDED.id,
    company_name = EXCLUDED.company_name,
    notes = EXCLUDED.notes,
    updated_at = EXCLUDED.updated_at,
    entries_json = EXCLUDED.entries_json;";
            command.Parameters.AddWithValue("id", normalized.Id);
            command.Parameters.AddWithValue("companyId", normalized.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("companyName", normalized.CompanyName ?? string.Empty);
            command.Parameters.AddWithValue("year", normalized.Year);
            command.Parameters.AddWithValue("month", normalized.Month);
            command.Parameters.AddWithValue("notes", normalized.Notes ?? string.Empty);
            command.Parameters.AddWithValue("createdAt", normalized.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("updatedAt", normalized.UpdatedAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("entriesJson", SerializeReportEntries(normalized.Entries));
            command.ExecuteNonQuery();
        }

        private List<MonthlySalaryReport> LoadAllSalaryReports(NpgsqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, company_id, company_name, year, month, notes, created_at, updated_at, entries_json
FROM app.salary_reports
ORDER BY year DESC, month DESC, updated_at DESC, id DESC;";

            using var reader = command.ExecuteReader();
            var reports = new List<MonthlySalaryReport>();
            while (reader.Read())
                reports.Add(ReadSalaryReport(reader));

            return reports;
        }

        private MonthlySalaryReport ReadSalaryReport(NpgsqlDataReader reader)
        {
            var entriesJson = reader.IsDBNull(8) ? "[]" : reader.GetString(8);
            return new MonthlySalaryReport
            {
                Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                CompanyId = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                CompanyName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Year = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Month = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                Notes = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                CreatedAt = ParseDateTime(reader.IsDBNull(6) ? string.Empty : reader.GetString(6)),
                UpdatedAt = ParseDateTime(reader.IsDBNull(7) ? string.Empty : reader.GetString(7)),
                Entries = DeserializeReportEntries(entriesJson)
            };
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

        private string SerializeReportEntries(IEnumerable<SalaryEntry> entries)
        {
            var clonedEntries = SalaryEntryCloneHelper.CloneEntries(entries ?? Array.Empty<SalaryEntry>());
            foreach (var entry in clonedEntries)
                entry.EmployeeFolder = ToPortablePath(entry.EmployeeFolder);

            return JsonSerializer.Serialize(clonedEntries);
        }

        private List<SalaryEntry> DeserializeReportEntries(string json)
        {
            var entries = JsonSerializer.Deserialize<List<SalaryEntry>>(json) ?? new List<SalaryEntry>();
            foreach (var entry in entries)
                entry.EmployeeFolder = ResolveStoredPath(entry.EmployeeFolder);

            return entries;
        }

        private bool MatchesReportEntry(SalaryEntry entry, string employeeId, string originalFolder, string deletedFolder)
        {
            if (!string.IsNullOrWhiteSpace(employeeId)
                && string.Equals(entry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedEntryFolder = NormalizeFullPath(entry.EmployeeFolder);
            var normalizedOriginalFolder = NormalizeFullPath(originalFolder);
            var normalizedDeletedFolder = NormalizeFullPath(deletedFolder);

            return (!string.IsNullOrWhiteSpace(normalizedOriginalFolder)
                    && string.Equals(normalizedEntryFolder, normalizedOriginalFolder, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(normalizedDeletedFolder)
                    && string.Equals(normalizedEntryFolder, normalizedDeletedFolder, StringComparison.OrdinalIgnoreCase));
        }

        private string ToPortablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalizedPath = NormalizeFullPath(path);
            var rootPath = NormalizeFullPath(_folderService.RootPath);
            if (string.IsNullOrWhiteSpace(rootPath))
                return normalizedPath;

            if (!Path.IsPathRooted(normalizedPath))
                return normalizedPath;

            return IsPathUnderRoot(normalizedPath, rootPath)
                ? Path.GetRelativePath(rootPath, normalizedPath)
                : normalizedPath;
        }

        private string ResolveStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            if (Path.IsPathRooted(path))
                return NormalizeFullPath(path);

            var rootPath = NormalizeFullPath(_folderService.RootPath);
            if (string.IsNullOrWhiteSpace(rootPath))
                return path;

            return NormalizeFullPath(Path.Combine(rootPath, path));
        }

        private static bool IsPathUnderRoot(string path, string rootPath)
        {
            try
            {
                var normalizedPath = EnsureTrailingSeparator(NormalizeFullPath(path));
                var normalizedRoot = EnsureTrailingSeparator(NormalizeFullPath(rootPath));
                return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path;
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.EndsWith(Path.DirectorySeparatorChar)
                || path.EndsWith(Path.AltDirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static DateTime ParseDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DateTime.Now;

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.Now;
        }

        private static MonthlySalaryReport CloneReport(MonthlySalaryReport report)
        {
            return new MonthlySalaryReport
            {
                Id = report.Id,
                Year = report.Year,
                Month = report.Month,
                CompanyId = report.CompanyId,
                CompanyName = report.CompanyName,
                Entries = SalaryEntryCloneHelper.CloneEntries(report.Entries ?? new List<SalaryEntry>()),
                CreatedAt = report.CreatedAt,
                UpdatedAt = report.UpdatedAt,
                Notes = report.Notes
            };
        }
    }
}

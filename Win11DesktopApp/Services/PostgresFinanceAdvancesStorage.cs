using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Npgsql;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresFinanceAdvancesStorage : IFinanceAdvancesStorage
    {
        private readonly AppSettingsService _settingsService;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresFinanceAdvancesStorage(AppSettingsService settingsService, FolderService folderService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        }

        public void InsertAdvance(string employeeId, string employeeFolder, AdvancePayment advance)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            UpsertAdvance(connection, transaction, employeeId, employeeFolder, advance);
            transaction.Commit();
        }

        public void DeleteAdvance(string advanceId)
        {
            if (string.IsNullOrWhiteSpace(advanceId))
                return;

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app.advances WHERE id = @id;";
            command.Parameters.AddWithValue("id", advanceId);
            command.ExecuteNonQuery();
        }

        public void DeleteAdvancesForEmployee(string employeeId, string originalFolder, string deletedFolder)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
DELETE FROM app.advances
WHERE (@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
   OR (@originalFolder <> '' AND lower(employee_folder) = lower(@originalFolder))
   OR (@deletedFolder <> '' AND lower(employee_folder) = lower(@deletedFolder));";
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("originalFolder", ToPortablePath(originalFolder));
            command.Parameters.AddWithValue("deletedFolder", ToPortablePath(deletedFolder));
            command.ExecuteNonQuery();
        }

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM app.advances
WHERE lower(company_id) = lower(@companyId)
  AND month = @monthKey
ORDER BY date, id;";
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);
            return ReadAdvances(command);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string companyId, string monthKey)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COALESCE(SUM(CAST(amount AS NUMERIC)), 0)
FROM app.advances
WHERE lower(company_id) = lower(@companyId)
  AND month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeId, string employeeFolder, string monthKey)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT COALESCE(SUM(CAST(amount AS NUMERIC)), 0)
FROM app.advances
WHERE month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder));";
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            return Convert.ToDecimal(command.ExecuteScalar() ?? 0m, CultureInfo.InvariantCulture);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeId, string employeeFolder, string monthKey)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM app.advances
WHERE month = @monthKey
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY date, id;";
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeId, string employeeFolder, string firmName, string companyId, string monthKey)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM app.advances
WHERE month = @monthKey
  AND (lower(company_id) = lower(@firmName) OR company_id = @companyId)
  AND ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY date, id;";
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);
            command.Parameters.AddWithValue("firmName", firmName ?? string.Empty);
            command.Parameters.AddWithValue("companyId", companyId ?? string.Empty);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
        }

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey,
            string companyId)
        {
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (requests.Count == 0 || string.IsNullOrWhiteSpace(monthKey))
                return result;

            var requestsByEmployeeId = new Dictionary<string, List<(string requestKey, string firmName)>>(StringComparer.OrdinalIgnoreCase);
            var requestsByFolder = new Dictionary<string, List<(string requestKey, string firmName, bool hasEmployeeId)>>(StringComparer.OrdinalIgnoreCase);

            foreach (var request in requests)
            {
                result[request.requestKey] = 0m;

                var folderKey = ToPortablePath(request.employeeFolder);
                if (!requestsByFolder.TryGetValue(folderKey, out var folderRequests))
                {
                    folderRequests = new List<(string requestKey, string firmName, bool hasEmployeeId)>();
                    requestsByFolder[folderKey] = folderRequests;
                }

                var hasEmployeeId = !string.IsNullOrWhiteSpace(request.employeeId);
                folderRequests.Add((request.requestKey, request.firmName, hasEmployeeId));

                if (!hasEmployeeId)
                    continue;

                if (!requestsByEmployeeId.TryGetValue(request.employeeId, out var employeeRequests))
                {
                    employeeRequests = new List<(string requestKey, string firmName)>();
                    requestsByEmployeeId[request.employeeId] = employeeRequests;
                }

                employeeRequests.Add((request.requestKey, request.firmName));
            }

            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT employee_id, employee_folder, company_id, amount
FROM app.advances
WHERE month = @monthKey;";
            command.Parameters.AddWithValue("monthKey", monthKey ?? string.Empty);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var rowEmployeeId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var rowEmployeeFolder = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var rowCompanyId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                var rowAmount = reader.IsDBNull(3) ? 0m : ParseDecimal(reader.GetString(3));
                var matchedRequestKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!string.IsNullOrWhiteSpace(rowEmployeeId)
                    && requestsByEmployeeId.TryGetValue(rowEmployeeId, out var employeeMatches))
                {
                    foreach (var employeeMatch in employeeMatches)
                    {
                        if (rowCompanyId == companyId || string.Equals(rowCompanyId, employeeMatch.firmName, StringComparison.Ordinal))
                            matchedRequestKeys.Add(employeeMatch.requestKey);
                    }
                }

                if (requestsByFolder.TryGetValue(rowEmployeeFolder, out var folderMatches))
                {
                    foreach (var folderMatch in folderMatches)
                    {
                        if ((string.IsNullOrWhiteSpace(rowEmployeeId) || !folderMatch.hasEmployeeId)
                            && (rowCompanyId == companyId || string.Equals(rowCompanyId, folderMatch.firmName, StringComparison.Ordinal)))
                        {
                            matchedRequestKeys.Add(folderMatch.requestKey);
                        }
                    }
                }

                foreach (var requestKey in matchedRequestKeys)
                    result[requestKey] += rowAmount;
            }

            return result;
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeId, string employeeFolder)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
FROM app.advances
WHERE ((@employeeId <> '' AND lower(employee_id) = lower(@employeeId))
    OR lower(employee_folder) = lower(@employeeFolder))
ORDER BY date DESC, id DESC;";
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            return ReadAdvances(command);
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

CREATE INDEX IF NOT EXISTS idx_pg_adv_month ON app.advances(month);
CREATE INDEX IF NOT EXISTS idx_pg_adv_employee_folder ON app.advances(employee_folder, month);
CREATE INDEX IF NOT EXISTS idx_pg_adv_employee_id ON app.advances(employee_id, month);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        private void UpsertAdvance(NpgsqlConnection connection, NpgsqlTransaction transaction, string employeeId, string employeeFolder, AdvancePayment advance)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO app.advances (
    id, employee_id, employee_folder, employee_name, company_id, date, amount, month, note
) VALUES (
    @id, @employeeId, @employeeFolder, @employeeName, @companyId, @date, @amount, @month, @note
)
ON CONFLICT(id) DO UPDATE SET
    employee_id = EXCLUDED.employee_id,
    employee_folder = EXCLUDED.employee_folder,
    employee_name = EXCLUDED.employee_name,
    company_id = EXCLUDED.company_id,
    date = EXCLUDED.date,
    amount = EXCLUDED.amount,
    month = EXCLUDED.month,
    note = EXCLUDED.note;";

            command.Parameters.AddWithValue("id", string.IsNullOrWhiteSpace(advance.Id) ? Guid.NewGuid().ToString() : advance.Id);
            command.Parameters.AddWithValue("employeeId", employeeId ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(employeeFolder));
            command.Parameters.AddWithValue("employeeName", advance.EmployeeName ?? string.Empty);
            command.Parameters.AddWithValue("companyId", advance.CompanyId ?? string.Empty);
            command.Parameters.AddWithValue("date", advance.Date.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("amount", ToInvariant(advance.Amount));
            command.Parameters.AddWithValue("month", advance.Month ?? string.Empty);
            command.Parameters.AddWithValue("note", advance.Note ?? string.Empty);
            command.ExecuteNonQuery();
        }

        private List<AdvancePayment> ReadAdvances(NpgsqlCommand command)
        {
            var advances = new List<AdvancePayment>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                advances.Add(new AdvancePayment
                {
                    Id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    EmployeeFolder = ResolveStoredPath(reader.IsDBNull(2) ? string.Empty : reader.GetString(2)),
                    EmployeeName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    CompanyId = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    Date = reader.IsDBNull(5)
                        ? DateTime.Now
                        : (DateTime.TryParse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate)
                            ? parsedDate
                            : DateTime.Now),
                    Amount = reader.IsDBNull(6) ? 0m : ParseDecimal(reader.GetString(6)),
                    Month = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    Note = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
                });
            }

            return advances;
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
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string ToInvariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0m;

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            LoggingService.LogWarning("PostgresFinanceAdvancesStorage.ParseDecimal", $"Failed to parse decimal value '{value}'. Using 0.");
            return 0m;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Npgsql;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresEmployeeIndexStorage
    {
        private const string SelectColumns = @"
unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
start_date, end_date, contract_type, position_title, position_number, phone, email,
passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
work_permit_name, work_permit_expiry, bank_account_number, bank_name,
is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at";

        private readonly AppSettingsService _settingsService;
        private readonly FolderService _folderService;
        private readonly object _initLock = new();
        private bool _isInitialized;

        public PostgresEmployeeIndexStorage(AppSettingsService settingsService, FolderService folderService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _folderService = folderService ?? throw new ArgumentNullException(nameof(folderService));
        }

        public void EnsureInitialized()
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
CREATE INDEX IF NOT EXISTS idx_pg_ei_full_name ON app.employee_index(full_name);";
                command.ExecuteNonQuery();
                _isInitialized = true;
            }
        }

        public int GetEmployeeIndexCount()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM app.employee_index;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        public bool HasLegacyAbsolutePaths()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT employee_folder, photo_path
FROM app.employee_index
WHERE COALESCE(employee_folder, '') <> ''
   OR COALESCE(photo_path, '') <> '';";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var employeeFolder = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var photoPath = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                if (IsLegacyAbsolutePath(employeeFolder) || IsLegacyAbsolutePath(photoPath))
                    return true;
            }

            return false;
        }

        public void UpsertEmployeeIndex(EmployeeIndexRow row)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(row?.UniqueId))
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
INSERT INTO app.employee_index (
    unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
    start_date, end_date, contract_type, position_title, position_number, phone, email,
    passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
    work_permit_name, work_permit_expiry, bank_account_number, bank_name,
    is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
) VALUES (
    @uniqueId, @fullName, @firstName, @lastName, @firmName, @employeeFolder, @employeeType, @status,
    @startDate, @endDate, @contractType, @positionTitle, @positionNumber, @phone, @email,
    @passportNumber, @visaNumber, @insuranceNumber, @passportExpiry, @visaExpiry, @insuranceExpiry,
    @workPermitName, @workPermitExpiry, @bankAccountNumber, @bankName,
    @isArchived, @archivedFromFirm, @photoPath, @hasPhoto, @hasPassport, @hasVisa, @hasInsurance, @updatedAt
)
ON CONFLICT (unique_id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    firm_name = EXCLUDED.firm_name,
    employee_folder = EXCLUDED.employee_folder,
    employee_type = EXCLUDED.employee_type,
    status = EXCLUDED.status,
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    contract_type = EXCLUDED.contract_type,
    position_title = EXCLUDED.position_title,
    position_number = EXCLUDED.position_number,
    phone = EXCLUDED.phone,
    email = EXCLUDED.email,
    passport_number = EXCLUDED.passport_number,
    visa_number = EXCLUDED.visa_number,
    insurance_number = EXCLUDED.insurance_number,
    passport_expiry = EXCLUDED.passport_expiry,
    visa_expiry = EXCLUDED.visa_expiry,
    insurance_expiry = EXCLUDED.insurance_expiry,
    work_permit_name = EXCLUDED.work_permit_name,
    work_permit_expiry = EXCLUDED.work_permit_expiry,
    bank_account_number = EXCLUDED.bank_account_number,
    bank_name = EXCLUDED.bank_name,
    is_archived = EXCLUDED.is_archived,
    archived_from_firm = EXCLUDED.archived_from_firm,
    photo_path = EXCLUDED.photo_path,
    has_photo = EXCLUDED.has_photo,
    has_passport = EXCLUDED.has_passport,
    has_visa = EXCLUDED.has_visa,
    has_insurance = EXCLUDED.has_insurance,
    updated_at = EXCLUDED.updated_at;";
            AddRowParameters(command, row);
            command.ExecuteNonQuery();
        }

        public void DeleteEmployeeIndex(string uniqueId)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(uniqueId))
                return;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM app.employee_index WHERE unique_id = @uniqueId;";
            command.Parameters.AddWithValue("uniqueId", uniqueId);
            command.ExecuteNonQuery();
        }

        public List<EmployeeIndexRow> GetEmployeesForFirmRows(string firmName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(firmName))
                return new List<EmployeeIndexRow>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT {SelectColumns}
FROM app.employee_index
WHERE lower(firm_name) = lower(@firmName)
  AND is_archived = 0
ORDER BY full_name, start_date;";
            command.Parameters.AddWithValue("firmName", firmName);
            return ReadRows(command);
        }

        public List<EmployeeIndexRow> GetArchivedEmployeeRows()
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT {SelectColumns}
FROM app.employee_index
WHERE is_archived = 1
ORDER BY full_name, start_date;";
            return ReadRows(command);
        }

        public EmployeeIndexRow? GetEmployeeRowByFolder(string employeeFolder)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            var portableFolder = ToPortablePath(employeeFolder);
            var absoluteFolder = NormalizeFullPath(employeeFolder);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $@"
SELECT {SelectColumns}
FROM app.employee_index
WHERE lower(employee_folder) = lower(@portableFolder)
   OR lower(employee_folder) = lower(@absoluteFolder)
LIMIT 1;";
            command.Parameters.AddWithValue("portableFolder", portableFolder);
            command.Parameters.AddWithValue("absoluteFolder", absoluteFolder);

            var rows = ReadRows(command);
            return rows.Count > 0 ? rows[0] : null;
        }

        public int RenameFirmReferences(string oldName, string newName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;

            var oldEmployeesFolder = _folderService.GetEmployeesFolder(oldName);
            var newEmployeesFolder = _folderService.GetEmployeesFolder(newName);
            var oldPortableFolder = ToPortablePath(oldEmployeesFolder);
            var newPortableFolder = ToPortablePath(newEmployeesFolder);
            var oldAbsoluteFolder = NormalizeFullPath(oldEmployeesFolder);
            var newAbsoluteFolder = NormalizeFullPath(newEmployeesFolder);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = @"
UPDATE app.employee_index
SET firm_name = @newName,
    employee_folder = CASE
        WHEN @oldPortableFolder <> '' AND lower(employee_folder) = lower(@oldPortableFolder)
            THEN @newPortableFolder
        WHEN @oldPortableFolder <> '' AND lower(employee_folder) LIKE lower(@oldPortableFolderLike)
            THEN @newPortableFolder || substr(employee_folder, length(@oldPortableFolder) + 1)
        WHEN @oldAbsoluteFolder <> '' AND lower(employee_folder) = lower(@oldAbsoluteFolder)
            THEN @newAbsoluteFolder
        WHEN @oldAbsoluteFolder <> '' AND lower(employee_folder) LIKE lower(@oldAbsoluteFolderLike)
            THEN @newAbsoluteFolder || substr(employee_folder, length(@oldAbsoluteFolder) + 1)
        ELSE employee_folder
    END
WHERE lower(firm_name) = lower(@oldName);";
            command.Parameters.AddWithValue("oldName", oldName);
            command.Parameters.AddWithValue("newName", newName);
            command.Parameters.AddWithValue("oldPortableFolder", oldPortableFolder);
            command.Parameters.AddWithValue("newPortableFolder", newPortableFolder);
            command.Parameters.AddWithValue("oldPortableFolderLike", BuildChildPathLike(oldPortableFolder));
            command.Parameters.AddWithValue("oldAbsoluteFolder", oldAbsoluteFolder);
            command.Parameters.AddWithValue("newAbsoluteFolder", newAbsoluteFolder);
            command.Parameters.AddWithValue("oldAbsoluteFolderLike", BuildChildPathLike(oldAbsoluteFolder));
            return command.ExecuteNonQuery();
        }

        public EmployeeIndexRebuildResult RebuildEmployeeIndex(IReadOnlyList<EmployeeIndexRow> rows, int foldersScanned = 0, int foldersSkipped = 0)
        {
            EnsureInitialized();
            var recordsFound = rows.Count;
            var recordsImported = 0;

            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using (var clearCommand = connection.CreateCommand())
                {
                    clearCommand.Transaction = transaction;
                    clearCommand.CommandText = "DELETE FROM app.employee_index;";
                    clearCommand.ExecuteNonQuery();
                }

                foreach (var row in rows)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = BuildUpsertSql();
                    AddRowParameters(command, row);
                    command.ExecuteNonQuery();
                    recordsImported++;
                }

                transaction.Commit();
                var finalCount = GetEmployeeIndexCount();
                if (finalCount != recordsFound)
                    throw new InvalidOperationException($"Employee index rebuild mismatch: expected {recordsFound}, imported {finalCount}.");

                return new EmployeeIndexRebuildResult
                {
                    WasRebuildAttempted = true,
                    IsSuccessful = true,
                    RecordsFound = recordsFound,
                    RecordsImported = finalCount,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = $"Rebuilt PostgreSQL employee index with {finalCount} rows."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresEmployeeIndexStorage.RebuildEmployeeIndex", ex);
                return new EmployeeIndexRebuildResult
                {
                    WasRebuildAttempted = true,
                    IsSuccessful = false,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    FoldersScanned = foldersScanned,
                    FoldersSkipped = foldersSkipped,
                    Message = ex.Message
                };
            }
        }

        private static string BuildUpsertSql()
        {
            return @"
INSERT INTO app.employee_index (
    unique_id, full_name, first_name, last_name, firm_name, employee_folder, employee_type, status,
    start_date, end_date, contract_type, position_title, position_number, phone, email,
    passport_number, visa_number, insurance_number, passport_expiry, visa_expiry, insurance_expiry,
    work_permit_name, work_permit_expiry, bank_account_number, bank_name,
    is_archived, archived_from_firm, photo_path, has_photo, has_passport, has_visa, has_insurance, updated_at
) VALUES (
    @uniqueId, @fullName, @firstName, @lastName, @firmName, @employeeFolder, @employeeType, @status,
    @startDate, @endDate, @contractType, @positionTitle, @positionNumber, @phone, @email,
    @passportNumber, @visaNumber, @insuranceNumber, @passportExpiry, @visaExpiry, @insuranceExpiry,
    @workPermitName, @workPermitExpiry, @bankAccountNumber, @bankName,
    @isArchived, @archivedFromFirm, @photoPath, @hasPhoto, @hasPassport, @hasVisa, @hasInsurance, @updatedAt
)
ON CONFLICT (unique_id) DO UPDATE SET
    full_name = EXCLUDED.full_name,
    first_name = EXCLUDED.first_name,
    last_name = EXCLUDED.last_name,
    firm_name = EXCLUDED.firm_name,
    employee_folder = EXCLUDED.employee_folder,
    employee_type = EXCLUDED.employee_type,
    status = EXCLUDED.status,
    start_date = EXCLUDED.start_date,
    end_date = EXCLUDED.end_date,
    contract_type = EXCLUDED.contract_type,
    position_title = EXCLUDED.position_title,
    position_number = EXCLUDED.position_number,
    phone = EXCLUDED.phone,
    email = EXCLUDED.email,
    passport_number = EXCLUDED.passport_number,
    visa_number = EXCLUDED.visa_number,
    insurance_number = EXCLUDED.insurance_number,
    passport_expiry = EXCLUDED.passport_expiry,
    visa_expiry = EXCLUDED.visa_expiry,
    insurance_expiry = EXCLUDED.insurance_expiry,
    work_permit_name = EXCLUDED.work_permit_name,
    work_permit_expiry = EXCLUDED.work_permit_expiry,
    bank_account_number = EXCLUDED.bank_account_number,
    bank_name = EXCLUDED.bank_name,
    is_archived = EXCLUDED.is_archived,
    archived_from_firm = EXCLUDED.archived_from_firm,
    photo_path = EXCLUDED.photo_path,
    has_photo = EXCLUDED.has_photo,
    has_passport = EXCLUDED.has_passport,
    has_visa = EXCLUDED.has_visa,
    has_insurance = EXCLUDED.has_insurance,
    updated_at = EXCLUDED.updated_at;";
        }

        private void AddRowParameters(NpgsqlCommand command, EmployeeIndexRow row)
        {
            command.Parameters.AddWithValue("uniqueId", row.UniqueId ?? string.Empty);
            command.Parameters.AddWithValue("fullName", row.FullName ?? string.Empty);
            command.Parameters.AddWithValue("firstName", row.FirstName ?? string.Empty);
            command.Parameters.AddWithValue("lastName", row.LastName ?? string.Empty);
            command.Parameters.AddWithValue("firmName", row.FirmName ?? string.Empty);
            command.Parameters.AddWithValue("employeeFolder", ToPortablePath(row.EmployeeFolder));
            command.Parameters.AddWithValue("employeeType", row.EmployeeType ?? string.Empty);
            command.Parameters.AddWithValue("status", row.Status ?? string.Empty);
            command.Parameters.AddWithValue("startDate", row.StartDate ?? string.Empty);
            command.Parameters.AddWithValue("endDate", row.EndDate ?? string.Empty);
            command.Parameters.AddWithValue("contractType", row.ContractType ?? string.Empty);
            command.Parameters.AddWithValue("positionTitle", row.PositionTitle ?? string.Empty);
            command.Parameters.AddWithValue("positionNumber", row.PositionNumber ?? string.Empty);
            command.Parameters.AddWithValue("phone", row.Phone ?? string.Empty);
            command.Parameters.AddWithValue("email", row.Email ?? string.Empty);
            command.Parameters.AddWithValue("passportNumber", row.PassportNumber ?? string.Empty);
            command.Parameters.AddWithValue("visaNumber", row.VisaNumber ?? string.Empty);
            command.Parameters.AddWithValue("insuranceNumber", row.InsuranceNumber ?? string.Empty);
            command.Parameters.AddWithValue("passportExpiry", row.PassportExpiry ?? string.Empty);
            command.Parameters.AddWithValue("visaExpiry", row.VisaExpiry ?? string.Empty);
            command.Parameters.AddWithValue("insuranceExpiry", row.InsuranceExpiry ?? string.Empty);
            command.Parameters.AddWithValue("workPermitName", row.WorkPermitName ?? string.Empty);
            command.Parameters.AddWithValue("workPermitExpiry", row.WorkPermitExpiry ?? string.Empty);
            command.Parameters.AddWithValue("bankAccountNumber", row.BankAccountNumber ?? string.Empty);
            command.Parameters.AddWithValue("bankName", row.BankName ?? string.Empty);
            command.Parameters.AddWithValue("isArchived", row.IsArchived ? 1 : 0);
            command.Parameters.AddWithValue("archivedFromFirm", row.ArchivedFromFirm ?? string.Empty);
            command.Parameters.AddWithValue("photoPath", ToPortablePath(row.PhotoPath));
            command.Parameters.AddWithValue("hasPhoto", row.HasPhoto ? 1 : 0);
            command.Parameters.AddWithValue("hasPassport", row.HasPassport ? 1 : 0);
            command.Parameters.AddWithValue("hasVisa", row.HasVisa ? 1 : 0);
            command.Parameters.AddWithValue("hasInsurance", row.HasInsurance ? 1 : 0);
            command.Parameters.AddWithValue("updatedAt", row.UpdatedAt ?? string.Empty);
        }

        private List<EmployeeIndexRow> ReadRows(NpgsqlCommand command)
        {
            var rows = new List<EmployeeIndexRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EmployeeIndexRow
                {
                    UniqueId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    FullName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    FirstName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                    LastName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    FirmName = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    EmployeeFolder = ResolveStoredPath(reader.IsDBNull(5) ? string.Empty : reader.GetString(5)),
                    EmployeeType = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                    Status = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                    StartDate = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                    EndDate = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                    ContractType = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                    PositionTitle = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
                    PositionNumber = reader.IsDBNull(12) ? string.Empty : reader.GetString(12),
                    Phone = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                    Email = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    PassportNumber = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    VisaNumber = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
                    InsuranceNumber = reader.IsDBNull(17) ? string.Empty : reader.GetString(17),
                    PassportExpiry = reader.IsDBNull(18) ? string.Empty : reader.GetString(18),
                    VisaExpiry = reader.IsDBNull(19) ? string.Empty : reader.GetString(19),
                    InsuranceExpiry = reader.IsDBNull(20) ? string.Empty : reader.GetString(20),
                    WorkPermitName = reader.IsDBNull(21) ? string.Empty : reader.GetString(21),
                    WorkPermitExpiry = reader.IsDBNull(22) ? string.Empty : reader.GetString(22),
                    BankAccountNumber = reader.IsDBNull(23) ? string.Empty : reader.GetString(23),
                    BankName = reader.IsDBNull(24) ? string.Empty : reader.GetString(24),
                    IsArchived = !reader.IsDBNull(25) && reader.GetInt32(25) != 0,
                    ArchivedFromFirm = reader.IsDBNull(26) ? string.Empty : reader.GetString(26),
                    PhotoPath = ResolveStoredPath(reader.IsDBNull(27) ? string.Empty : reader.GetString(27)),
                    HasPhoto = !reader.IsDBNull(28) && reader.GetInt32(28) != 0,
                    HasPassport = !reader.IsDBNull(29) && reader.GetInt32(29) != 0,
                    HasVisa = !reader.IsDBNull(30) && reader.GetInt32(30) != 0,
                    HasInsurance = !reader.IsDBNull(31) && reader.GetInt32(31) != 0,
                    UpdatedAt = reader.IsDBNull(32) ? string.Empty : reader.GetString(32)
                });
            }

            return rows;
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

        private static bool IsLegacyAbsolutePath(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);

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
            return string.IsNullOrWhiteSpace(rootPath)
                ? path
                : NormalizeFullPath(Path.Combine(rootPath, path));
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

            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static string BuildChildPathLike(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : EnsureTrailingSeparator(path) + "%";
    }
}

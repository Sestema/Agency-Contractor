using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public static class EmployeeIndexExtras
    {
        public const int CurrentVersion = 1;

        public const string ColumnList =
            "extras_version, source_json_utc, birth_date, has_rodne_cislo, rodne_cislo, gender, citizenship, " +
            "passport_city, passport_country, passport_authority, visa_authority, visa_start_date, work_address_tag, " +
            "highest_education_code, address_local_street, address_local_number, address_local_city, address_local_zip, " +
            "address_abroad_street, address_abroad_number, address_abroad_city, address_abroad_zip, " +
            "ignored_documents_json, custom_documents_json, firm_history_json";

        private static readonly (string Name, string Definition)[] Columns =
        {
            ("extras_version", "INTEGER NOT NULL DEFAULT 0"),
            ("source_json_utc", "TEXT"),
            ("birth_date", "TEXT"),
            ("has_rodne_cislo", "INTEGER NOT NULL DEFAULT 0"),
            ("rodne_cislo", "TEXT"),
            ("gender", "TEXT"),
            ("citizenship", "TEXT"),
            ("passport_city", "TEXT"),
            ("passport_country", "TEXT"),
            ("passport_authority", "TEXT"),
            ("visa_authority", "TEXT"),
            ("visa_start_date", "TEXT"),
            ("work_address_tag", "TEXT"),
            ("highest_education_code", "TEXT"),
            ("address_local_street", "TEXT"),
            ("address_local_number", "TEXT"),
            ("address_local_city", "TEXT"),
            ("address_local_zip", "TEXT"),
            ("address_abroad_street", "TEXT"),
            ("address_abroad_number", "TEXT"),
            ("address_abroad_city", "TEXT"),
            ("address_abroad_zip", "TEXT"),
            ("ignored_documents_json", "TEXT"),
            ("custom_documents_json", "TEXT"),
            ("firm_history_json", "TEXT")
        };

        public static void EnsureSqlite(SqliteConnection connection)
        {
            foreach (var column in Columns)
                EnsureSqliteColumn(connection, column.Name, column.Definition);
        }

        public static void EnsurePostgres(NpgsqlConnection connection)
        {
            using var command = connection.CreateCommand();
            foreach (var column in Columns)
            {
                command.CommandText = $"ALTER TABLE app.employee_index ADD COLUMN IF NOT EXISTS {column.Name} {column.Definition};";
                command.ExecuteNonQuery();
            }
        }

        public static void Apply(EmployeeIndexRow row, EmployeeData data, string employeeFolder)
        {
            row.ExtrasVersion = CurrentVersion;
            row.SourceJsonUtc = ReadSourceTimestamp(employeeFolder);
            row.BirthDate = data.BirthDate ?? string.Empty;
            row.HasRodneCislo = data.HasRodneCisloData;
            row.RodneCislo = data.HasRodneCisloData ? data.RodneCislo ?? string.Empty : string.Empty;
            row.Gender = data.Gender ?? string.Empty;
            row.Citizenship = data.Citizenship ?? string.Empty;
            row.PassportCity = data.PassportCity ?? string.Empty;
            row.PassportCountry = data.PassportCountry ?? string.Empty;
            row.PassportAuthority = data.PassportAuthority ?? string.Empty;
            row.VisaAuthority = data.VisaAuthority ?? string.Empty;
            row.VisaStartDate = data.VisaStartDate ?? string.Empty;
            row.WorkAddressTag = data.WorkAddressTag ?? string.Empty;
            row.HighestEducationCode = data.HighestEducationCode ?? string.Empty;
            row.AddressLocalStreet = data.AddressLocal?.Street ?? string.Empty;
            row.AddressLocalNumber = data.AddressLocal?.Number ?? string.Empty;
            row.AddressLocalCity = data.AddressLocal?.City ?? string.Empty;
            row.AddressLocalZip = data.AddressLocal?.Zip ?? string.Empty;
            row.AddressAbroadStreet = data.AddressAbroad?.Street ?? string.Empty;
            row.AddressAbroadNumber = data.AddressAbroad?.Number ?? string.Empty;
            row.AddressAbroadCity = data.AddressAbroad?.City ?? string.Empty;
            row.AddressAbroadZip = data.AddressAbroad?.Zip ?? string.Empty;
            row.IgnoredDocumentsJson = JsonSerializer.Serialize(data.IgnoredDocuments ?? new Dictionary<string, string>());
            row.CustomDocumentsJson = JsonSerializer.Serialize(data.CustomDocuments ?? new List<CustomSignedDocument>());
            row.FirmHistoryJson = JsonSerializer.Serialize(data.FirmHistory ?? new List<FirmHistoryEntry>());
        }

        public static bool IsFresh(EmployeeIndexRow row, DateTime? jsonWriteUtc)
        {
            if (row.ExtrasVersion < CurrentVersion)
                return false;

            if (jsonWriteUtc == null)
                return true;

            if (!TryParseUtc(row.SourceJsonUtc, out var sourceUtc))
                return false;

            return jsonWriteUtc.Value <= sourceUtc.AddSeconds(2);
        }

        public static EmployeeData ToEmployeeData(EmployeeIndexRow row)
        {
            return new EmployeeData
            {
                UniqueId = row.UniqueId ?? string.Empty,
                FirstName = row.FirstName ?? string.Empty,
                LastName = row.LastName ?? string.Empty,
                EmployeeType = string.IsNullOrWhiteSpace(row.EmployeeType) ? "visa" : row.EmployeeType,
                Gender = string.IsNullOrWhiteSpace(row.Gender) ? "male" : row.Gender,
                BirthDate = row.BirthDate ?? string.Empty,
                HasRodneCisloData = row.HasRodneCislo,
                RodneCislo = row.HasRodneCislo ? row.RodneCislo ?? string.Empty : string.Empty,
                Citizenship = row.Citizenship ?? string.Empty,
                PassportNumber = row.PassportNumber ?? string.Empty,
                PassportAuthority = row.PassportAuthority ?? string.Empty,
                PassportCity = row.PassportCity ?? string.Empty,
                PassportCountry = row.PassportCountry ?? string.Empty,
                PassportExpiry = row.PassportExpiry ?? string.Empty,
                VisaNumber = row.VisaNumber ?? string.Empty,
                VisaAuthority = row.VisaAuthority ?? string.Empty,
                VisaStartDate = row.VisaStartDate ?? string.Empty,
                VisaExpiry = row.VisaExpiry ?? string.Empty,
                InsuranceNumber = row.InsuranceNumber ?? string.Empty,
                InsuranceExpiry = row.InsuranceExpiry ?? string.Empty,
                WorkPermitName = row.WorkPermitName ?? string.Empty,
                WorkPermitExpiry = row.WorkPermitExpiry ?? string.Empty,
                HasBankAccountData = !string.IsNullOrWhiteSpace(row.BankAccountNumber) || !string.IsNullOrWhiteSpace(row.BankName),
                BankAccountNumber = row.BankAccountNumber ?? string.Empty,
                BankName = row.BankName ?? string.Empty,
                Phone = row.Phone ?? string.Empty,
                Email = row.Email ?? string.Empty,
                StartDate = row.StartDate ?? string.Empty,
                EndDate = row.EndDate ?? string.Empty,
                ContractType = row.ContractType ?? string.Empty,
                PositionTag = row.PositionTitle ?? string.Empty,
                PositionNumber = row.PositionNumber ?? string.Empty,
                WorkAddressTag = row.WorkAddressTag ?? string.Empty,
                HighestEducationCode = row.HighestEducationCode ?? string.Empty,
                Status = row.Status ?? string.Empty,
                IsArchived = row.IsArchived,
                ArchivedFromFirm = row.ArchivedFromFirm ?? string.Empty,
                AddressLocal = new EmployeeAddress
                {
                    Street = row.AddressLocalStreet ?? string.Empty,
                    Number = row.AddressLocalNumber ?? string.Empty,
                    City = row.AddressLocalCity ?? string.Empty,
                    Zip = row.AddressLocalZip ?? string.Empty
                },
                AddressAbroad = new EmployeeAddress
                {
                    Street = row.AddressAbroadStreet ?? string.Empty,
                    Number = row.AddressAbroadNumber ?? string.Empty,
                    City = row.AddressAbroadCity ?? string.Empty,
                    Zip = row.AddressAbroadZip ?? string.Empty
                },
                IgnoredDocuments = Deserialize<Dictionary<string, string>>(row.IgnoredDocumentsJson) ?? new Dictionary<string, string>(),
                CustomDocuments = Deserialize<List<CustomSignedDocument>>(row.CustomDocumentsJson) ?? new List<CustomSignedDocument>(),
                FirmHistory = Deserialize<List<FirmHistoryEntry>>(row.FirmHistoryJson) ?? new List<FirmHistoryEntry>()
            };
        }

        public static void ReadInto(IDataRecord reader, EmployeeIndexRow row)
        {
            var index = 33;
            row.ExtrasVersion = ReadInt(reader, index++);
            row.SourceJsonUtc = ReadString(reader, index++);
            row.BirthDate = ReadString(reader, index++);
            row.HasRodneCislo = ReadInt(reader, index++) != 0;
            row.RodneCislo = ReadString(reader, index++);
            row.Gender = ReadString(reader, index++);
            row.Citizenship = ReadString(reader, index++);
            row.PassportCity = ReadString(reader, index++);
            row.PassportCountry = ReadString(reader, index++);
            row.PassportAuthority = ReadString(reader, index++);
            row.VisaAuthority = ReadString(reader, index++);
            row.VisaStartDate = ReadString(reader, index++);
            row.WorkAddressTag = ReadString(reader, index++);
            row.HighestEducationCode = ReadString(reader, index++);
            row.AddressLocalStreet = ReadString(reader, index++);
            row.AddressLocalNumber = ReadString(reader, index++);
            row.AddressLocalCity = ReadString(reader, index++);
            row.AddressLocalZip = ReadString(reader, index++);
            row.AddressAbroadStreet = ReadString(reader, index++);
            row.AddressAbroadNumber = ReadString(reader, index++);
            row.AddressAbroadCity = ReadString(reader, index++);
            row.AddressAbroadZip = ReadString(reader, index++);
            row.IgnoredDocumentsJson = ReadString(reader, index++);
            row.CustomDocumentsJson = ReadString(reader, index++);
            row.FirmHistoryJson = ReadString(reader, index++);
        }

        public static void AddSqliteParameters(SqliteCommand command, EmployeeIndexRow row)
        {
            command.Parameters.AddWithValue("@extrasVersion", row.ExtrasVersion);
            command.Parameters.AddWithValue("@sourceJsonUtc", row.SourceJsonUtc ?? string.Empty);
            command.Parameters.AddWithValue("@birthDate", row.BirthDate ?? string.Empty);
            command.Parameters.AddWithValue("@hasRodneCislo", row.HasRodneCislo ? 1 : 0);
            command.Parameters.AddWithValue("@rodneCislo", row.RodneCislo ?? string.Empty);
            command.Parameters.AddWithValue("@gender", row.Gender ?? string.Empty);
            command.Parameters.AddWithValue("@citizenship", row.Citizenship ?? string.Empty);
            command.Parameters.AddWithValue("@passportCity", row.PassportCity ?? string.Empty);
            command.Parameters.AddWithValue("@passportCountry", row.PassportCountry ?? string.Empty);
            command.Parameters.AddWithValue("@passportAuthority", row.PassportAuthority ?? string.Empty);
            command.Parameters.AddWithValue("@visaAuthority", row.VisaAuthority ?? string.Empty);
            command.Parameters.AddWithValue("@visaStartDate", row.VisaStartDate ?? string.Empty);
            command.Parameters.AddWithValue("@workAddressTag", row.WorkAddressTag ?? string.Empty);
            command.Parameters.AddWithValue("@highestEducationCode", row.HighestEducationCode ?? string.Empty);
            command.Parameters.AddWithValue("@addressLocalStreet", row.AddressLocalStreet ?? string.Empty);
            command.Parameters.AddWithValue("@addressLocalNumber", row.AddressLocalNumber ?? string.Empty);
            command.Parameters.AddWithValue("@addressLocalCity", row.AddressLocalCity ?? string.Empty);
            command.Parameters.AddWithValue("@addressLocalZip", row.AddressLocalZip ?? string.Empty);
            command.Parameters.AddWithValue("@addressAbroadStreet", row.AddressAbroadStreet ?? string.Empty);
            command.Parameters.AddWithValue("@addressAbroadNumber", row.AddressAbroadNumber ?? string.Empty);
            command.Parameters.AddWithValue("@addressAbroadCity", row.AddressAbroadCity ?? string.Empty);
            command.Parameters.AddWithValue("@addressAbroadZip", row.AddressAbroadZip ?? string.Empty);
            command.Parameters.AddWithValue("@ignoredDocumentsJson", row.IgnoredDocumentsJson ?? string.Empty);
            command.Parameters.AddWithValue("@customDocumentsJson", row.CustomDocumentsJson ?? string.Empty);
            command.Parameters.AddWithValue("@firmHistoryJson", row.FirmHistoryJson ?? string.Empty);
        }

        public static void AddPostgresParameters(NpgsqlCommand command, EmployeeIndexRow row)
        {
            command.Parameters.AddWithValue("extrasVersion", row.ExtrasVersion);
            command.Parameters.AddWithValue("sourceJsonUtc", row.SourceJsonUtc ?? string.Empty);
            command.Parameters.AddWithValue("birthDate", row.BirthDate ?? string.Empty);
            command.Parameters.AddWithValue("hasRodneCislo", row.HasRodneCislo ? 1 : 0);
            command.Parameters.AddWithValue("rodneCislo", row.RodneCislo ?? string.Empty);
            command.Parameters.AddWithValue("gender", row.Gender ?? string.Empty);
            command.Parameters.AddWithValue("citizenship", row.Citizenship ?? string.Empty);
            command.Parameters.AddWithValue("passportCity", row.PassportCity ?? string.Empty);
            command.Parameters.AddWithValue("passportCountry", row.PassportCountry ?? string.Empty);
            command.Parameters.AddWithValue("passportAuthority", row.PassportAuthority ?? string.Empty);
            command.Parameters.AddWithValue("visaAuthority", row.VisaAuthority ?? string.Empty);
            command.Parameters.AddWithValue("visaStartDate", row.VisaStartDate ?? string.Empty);
            command.Parameters.AddWithValue("workAddressTag", row.WorkAddressTag ?? string.Empty);
            command.Parameters.AddWithValue("highestEducationCode", row.HighestEducationCode ?? string.Empty);
            command.Parameters.AddWithValue("addressLocalStreet", row.AddressLocalStreet ?? string.Empty);
            command.Parameters.AddWithValue("addressLocalNumber", row.AddressLocalNumber ?? string.Empty);
            command.Parameters.AddWithValue("addressLocalCity", row.AddressLocalCity ?? string.Empty);
            command.Parameters.AddWithValue("addressLocalZip", row.AddressLocalZip ?? string.Empty);
            command.Parameters.AddWithValue("addressAbroadStreet", row.AddressAbroadStreet ?? string.Empty);
            command.Parameters.AddWithValue("addressAbroadNumber", row.AddressAbroadNumber ?? string.Empty);
            command.Parameters.AddWithValue("addressAbroadCity", row.AddressAbroadCity ?? string.Empty);
            command.Parameters.AddWithValue("addressAbroadZip", row.AddressAbroadZip ?? string.Empty);
            command.Parameters.AddWithValue("ignoredDocumentsJson", row.IgnoredDocumentsJson ?? string.Empty);
            command.Parameters.AddWithValue("customDocumentsJson", row.CustomDocumentsJson ?? string.Empty);
            command.Parameters.AddWithValue("firmHistoryJson", row.FirmHistoryJson ?? string.Empty);
        }

        private static string ReadSourceTimestamp(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return string.Empty;

            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            if (!File.Exists(jsonPath))
                return string.Empty;

            return File.GetLastWriteTimeUtc(jsonPath).ToString("O");
        }

        private static bool TryParseUtc(string? value, out DateTime utc)
        {
            utc = default;
            if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
                return false;

            utc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            return true;
        }

        private static void EnsureSqliteColumn(SqliteConnection connection, string columnName, string columnDefinition)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info(employee_index);";
            using var reader = checkCommand.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE employee_index ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private static string ReadString(IDataRecord reader, int index)
        {
            return reader.IsDBNull(index) ? string.Empty : reader.GetString(index);
        }

        private static int ReadInt(IDataRecord reader, int index)
        {
            return reader.IsDBNull(index) ? 0 : Convert.ToInt32(reader.GetValue(index), CultureInfo.InvariantCulture);
        }

        private static T? Deserialize<T>(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return default;

            try
            {
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (JsonException)
            {
                return default;
            }
        }
    }
}

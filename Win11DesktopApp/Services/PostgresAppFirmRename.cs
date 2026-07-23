using System;
using System.IO;
using Npgsql;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Renames firm references in Postgres app.* tables that LocalDbService.RenameCurrentFirmReferences
    /// updates in SQLite mode (advances, accommodations, custom fields, salary reports, folder paths).
    /// </summary>
    internal static class PostgresAppFirmRename
    {
        public static int Rename(
            AppSettingsService settingsService,
            FolderService folderService,
            string oldName,
            string newName,
            string oldCompanyFolder,
            string newCompanyFolder)
        {
            if (settingsService == null)
                throw new ArgumentNullException(nameof(settingsService));
            if (folderService == null)
                throw new ArgumentNullException(nameof(folderService));
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return 0;

            var oldAbsolute = NormalizeFolder(oldCompanyFolder);
            var newAbsolute = NormalizeFolder(newCompanyFolder);
            var oldRelative = ToPortablePath(folderService, oldCompanyFolder);
            var newRelative = ToPortablePath(folderService, newCompanyFolder);

            using var connection = PostgresConnectionFactory.OpenConnection(settingsService);
            using var transaction = connection.BeginTransaction();

            EnsureAppSchema(connection, transaction);

            var updated = 0;
            updated += RenameTextColumn(connection, transaction, "app.custom_salary_fields", "firm_name", oldName, newName);
            updated += RenameTextColumn(connection, transaction, "app.advances", "company_id", oldName, newName);
            updated += RenameTextColumn(connection, transaction, "app.accommodations", "company_id", oldName, newName);
            updated += RenameSalaryReports(connection, transaction, oldName, newName);

            foreach (var table in new[]
                     {
                         "app.salary_history",
                         "app.advances",
                         "app.activity_log",
                         "app.archive_log",
                         "app.employee_history",
                         "app.accommodations"
                     })
            {
                updated += RenamePathColumn(
                    connection,
                    transaction,
                    table,
                    "employee_folder",
                    oldAbsolute,
                    newAbsolute,
                    oldRelative,
                    newRelative);
            }

            transaction.Commit();
            LoggingService.LogInfo(
                "PostgresAppFirmRename",
                $"Renamed firm app references '{oldName}' -> '{newName}'. rows={updated}.");
            return updated;
        }

        private static void EnsureAppSchema(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "CREATE SCHEMA IF NOT EXISTS app;";
            command.ExecuteNonQuery();
        }

        private static int RenameSalaryReports(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string oldName,
            string newName)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
UPDATE app.salary_reports
SET company_id = @newName,
    company_name = @newName
WHERE lower(company_id) = lower(@oldName)
   OR lower(COALESCE(company_name, '')) = lower(@oldName);";
            command.Parameters.AddWithValue("oldName", oldName);
            command.Parameters.AddWithValue("newName", newName);
            try
            {
                return command.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return 0;
            }
        }

        private static int RenameTextColumn(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string table,
            string column,
            string oldValue,
            string newValue)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $@"
UPDATE {table}
SET {column} = @newValue
WHERE lower({column}) = lower(@oldValue);";
            command.Parameters.AddWithValue("oldValue", oldValue);
            command.Parameters.AddWithValue("newValue", newValue);
            try
            {
                return command.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                return 0;
            }
        }

        private static int RenamePathColumn(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string table,
            string column,
            string oldAbsolute,
            string newAbsolute,
            string oldRelative,
            string newRelative)
        {
            if (string.IsNullOrWhiteSpace(oldAbsolute) && string.IsNullOrWhiteSpace(oldRelative))
                return 0;

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Same boundary-safe prefix remap as LocalDbService / salary rename (no LIKE).
            command.CommandText = $@"
UPDATE {table}
SET {column} = CASE
    WHEN lower(replace(COALESCE({column}, ''), '/', '\')) = lower(@oldAbsolute)
      OR (
           length(replace(COALESCE({column}, ''), '/', '\')) > length(@oldAbsolute)
           AND lower(substr(replace(COALESCE({column}, ''), '/', '\'), 1, length(@oldAbsolute))) = lower(@oldAbsolute)
           AND substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldAbsolute) + 1, 1) = '\'
         )
    THEN @newAbsolute || substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldAbsolute) + 1)
    WHEN lower(replace(COALESCE({column}, ''), '/', '\')) = lower(@oldRelative)
      OR (
           length(replace(COALESCE({column}, ''), '/', '\')) > length(@oldRelative)
           AND lower(substr(replace(COALESCE({column}, ''), '/', '\'), 1, length(@oldRelative))) = lower(@oldRelative)
           AND substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldRelative) + 1, 1) = '\'
         )
    THEN @newRelative || substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldRelative) + 1)
    ELSE {column}
END
WHERE lower(replace(COALESCE({column}, ''), '/', '\')) = lower(@oldAbsolute)
   OR (
        length(replace(COALESCE({column}, ''), '/', '\')) > length(@oldAbsolute)
        AND lower(substr(replace(COALESCE({column}, ''), '/', '\'), 1, length(@oldAbsolute))) = lower(@oldAbsolute)
        AND substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldAbsolute) + 1, 1) = '\'
      )
   OR lower(replace(COALESCE({column}, ''), '/', '\')) = lower(@oldRelative)
   OR (
        length(replace(COALESCE({column}, ''), '/', '\')) > length(@oldRelative)
        AND lower(substr(replace(COALESCE({column}, ''), '/', '\'), 1, length(@oldRelative))) = lower(@oldRelative)
        AND substr(replace(COALESCE({column}, ''), '/', '\'), length(@oldRelative) + 1, 1) = '\'
      );";
            command.Parameters.AddWithValue("oldAbsolute", oldAbsolute ?? string.Empty);
            command.Parameters.AddWithValue("newAbsolute", newAbsolute ?? string.Empty);
            command.Parameters.AddWithValue("oldRelative", oldRelative ?? string.Empty);
            command.Parameters.AddWithValue("newRelative", newRelative ?? string.Empty);
            try
            {
                return command.ExecuteNonQuery();
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable
                                              || ex.SqlState == PostgresErrorCodes.UndefinedColumn)
            {
                return 0;
            }
        }

        private static string NormalizeFolder(string folder)
            => string.IsNullOrWhiteSpace(folder)
                ? string.Empty
                : folder.Replace('/', '\\').TrimEnd('\\');

        private static string ToPortablePath(FolderService folderService, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                var root = folderService.RootPath;
                if (string.IsNullOrWhiteSpace(root))
                    return NormalizeFolder(path);

                var full = Path.GetFullPath(path);
                var rootFull = Path.GetFullPath(root);
                if (full.StartsWith(rootFull.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase)
                    || string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    return NormalizeFolder(Path.GetRelativePath(rootFull, full));
                }

                return NormalizeFolder(full);
            }
            catch
            {
                return NormalizeFolder(path);
            }
        }
    }
}

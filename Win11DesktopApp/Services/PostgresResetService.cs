using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresResetResult
    {
        public bool Success { get; init; }
        public string Database { get; init; } = string.Empty;
        public string ErrorMessage { get; init; } = string.Empty;

        public string ToDisplayMessage()
        {
            return Success
                ? $"PostgreSQL дані програми у базі \"{Database}\" видалено. Можна запускати міграцію заново."
                : $"PostgreSQL дані не видалено: {ErrorMessage}";
        }
    }

    public sealed class PostgresResetService
    {
        private readonly AppSettingsService _settingsService;

        public PostgresResetService(AppSettingsService settingsService)
        {
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public async Task<PostgresResetResult> DropApplicationSchemasAsync(CancellationToken cancellationToken = default)
        {
            var settings = _settingsService.Settings;
            var databaseName = string.IsNullOrWhiteSpace(settings.PostgresDatabase)
                ? "agency_db"
                : settings.PostgresDatabase.Trim();

            if (IsReservedDatabase(databaseName))
            {
                return new PostgresResetResult
                {
                    Database = databaseName,
                    ErrorMessage = "Службову базу PostgreSQL видаляти не можна. Використовуйте окрему базу, наприклад agency_db."
                };
            }

            try
            {
                await using var connection = new NpgsqlConnection(BuildConnectionString(databaseName));
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = @"
DROP SCHEMA IF EXISTS salary CASCADE;
DROP SCHEMA IF EXISTS app CASCADE;
DROP SCHEMA IF EXISTS core CASCADE;";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                return new PostgresResetResult
                {
                    Success = true,
                    Database = databaseName
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PostgresResetResult
                {
                    Database = databaseName,
                    ErrorMessage = "Операцію скасовано."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PostgresResetService.DropApplicationSchemasAsync", ex);
                return new PostgresResetResult
                {
                    Database = databaseName,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string BuildConnectionString(string databaseName)
        {
            var settings = _settingsService.Settings;
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(settings.PostgresHost) ? "localhost" : settings.PostgresHost.Trim(),
                Port = settings.PostgresPort <= 0 ? 5432 : settings.PostgresPort,
                Database = databaseName,
                Username = string.IsNullOrWhiteSpace(settings.PostgresUsername) ? "postgres" : settings.PostgresUsername.Trim(),
                Password = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword),
                Timeout = 10,
                CommandTimeout = 30,
                Pooling = false
            };

            return builder.ConnectionString;
        }

        private static bool IsReservedDatabase(string databaseName)
        {
            return string.Equals(databaseName, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(databaseName, "template0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(databaseName, "template1", StringComparison.OrdinalIgnoreCase);
        }
    }
}

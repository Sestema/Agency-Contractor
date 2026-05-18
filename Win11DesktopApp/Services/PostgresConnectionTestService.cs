using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresConnectionTestRequest
    {
        public string Host { get; init; } = "localhost";
        public int Port { get; init; } = 5432;
        public string Database { get; init; } = "postgres";
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
        public int TimeoutSeconds { get; init; } = 5;
    }

    public sealed class PostgresConnectionTestResult
    {
        public bool Success { get; init; }
        public string ServerVersion { get; init; } = string.Empty;
        public string Database { get; init; } = string.Empty;
        public bool DatabaseExists { get; init; }
        public bool CanCreateDatabase { get; init; }
        public string ErrorMessage { get; init; } = string.Empty;
    }

    public sealed class PostgresConnectionTestService
    {
        public async Task<PostgresConnectionTestResult> TestAsync(
            PostgresConnectionTestRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var databaseName = string.IsNullOrWhiteSpace(request.Database)
                ? "postgres"
                : request.Database.Trim();

            try
            {
                await using var connection = new NpgsqlConnection(BuildConnectionString(request, databaseName));
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                var serverVersion = connection.PostgreSqlVersion?.ToString() ?? string.Empty;
                var databaseExists = await DatabaseExistsAsync(connection, databaseName, cancellationToken).ConfigureAwait(false);
                var canCreateDatabase = await CanCreateDatabaseAsync(connection, cancellationToken).ConfigureAwait(false);

                return new PostgresConnectionTestResult
                {
                    Success = true,
                    ServerVersion = serverVersion,
                    Database = databaseName,
                    DatabaseExists = databaseExists,
                    CanCreateDatabase = canCreateDatabase
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new PostgresConnectionTestResult
                {
                    Database = databaseName,
                    ErrorMessage = "PostgreSQL connection test was cancelled."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PostgresConnectionTestService.TestAsync", ex.Message);
                return new PostgresConnectionTestResult
                {
                    Database = databaseName,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static string BuildConnectionString(PostgresConnectionTestRequest request, string databaseName)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = string.IsNullOrWhiteSpace(request.Host) ? "localhost" : request.Host.Trim(),
                Port = request.Port <= 0 ? 5432 : request.Port,
                Database = databaseName,
                Username = request.Username?.Trim() ?? string.Empty,
                Password = request.Password ?? string.Empty,
                Timeout = request.TimeoutSeconds <= 0 ? 5 : request.TimeoutSeconds,
                CommandTimeout = request.TimeoutSeconds <= 0 ? 5 : request.TimeoutSeconds,
                Pooling = false
            };

            return builder.ConnectionString;
        }

        private static async Task<bool> DatabaseExistsAsync(
            NpgsqlConnection connection,
            string databaseName,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM pg_database WHERE datname = @database LIMIT 1;";
            command.Parameters.AddWithValue("database", databaseName);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result != null;
        }

        private static async Task<bool> CanCreateDatabaseAsync(
            NpgsqlConnection connection,
            CancellationToken cancellationToken)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT rolsuper OR rolcreatedb FROM pg_roles WHERE rolname = current_user;";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is bool canCreate && canCreate;
        }
    }
}

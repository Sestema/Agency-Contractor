using System;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public sealed class PostgresConnectionStringOptions
    {
        public string? Host { get; init; }
        public int? Port { get; init; }
        public string? Database { get; init; }
        public string? Username { get; init; }
        public string? Password { get; init; }
        public bool IncludePassword { get; init; } = true;
        public bool UseEncryptedPasswordFromSettings { get; init; } = true;
        public int? ConnectionTimeoutSeconds { get; init; }
        public int? CommandTimeoutSeconds { get; init; }
        public bool? Pooling { get; init; }
        public int? KeepAliveSeconds { get; init; }
        public bool UseDefaultUsernameWhenEmpty { get; init; } = true;
    }

    public static class PostgresConnectionFactory
    {
        public const int DefaultConnectionTimeoutSeconds = 10;
        public const int DefaultCommandTimeoutSeconds = 30;
        public const int SyncEventKeepAliveSeconds = 30;
        public const string DefaultDatabaseName = "agency_db";
        public const string DefaultUsername = "postgres";

        public static string BuildConnectionString(
            string? host,
            int port,
            string? databaseName,
            string? username,
            string? password,
            PostgresConnectionStringOptions? options = null)
        {
            options ??= new PostgresConnectionStringOptions();

            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = NormalizeHost(host),
                Port = port <= 0 ? 5432 : port,
                Database = NormalizeDatabase(databaseName),
                Username = options.UseDefaultUsernameWhenEmpty
                    ? NormalizeUsername(username)
                    : username?.Trim() ?? string.Empty,
                Timeout = options.ConnectionTimeoutSeconds ?? DefaultConnectionTimeoutSeconds,
                CommandTimeout = options.CommandTimeoutSeconds ?? DefaultCommandTimeoutSeconds,
                Pooling = options.Pooling ?? true
            };

            if (options.IncludePassword)
                builder.Password = password ?? string.Empty;

            if (options.KeepAliveSeconds is int keepAlive && keepAlive > 0)
                builder.KeepAlive = keepAlive;

            return builder.ConnectionString;
        }

        public static string BuildConnectionStringFromSettings(
            AppSettingsService.AppSettings settings,
            PostgresConnectionStringOptions? options = null)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            options ??= new PostgresConnectionStringOptions();

            string? password = options.Password;
            if (password == null && options.IncludePassword && options.UseEncryptedPasswordFromSettings)
                password = LocalSecretProtection.Unprotect(settings.EncryptedPostgresPassword);

            return BuildConnectionString(
                options.Host ?? settings.PostgresHost,
                options.Port ?? settings.PostgresPort,
                options.Database ?? settings.PostgresDatabase,
                options.Username ?? settings.PostgresUsername,
                password,
                options);
        }

        public static NpgsqlConnection OpenConnection(
            AppSettingsService settingsService,
            PostgresConnectionStringOptions? options = null)
        {
            var connection = CreateConnection(settingsService, options);
            connection.Open();
            return connection;
        }

        public static NpgsqlConnection CreateConnection(
            AppSettingsService settingsService,
            PostgresConnectionStringOptions? options = null)
        {
            if (settingsService?.Settings == null)
                throw new InvalidOperationException("PostgreSQL settings are not available.");

            return new NpgsqlConnection(BuildConnectionStringFromSettings(settingsService.Settings, options));
        }

        public static string BuildConnectionStringFromTestRequest(
            PostgresConnectionTestRequest request,
            string databaseName)
        {
            var timeout = request.TimeoutSeconds <= 0 ? 5 : request.TimeoutSeconds;
            return BuildConnectionString(
                request.Host,
                request.Port,
                databaseName,
                request.Username,
                request.Password,
                new PostgresConnectionStringOptions
                {
                    ConnectionTimeoutSeconds = timeout,
                    CommandTimeoutSeconds = timeout,
                    Pooling = false,
                    UseDefaultUsernameWhenEmpty = false
                });
        }

        public static string BuildConnectionStringFromMigrationRequest(
            PostgresMigrationRequest request,
            string databaseName,
            bool includePassword = true)
        {
            var timeout = request.TimeoutSeconds <= 0 ? DefaultConnectionTimeoutSeconds : request.TimeoutSeconds;
            return BuildConnectionString(
                request.Host,
                request.Port,
                databaseName,
                request.Username,
                includePassword ? request.Password : null,
                new PostgresConnectionStringOptions
                {
                    ConnectionTimeoutSeconds = timeout,
                    CommandTimeoutSeconds = timeout,
                    Pooling = true,
                    IncludePassword = includePassword,
                    UseDefaultUsernameWhenEmpty = false
                });
        }

        private static string NormalizeHost(string? host)
            => string.IsNullOrWhiteSpace(host) ? "localhost" : host.Trim();

        private static string NormalizeDatabase(string? databaseName)
            => string.IsNullOrWhiteSpace(databaseName) ? DefaultDatabaseName : databaseName.Trim();

        private static string NormalizeUsername(string? username)
            => string.IsNullOrWhiteSpace(username) ? DefaultUsername : username.Trim();
    }
}

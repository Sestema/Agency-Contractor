using System;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public static class PostgresErrorMessageService
    {
        public static string ToUserMessage(Exception ex)
        {
            var technicalDetails = BuildTechnicalDetails(ex);
            var userMessage = ResolveUserMessage(ex);

            return string.IsNullOrWhiteSpace(technicalDetails)
                ? userMessage
                : string.Format(Res("PostgresErrWithDetailsFmt"), userMessage, technicalDetails);
        }

        private static string ResolveUserMessage(Exception ex)
        {
            if (ex is OperationCanceledException)
                return Res("PostgresErrCancelled");

            if (TryFindPostgresException(ex, out var postgresException))
            {
                return postgresException.SqlState switch
                {
                    PostgresErrorCodes.InvalidCatalogName => Res("PostgresErrDatabaseMissing"),
                    PostgresErrorCodes.InvalidPassword => Res("PostgresErrInvalidPassword"),
                    PostgresErrorCodes.InvalidAuthorizationSpecification => IsPgHbaError(postgresException)
                        ? Res("PostgresErrPgHba")
                        : Res("PostgresErrAuthorization"),
                    PostgresErrorCodes.InsufficientPrivilege => Res("PostgresErrInsufficientPrivilege"),
                    PostgresErrorCodes.AdminShutdown => Res("PostgresErrAdminShutdown"),
                    PostgresErrorCodes.TooManyConnections => Res("PostgresErrTooManyConnections"),
                    _ => Res("PostgresErrGeneric")
                };
            }

            if (ContainsSocketException(ex, out var socketException))
            {
                return socketException.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => Res("PostgresErrConnectionRefused"),
                    SocketError.HostNotFound or SocketError.NoData => Res("PostgresErrHostNotFound"),
                    SocketError.TimedOut => Res("PostgresErrTimeout"),
                    _ => Res("PostgresErrNetwork")
                };
            }

            if (ex is TimeoutException || ex.InnerException is TimeoutException)
                return Res("PostgresErrTimeout");

            if (ex is IOException && ex.InnerException is SocketException)
                return Res("PostgresErrNetwork");

            return Res("PostgresErrGeneric");
        }

        private static bool TryFindPostgresException(Exception ex, out PostgresException postgresException)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is PostgresException pg)
                {
                    postgresException = pg;
                    return true;
                }
            }

            postgresException = null!;
            return false;
        }

        private static bool ContainsSocketException(Exception ex, out SocketException socketException)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is SocketException socket)
                {
                    socketException = socket;
                    return true;
                }
            }

            socketException = null!;
            return false;
        }

        private static bool IsPgHbaError(PostgresException ex)
        {
            var message = ex.MessageText ?? ex.Message;
            return message.Contains("pg_hba.conf", StringComparison.OrdinalIgnoreCase)
                || message.Contains("no pg_hba.conf entry", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildTechnicalDetails(Exception ex)
        {
            if (TryFindPostgresException(ex, out var postgresException))
            {
                var code = string.IsNullOrWhiteSpace(postgresException.SqlState)
                    ? string.Empty
                    : $"{postgresException.SqlState}: ";
                return $"{code}{postgresException.MessageText}";
            }

            return ex.Message;
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;
    }
}

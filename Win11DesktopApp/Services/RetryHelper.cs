using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Win11DesktopApp.Services
{
    public static class RetryHelper
    {
        /// <summary>Returns true for common transient PostgreSQL / network failures (VPN blips, timeouts, restarts).</summary>
        public static bool IsTransientPostgres(Exception ex)
        {
            if (ex is OperationCanceledException)
                return false;
            if (ex is NpgsqlException npg)
                return npg.IsTransient;
            if (ex is TimeoutException)
                return true;
            if (ex is SocketException)
                return true;
            if (ex.InnerException != null)
            {
                if (ex.InnerException is SocketException)
                    return true;
                if (ex.InnerException is NpgsqlException innerNpg)
                    return innerNpg.IsTransient;
            }

            return false;
        }

        /// <summary>Retries synchronous PostgreSQL operations on transient failures.</summary>
        public static void ExecutePostgres(Action action, int maxRetries = 4, int initialDelayMs = 250)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex) when (i < maxRetries && IsTransientPostgres(ex))
                {
                    Thread.Sleep(GetDelayMs(initialDelayMs, i));
                }
            }
        }

        /// <summary>Retries asynchronous PostgreSQL operations on transient failures.</summary>
        public static async Task ExecutePostgresAsync(
            Func<Task> action,
            CancellationToken cancellationToken,
            int maxRetries = 4,
            int initialDelayMs = 250)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await action().ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (i < maxRetries && IsTransientPostgres(ex))
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static bool IsTransientSqliteLock(SqliteException ex)
        {
            return ex.SqliteErrorCode is 5 or 6;
        }

        public static void Execute(Action action, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (IOException) when (i < maxRetries)
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (SqliteException ex) when (i < maxRetries && IsTransientSqliteLock(ex))
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
            }
        }

        public static T Execute<T>(Func<T> func, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    return func();
                }
                catch (IOException) when (i < maxRetries)
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (SqliteException ex) when (i < maxRetries && IsTransientSqliteLock(ex))
                {
                    System.Threading.Thread.Sleep(initialDelayMs * (int)Math.Pow(2, i));
                }
            }
            return func();
        }

        public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int initialDelayMs = 150)
        {
            await ExecuteAsync(action, CancellationToken.None, maxRetries, initialDelayMs);
        }

        public static async Task ExecuteAsync(Func<Task> action, CancellationToken cancellationToken, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await action();
                    return;
                }
                catch (IOException) when (i < maxRetries)
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
                catch (SqliteException ex) when (i < maxRetries && IsTransientSqliteLock(ex))
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
            }
        }

        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> func, int maxRetries = 3, int initialDelayMs = 150)
        {
            return await ExecuteAsync(func, CancellationToken.None, maxRetries, initialDelayMs);
        }

        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> func, CancellationToken cancellationToken, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return await func();
                }
                catch (IOException) when (i < maxRetries)
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
                catch (SqliteException ex) when (i < maxRetries && IsTransientSqliteLock(ex))
                {
                    await Task.Delay(GetDelayMs(initialDelayMs, i), cancellationToken);
                }
            }
            return await func();
        }

        private static int GetDelayMs(int initialDelayMs, int retryIndex)
        {
            return initialDelayMs * (int)Math.Pow(2, retryIndex);
        }
    }
}

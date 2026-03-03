using System;
using System.IO;
using System.Threading.Tasks;

namespace Win11DesktopApp.Services
{
    public static class RetryHelper
    {
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
            }
            return func();
        }

        public static async Task ExecuteAsync(Func<Task> action, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    await action();
                    return;
                }
                catch (IOException) when (i < maxRetries)
                {
                    await Task.Delay(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    await Task.Delay(initialDelayMs * (int)Math.Pow(2, i));
                }
            }
        }

        public static async Task<T> ExecuteAsync<T>(Func<Task<T>> func, int maxRetries = 3, int initialDelayMs = 150)
        {
            for (int i = 0; i <= maxRetries; i++)
            {
                try
                {
                    return await func();
                }
                catch (IOException) when (i < maxRetries)
                {
                    await Task.Delay(initialDelayMs * (int)Math.Pow(2, i));
                }
                catch (UnauthorizedAccessException) when (i < maxRetries)
                {
                    await Task.Delay(initialDelayMs * (int)Math.Pow(2, i));
                }
            }
            return await func();
        }
    }
}

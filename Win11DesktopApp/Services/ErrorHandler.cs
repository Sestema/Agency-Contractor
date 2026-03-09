using System;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public enum ErrorSeverity { Info, Warning, Error, Critical }

    public static class ErrorHandler
    {
        private static bool _criticalDialogShown;

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        public static void Handle(string source, Exception ex, bool showUser = false)
        {
            Report(source, ex.Message, ErrorSeverity.Error, showUser);
        }

        public static void Handle(string source, string message, bool showUser = false)
        {
            Report(source, message, ErrorSeverity.Error, showUser);
        }

        public static void Report(string source, string message, ErrorSeverity severity, bool showUser = true)
        {
            switch (severity)
            {
                case ErrorSeverity.Info:
                    LoggingService.LogInfo(source, message);
                    if (showUser) ToastService.Instance.Info(message);
                    break;

                case ErrorSeverity.Warning:
                    LoggingService.LogWarning(source, message);
                    if (showUser) ToastService.Instance.Warning(message);
                    break;

                case ErrorSeverity.Error:
                    LoggingService.LogError(source, message);
                    if (showUser) ToastService.Instance.Error(message);
                    break;

                case ErrorSeverity.Critical:
                    LoggingService.LogError(source, message);
                    if (_criticalDialogShown)
                        break;

                    _criticalDialogShown = true;
                    Application.Current?.Dispatcher?.Invoke(() =>
                        MessageBox.Show(message, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error));
                    break;
            }
        }

        public static void Report(string source, Exception ex, ErrorSeverity severity, bool showUser = true)
        {
            LoggingService.LogError(source, ex);
            switch (severity)
            {
                case ErrorSeverity.Info:
                    if (showUser) ToastService.Instance.Info(ex.Message);
                    break;
                case ErrorSeverity.Warning:
                    if (showUser) ToastService.Instance.Warning(ex.Message);
                    break;
                case ErrorSeverity.Error:
                    if (showUser) ToastService.Instance.Error(ex.Message);
                    break;
                case ErrorSeverity.Critical:
                    if (_criticalDialogShown)
                        break;

                    _criticalDialogShown = true;
                    Application.Current?.Dispatcher?.Invoke(() =>
                        MessageBox.Show($"{Res("MsgUnexpectedError")}\n\n{ex.Message}",
                            Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error));
                    break;
            }
        }

        public static void SafeExecute(string source, Action action, bool showUser = false)
        {
            try { action(); }
            catch (Exception ex) { Report(source, ex, ErrorSeverity.Error, showUser); }
        }

        public static T? SafeExecute<T>(string source, Func<T> func, T? fallback = default, bool showUser = false)
        {
            try { return func(); }
            catch (Exception ex) { Report(source, ex, ErrorSeverity.Error, showUser); return fallback; }
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Win11DesktopApp.Services
{
    public enum ToastSeverity { Info, Success, Warning, Error }

    public class ToastItem : ViewModels.ViewModelBase
    {
        public string Message { get; set; } = "";
        public ToastSeverity Severity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int DurationMs { get; set; } = 4000;

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public string Icon => Severity switch
        {
            ToastSeverity.Success => "\uE73E",
            ToastSeverity.Warning => "\uE7BA",
            ToastSeverity.Error => "\uEA39",
            _ => "\uE946"
        };
    }

    public class ToastService : ViewModels.ViewModelBase
    {
        private static ToastService? _instance;
        public static ToastService Instance => _instance ??= new ToastService();

        public ObservableCollection<ToastItem> Toasts { get; } = new();

        public void Show(string message, ToastSeverity severity = ToastSeverity.Info, int durationMs = 4000)
        {
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                var toast = new ToastItem { Message = message, Severity = severity, DurationMs = durationMs };
                Toasts.Add(toast);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    toast.IsVisible = false;
                    var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    removeTimer.Tick += (_, _) =>
                    {
                        removeTimer.Stop();
                        Toasts.Remove(toast);
                    };
                    removeTimer.Start();
                };
                timer.Start();
            });
        }

        public void Info(string message) => Show(message, ToastSeverity.Info);
        public void Success(string message) => Show(message, ToastSeverity.Success);
        public void Warning(string message) => Show(message, ToastSeverity.Warning, 6000);
        public void Error(string message) => Show(message, ToastSeverity.Error, 8000);
    }
}

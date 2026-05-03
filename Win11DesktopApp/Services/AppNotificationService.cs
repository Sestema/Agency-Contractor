using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Services;

public sealed class AppNotificationItem : ViewModelBase
{
    private bool _isRead;

    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Icon { get; init; } = "\uE946";
    public ToastSeverity Severity { get; init; } = ToastSeverity.Info;
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public bool IsRead
    {
        get => _isRead;
        set => SetProperty(ref _isRead, value);
    }

    public string CreatedAtText => CreatedAt.ToString("HH:mm");
}

public sealed class AppNotificationService : ViewModelBase
{
    private const int MaxNotifications = 100;
    private const string NotificationsFileName = "notifications.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _notificationsPath;
    private bool _suspendSave;

    public ObservableCollection<AppNotificationItem> Notifications { get; } = new();

    public int UnreadCount => Notifications.Count(n => !n.IsRead);
    public bool HasUnread => UnreadCount > 0;

    public AppNotificationService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appData, "AgencyContractor");
        _notificationsPath = Path.Combine(appFolder, NotificationsFileName);
        LoadNotifications();
    }

    public void Success(string title, string message, bool showToast = true) =>
        Add(title, message, ToastSeverity.Success, "\uE73E", showToast);

    public void Info(string title, string message, bool showToast = true) =>
        Add(title, message, ToastSeverity.Info, "\uE946", showToast);

    public void Warning(string title, string message, bool showToast = true) =>
        Add(title, message, ToastSeverity.Warning, "\uE7BA", showToast);

    public void Error(string title, string message, bool showToast = true) =>
        Add(title, message, ToastSeverity.Error, "\uEA39", showToast);

    public void MarkAllRead()
    {
        _suspendSave = true;
        try
        {
            foreach (var notification in Notifications)
                notification.IsRead = true;
        }
        finally
        {
            _suspendSave = false;
        }

        RaiseUnreadChanged();
        SaveNotifications();
    }

    public void ClearAll()
    {
        Notifications.Clear();
        RaiseUnreadChanged();
        SaveNotifications();
    }

    private void Add(string title, string message, ToastSeverity severity, string icon, bool showToast)
    {
        void AddOnUiThread()
        {
            var item = new AppNotificationItem
            {
                Title = title,
                Message = message,
                Severity = severity,
                Icon = icon,
                CreatedAt = DateTime.Now
            };

            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppNotificationItem.IsRead))
                {
                    RaiseUnreadChanged();
                    SaveNotifications();
                }
            };

            Notifications.Insert(0, item);
            while (Notifications.Count > MaxNotifications)
                Notifications.RemoveAt(Notifications.Count - 1);

            RaiseUnreadChanged();
            SaveNotifications();

            if (showToast)
                ToastService.Instance.Show(message, severity);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            AddOnUiThread();
        else
            dispatcher.BeginInvoke(AddOnUiThread);
    }

    private void LoadNotifications()
    {
        try
        {
            _suspendSave = true;
            var storedItems = SafeFileService.ReadJsonOrDefault(
                _notificationsPath,
                new List<StoredNotificationItem>(),
                JsonOptions,
                Encoding.UTF8);

            foreach (var stored in storedItems
                         .Where(item => !string.IsNullOrWhiteSpace(item.Title) || !string.IsNullOrWhiteSpace(item.Message))
                         .OrderByDescending(item => item.CreatedAt)
                         .Take(MaxNotifications))
            {
                var item = new AppNotificationItem
                {
                    Title = stored.Title ?? string.Empty,
                    Message = stored.Message ?? string.Empty,
                    Icon = string.IsNullOrWhiteSpace(stored.Icon) ? "\uE946" : stored.Icon,
                    Severity = stored.Severity,
                    CreatedAt = stored.CreatedAt == default ? DateTime.Now : stored.CreatedAt,
                    IsRead = stored.IsRead
                };
                SubscribeItem(item);
                Notifications.Add(item);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("AppNotificationService.LoadNotifications", ex.Message);
        }
        finally
        {
            _suspendSave = false;
            RaiseUnreadChanged();
        }
    }

    private void SaveNotifications()
    {
        if (_suspendSave)
            return;

        try
        {
            var storedItems = Notifications
                .Take(MaxNotifications)
                .Select(item => new StoredNotificationItem
                {
                    Title = item.Title,
                    Message = item.Message,
                    Icon = item.Icon,
                    Severity = item.Severity,
                    CreatedAt = item.CreatedAt,
                    IsRead = item.IsRead
                })
                .ToList();

            SafeFileService.WriteJsonAtomic(_notificationsPath, storedItems, JsonOptions, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("AppNotificationService.SaveNotifications", ex.Message);
        }
    }

    private void SubscribeItem(AppNotificationItem item)
    {
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppNotificationItem.IsRead))
            {
                RaiseUnreadChanged();
                SaveNotifications();
            }
        };
    }

    private void RaiseUnreadChanged()
    {
        OnPropertyChanged(nameof(UnreadCount));
        OnPropertyChanged(nameof(HasUnread));
    }

    private sealed class StoredNotificationItem
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Icon { get; set; }
        public ToastSeverity Severity { get; set; } = ToastSeverity.Info;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsRead { get; set; }
    }
}

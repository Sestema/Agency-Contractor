using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;

namespace Win11DesktopApp.Services
{
    public enum AccessStatusMode
    {
        Unknown,
        TrialActive,
        Activated,
        ReadOnly,
        Blocked
    }

    public sealed class AccessStatusSnapshot
    {
        public AccessStatusMode Mode { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string AdminMessage { get; init; } = string.Empty;
        public string Severity { get; init; } = "Info";
        public DateTime? ExpiresAtUtc { get; init; }
        public int? DaysLeft { get; init; }
        public bool HasStatus => !string.IsNullOrWhiteSpace(Title);
        public bool HasAdminMessage => !string.IsNullOrWhiteSpace(AdminMessage);
    }

    public sealed class AccessStatusService : INotifyPropertyChanged
    {
        private bool _hasValidLocalLicense;
        private ClientAccessState _clientAccessState = new();
        private RemotePolicy? _remotePolicy;
        private AccessStatusSnapshot _current = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public AccessStatusSnapshot Current
        {
            get => _current;
            private set
            {
                _current = value;
                OnPropertyChanged(nameof(Current));
                OnPropertyChanged(nameof(Mode));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Detail));
                OnPropertyChanged(nameof(AdminMessage));
                OnPropertyChanged(nameof(Severity));
                OnPropertyChanged(nameof(ExpiresAtUtc));
                OnPropertyChanged(nameof(DaysLeft));
                OnPropertyChanged(nameof(HasStatus));
                OnPropertyChanged(nameof(HasAdminMessage));
            }
        }

        public AccessStatusMode Mode => Current.Mode;
        public string Title => Current.Title;
        public string Detail => Current.Detail;
        public string AdminMessage => Current.AdminMessage;
        public string Severity => Current.Severity;
        public DateTime? ExpiresAtUtc => Current.ExpiresAtUtc;
        public int? DaysLeft => Current.DaysLeft;
        public bool HasStatus => Current.HasStatus;
        public bool HasAdminMessage => Current.HasAdminMessage;

        public void Initialize(bool hasValidLocalLicense, ClientAccessState clientAccessState, RemotePolicy? remotePolicy)
        {
            _hasValidLocalLicense = hasValidLocalLicense;
            _clientAccessState = clientAccessState ?? new ClientAccessState();
            _remotePolicy = remotePolicy;
            RefreshPresentation();
        }

        public void RefreshPresentation()
        {
            Current = BuildSnapshot();
        }

        private AccessStatusSnapshot BuildSnapshot()
        {
            var adminMessage = (_remotePolicy?.AdminMessage ?? App.AppSettingsService?.Settings.AdminMessage ?? string.Empty).Trim();

            if (_clientAccessState.IsBlocked)
            {
                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.Blocked,
                    Title = Res("AccessStatusBlockedTitle", "Access blocked"),
                    Detail = Res("AccessStatusBlockedDetail", "This client was blocked by the administrator."),
                    AdminMessage = adminMessage,
                    Severity = "Error"
                };
            }

            var isReadOnly = (_remotePolicy?.ReadOnlyMode ?? false)
                             || (App.AppSettingsService?.Settings.AdminReadOnlyMode ?? false)
                             || (!_hasValidLocalLicense && _clientAccessState.IsExpired);
            if (isReadOnly)
            {
                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.ReadOnly,
                    Title = Res("AccessStatusReadOnlyTitle", "Read-only mode"),
                    Detail = Res("AccessStatusReadOnlyDetail", "The trial period ended. Full access requires activation."),
                    AdminMessage = adminMessage,
                    Severity = "Error",
                    ExpiresAtUtc = _clientAccessState.ExpiresAtUtc,
                    DaysLeft = 0
                };
            }

            if (_hasValidLocalLicense)
            {
                var localDaysLeft = LicenseService.GetDaysLeft();
                var localExpiresUtc = ParseUtc(LicenseService.GetExpiresAt());
                if (localDaysLeft == 99999)
                {
                    return new AccessStatusSnapshot
                    {
                        Mode = AccessStatusMode.Activated,
                        Title = Res("AccessStatusActivatedTitle", "Activated"),
                        Detail = Res("AccessStatusUnlimitedDetail", "Unlimited access is active."),
                        AdminMessage = adminMessage,
                        Severity = "Success"
                    };
                }

                var localExpiresText = FormatLocalDate(localExpiresUtc);
                var detail = $"{Res("LicenseActiveUntil", "Active until")} {localExpiresText}";
                if (localDaysLeft >= 0)
                    detail += $" ({localDaysLeft} {Res("LicenseDaysLeft", "days")})";

                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.Activated,
                    Title = Res("AccessStatusActivatedTitle", "Activated"),
                    Detail = detail,
                    AdminMessage = adminMessage,
                    Severity = "Success",
                    ExpiresAtUtc = localExpiresUtc,
                    DaysLeft = localDaysLeft >= 0 ? localDaysLeft : null
                };
            }

            if (_clientAccessState.HasRemoteAccessWindow && _clientAccessState.ExpiresAtUtc.HasValue)
            {
                var localExpiry = _clientAccessState.ExpiresAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.TrialActive,
                    Title = Res("AccessStatusTrialTitle", "Trial active"),
                    Detail = string.Format(
                        CultureInfo.CurrentCulture,
                        Res("AccessStatusTrialDetail", "{0} days left, until {1}"),
                        _clientAccessState.DaysRemaining,
                        localExpiry),
                    AdminMessage = adminMessage,
                    Severity = "Warning",
                    ExpiresAtUtc = _clientAccessState.ExpiresAtUtc,
                    DaysLeft = _clientAccessState.DaysRemaining
                };
            }

            return new AccessStatusSnapshot
            {
                Mode = AccessStatusMode.Unknown,
                Title = Res("AccessStatusUnknownTitle", "Access status unknown"),
                Detail = Res("AccessStatusUnknownDetail", "Could not determine the current access state."),
                AdminMessage = adminMessage,
                Severity = "Info"
            };
        }

        private static string FormatLocalDate(DateTime? utcDate)
        {
            if (!utcDate.HasValue)
                return "—";

            return utcDate.Value.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
        }

        private static DateTime? ParseUtc(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : null;
        }

        private static string Res(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key) as string ?? fallback;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

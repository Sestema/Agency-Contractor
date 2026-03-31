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
        OfflineGrace,
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
        private LocalLicenseStatus _localLicenseStatus = new();
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

        public void Initialize(LocalLicenseStatus localLicenseStatus, ClientAccessState clientAccessState, RemotePolicy? remotePolicy)
        {
            _localLicenseStatus = localLicenseStatus ?? new LocalLicenseStatus();
            _clientAccessState = clientAccessState ?? new ClientAccessState();
            _remotePolicy = remotePolicy;
            RefreshPresentation();
        }

        public void UpdateRemoteState(ClientAccessState clientAccessState, RemotePolicy? remotePolicy)
        {
            _clientAccessState = clientAccessState ?? new ClientAccessState();
            _remotePolicy = remotePolicy ?? _remotePolicy;
            _localLicenseStatus = LicenseService.GetLocalLicenseStatus();
            RefreshPresentation();
        }

        public void RefreshPresentation()
        {
            var snapshot = BuildSnapshot();
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                Current = snapshot;
                return;
            }

            dispatcher.Invoke(() => Current = snapshot);
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
                             || (_clientAccessState.HasKnownState && _clientAccessState.IsExpired);
            if (isReadOnly)
            {
                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.ReadOnly,
                    Title = Res("AccessStatusReadOnlyTitle", "Read-only mode"),
                    Detail = WithStaleMarker(Res("AccessStatusReadOnlyDetail", "The trial period ended. Full access requires activation.")),
                    AdminMessage = adminMessage,
                    Severity = "Error",
                    ExpiresAtUtc = _clientAccessState.ExpiresAtUtc,
                    DaysLeft = 0
                };
            }

            if (_clientAccessState.IsOfflineGraceActive && _clientAccessState.HasRemoteAccessWindow)
            {
                var cachedExpiry = FormatLocalDate(_clientAccessState.ExpiresAtUtc);
                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.OfflineGrace,
                    Title = Res("AccessStatusOfflineGraceTitle", "Offline access"),
                    Detail = string.Format(
                        CultureInfo.CurrentCulture,
                        Res("AccessStatusOfflineGraceDetail", "Working from the last confirmed access state. Server check is unavailable, {0} day(s) of offline grace remain. Cached access until {1}."),
                        _clientAccessState.OfflineGraceDaysRemaining,
                        cachedExpiry),
                    AdminMessage = adminMessage,
                    Severity = "Warning",
                    ExpiresAtUtc = _clientAccessState.ExpiresAtUtc,
                    DaysLeft = _clientAccessState.DaysRemaining
                };
            }

            if (_clientAccessState.HasRemoteAccessWindow && _clientAccessState.ExpiresAtUtc.HasValue)
            {
                var localExpiry = _clientAccessState.ExpiresAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.CurrentCulture);
                var isTrialWindow = _clientAccessState.DaysRemaining <= 14;
                return new AccessStatusSnapshot
                {
                    Mode = isTrialWindow ? AccessStatusMode.TrialActive : AccessStatusMode.Activated,
                    Title = isTrialWindow
                        ? Res("AccessStatusTrialTitle", "Trial active")
                        : Res("AccessStatusActivatedTitle", "Activated"),
                    Detail = isTrialWindow
                        ? WithStaleMarker(string.Format(
                            CultureInfo.CurrentCulture,
                            Res("AccessStatusTrialDetail", "{0} days left, until {1}"),
                            _clientAccessState.DaysRemaining,
                            localExpiry))
                        : WithStaleMarker($"{Res("LicenseActiveUntil", "Active until")} {localExpiry} ({_clientAccessState.DaysRemaining} {Res("LicenseDaysLeft", "days")})"),
                    AdminMessage = adminMessage,
                    Severity = isTrialWindow ? "Warning" : "Success",
                    ExpiresAtUtc = _clientAccessState.ExpiresAtUtc,
                    DaysLeft = _clientAccessState.DaysRemaining
                };
            }

            if (_localLicenseStatus.IsValid)
            {
                if (_localLicenseStatus.IsUnlimited)
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

                var localExpiresText = FormatLocalDate(_localLicenseStatus.ExpiresAtUtc);
                var detail = $"{Res("LicenseActiveUntil", "Active until")} {localExpiresText}";
                if (_localLicenseStatus.DaysLeft >= 0)
                    detail += $" ({_localLicenseStatus.DaysLeft} {Res("LicenseDaysLeft", "days")})";

                return new AccessStatusSnapshot
                {
                    Mode = AccessStatusMode.Activated,
                    Title = Res("AccessStatusActivatedTitle", "Activated"),
                    Detail = detail,
                    AdminMessage = adminMessage,
                    Severity = "Success",
                    ExpiresAtUtc = _localLicenseStatus.ExpiresAtUtc,
                    DaysLeft = _localLicenseStatus.DaysLeft >= 0 ? _localLicenseStatus.DaysLeft : null
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

        private static string Res(string key, string fallback)
        {
            return Application.Current?.TryFindResource(key) as string ?? fallback;
        }

        private string WithStaleMarker(string detail)
        {
            if (!_clientAccessState.IsStale)
                return detail;

            var suffix = Res("AccessStatusStaleDetailSuffix", "Server confirmation failed, so this cached status may be outdated.");
            return string.IsNullOrWhiteSpace(detail) ? suffix : $"{detail} {suffix}";
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

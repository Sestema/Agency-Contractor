using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class LicenseWindow : Window
    {
        private readonly bool _shutdownOnCloseWithoutAccess;
        private readonly AccessStatusService _accessStatusService;
        public bool IsActivated { get; private set; }
        public ClientAccessState LatestAccessState { get; private set; } = new();

        public LicenseWindow(AccessStatusService accessStatusService, bool shutdownOnCloseWithoutAccess = false, ClientAccessState? initialAccessState = null)
        {
            _accessStatusService = accessStatusService ?? throw new System.ArgumentNullException(nameof(accessStatusService));
            _shutdownOnCloseWithoutAccess = shutdownOnCloseWithoutAccess;
            LatestAccessState = initialAccessState ?? new ClientAccessState();
            InitializeComponent();
            TxtVersion.Text = $"Agency Contractor - {AppSettingsService.CurrentAppVersion}";
            _accessStatusService.UpdateRemoteState(LatestAccessState, LatestAccessState.Policy);
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            TxtMachineId.Text = LicenseService.GetMachineId();
            var snapshot = _accessStatusService.Current;

            if (!snapshot.HasStatus)
                _accessStatusService.RefreshPresentation();

            snapshot = _accessStatusService.Current;
            TxtStatus.Text = snapshot.Title;
            TxtStatusDetail.Text = snapshot.Detail;
            TxtAdminMessage.Text = snapshot.AdminMessage;
            TxtAdminMessage.Visibility = string.IsNullOrWhiteSpace(snapshot.AdminMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;

            TxtStatus.Foreground = snapshot.Severity switch
            {
                "Success" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C)),
                "Warning" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x7F, 0x17)),
                "Error" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F)),
                _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x6C, 0xC0))
            };

            var hasAccess = snapshot.Mode is AccessStatusMode.TrialActive
                or AccessStatusMode.Activated
                or AccessStatusMode.OfflineGrace
                or AccessStatusMode.ReadOnly;

            BtnContinue.IsEnabled = hasAccess;
            IsActivated = hasAccess;

            if (string.IsNullOrWhiteSpace(TxtResult.Text))
            {
                TxtResult.Text = hasAccess
                    ? "Доступ підтверджено. Можна продовжити роботу."
                    : "Якщо доступ ще не підтвердився, зверніться до адміністратора або використайте legacy-активацію лише як запасний варіант.";
                TxtResult.Foreground = hasAccess
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5F, 0x63, 0x68));
            }
        }

        private void BtnActivate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Виберіть файл активації",
                Filter = "Activation Key (*.key)|*.key|All files (*.*)|*.*",
                InitialDirectory = "C:\\"
            };

            if (dlg.ShowDialog() != true) return;

            int days = 0;
            if (CbPlan.SelectedItem is ComboBoxItem item && item.Tag is string tagStr)
                int.TryParse(tagStr, out days);

            var (success, message) = LicenseService.Activate(dlg.FileName, days);
            LatestAccessState = TelemetryService.GetCachedAccessStateSnapshot();
            _accessStatusService.UpdateRemoteState(LatestAccessState, LatestAccessState.Policy);
            TxtResult.Text = message;
            TxtResult.Foreground = success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));

            RefreshStatus();
        }

        private async void BtnRefreshStatus_Click(object sender, RoutedEventArgs e)
        {
            BtnRefreshStatus.IsEnabled = false;
            TxtResult.Text = "Оновлюємо статус доступу...";
            TxtResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2D, 0x6C, 0xC0));

            try
            {
                var accessState = await TelemetryService.GetStartupAccessStateAsync();
                LatestAccessState = accessState;
                _accessStatusService.UpdateRemoteState(accessState, accessState.Policy);
                var snapshot = _accessStatusService.Current;
                var hasAccess = snapshot.Mode is AccessStatusMode.TrialActive
                    or AccessStatusMode.Activated
                    or AccessStatusMode.OfflineGrace
                    or AccessStatusMode.ReadOnly;
                TxtResult.Text = accessState.HasKnownState
                    ? "Статус доступу оновлено."
                    : "Не вдалося отримати підтверджений стан доступу. Перевірте мережу або зверніться до адміністратора.";
                TxtResult.Foreground = hasAccess
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C))
                    : accessState.HasKnownState
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x7F, 0x17))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
                RefreshStatus();
            }
            catch (Exception ex)
            {
                TxtResult.Text = ErrorHandler.NormalizeUserMessage(ex, "Не вдалося оновити статус доступу.");
                TxtResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
            }
            finally
            {
                BtnRefreshStatus.IsEnabled = true;
            }
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            IsActivated = true;
            DialogResult = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_shutdownOnCloseWithoutAccess && !IsActivated)
            {
                Application.Current.Shutdown();
            }
            base.OnClosing(e);
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class LicenseWindow : Window
    {
        public bool IsActivated { get; private set; }

        public LicenseWindow()
        {
            InitializeComponent();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            TxtMachineId.Text = LicenseService.GetMachineId();
            var status = LicenseService.GetLicenseStatus();
            TxtStatus.Text = status;

            var valid = LicenseService.IsLicenseValid();
            TxtStatus.Foreground = valid
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));
            BtnContinue.IsEnabled = valid;
            IsActivated = valid;
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
            TxtResult.Text = message;
            TxtResult.Foreground = success
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x8E, 0x3C))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD3, 0x2F, 0x2F));

            RefreshStatus();
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            IsActivated = true;
            DialogResult = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!IsActivated)
            {
                Application.Current.Shutdown();
            }
            base.OnClosing(e);
        }
    }
}

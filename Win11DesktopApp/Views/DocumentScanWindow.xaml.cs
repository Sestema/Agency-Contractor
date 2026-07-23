using System.ComponentModel;
using System.Windows;
using Win11DesktopApp.Services;
using Win11DesktopApp.Services.Scanning;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class DocumentScanWindow : Window
    {
        private readonly DocumentScanViewModel _viewModel;

        public string? ResultPath { get; private set; }

        public DocumentScanWindow(AppSettingsService appSettingsService)
        {
            InitializeComponent();

            var sessionStore = new ScanSessionStore(new ImageEnhancementService());
            var scannerService = new CompositeScannerService();
            var assemblyService = new ScanDocumentAssemblyService();

            _viewModel = new DocumentScanViewModel(
                appSettingsService,
                sessionStore,
                scannerService,
                assemblyService,
                () => this);

            _viewModel.RequestClose += OnRequestClose;
            DataContext = _viewModel;
            Closing += OnClosing;
            Closed += (_, _) => _viewModel.Cleanup();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_viewModel.IsScanning || _viewModel.IsBusy)
            {
                e.Cancel = true;
                _viewModel.NotifyCloseBlocked();
                return;
            }

            _viewModel.PrepareClose();
        }

        private void OnRequestClose(bool success, string? path)
        {
            if (success)
            {
                ResultPath = path;
                DialogResult = true;
            }
            else
            {
                DialogResult = false;
            }

            Close();
        }
    }
}

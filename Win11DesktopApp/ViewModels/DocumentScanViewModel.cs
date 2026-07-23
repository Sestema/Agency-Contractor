using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Services.Scanning;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public sealed class ScanPageItemViewModel : ViewModelBase
    {
        public ScanPageItemViewModel(ScanPage page)
        {
            Page = page;
        }

        public ScanPage Page { get; }

        public int Order => Page.Order;

        public string ThumbnailPath => Page.ThumbnailPath ?? Page.SourcePath;

        public string Label => $"#{Order}";

        public void RefreshOrder()
        {
            OnPropertyChanged(nameof(Order));
            OnPropertyChanged(nameof(Label));
        }

        public void RefreshThumbnail()
        {
            OnPropertyChanged(nameof(ThumbnailPath));
        }
    }

    public sealed class ScanDeviceItemViewModel : ViewModelBase
    {
        public ScanDeviceItemViewModel(ScannerDeviceInfo device)
        {
            Id = device.Id;
            Provider = device.Provider;
            Name = device.Provider == "TWAIN"
                ? $"{device.Name} (TWAIN)"
                : device.Name;
        }

        public string Id { get; }
        public string Name { get; }
        public string Provider { get; }
    }

    public sealed class DocumentScanViewModel : ViewModelBase, ICleanable
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly IScanSessionStore _sessionStore;
        private readonly IScannerService _scannerService;
        private readonly IScanDocumentAssemblyService _assemblyService;
        private readonly Func<Window?> _ownerProvider;
        private ScanPageItemViewModel? _selectedPage;
        private ScanDeviceItemViewModel? _selectedDevice;
        private int _selectedDpi = 300;
        private ScanColorMode _selectedColorMode = ScanColorMode.Color;
        private ScanSource _selectedSource = ScanSource.Auto;
        private bool _isScanning;
        private bool _isBusy;
        private bool _isClosing;
        private string _statusMessage = string.Empty;
        private string _errorMessage = string.Empty;
        private readonly CancellationTokenSource _lifetimeCts = new();

        public DocumentScanViewModel(
            AppSettingsService appSettingsService,
            IScanSessionStore sessionStore,
            IScannerService scannerService,
            IScanDocumentAssemblyService assemblyService,
            Func<Window?> ownerProvider)
        {
            _appSettingsService = appSettingsService;
            _sessionStore = sessionStore;
            _scannerService = scannerService;
            _assemblyService = assemblyService;
            _ownerProvider = ownerProvider;

            Pages = new ObservableCollection<ScanPageItemViewModel>();
            Devices = new ObservableCollection<ScanDeviceItemViewModel>();
            DpiOptions = new[] { 150, 300, 600 };

            LoadDefaults();

            ScanPageCommand = new AsyncRelayCommand(_ => ScanPageAsync(), _ => CanScan);
            ScanViaDialogCommand = new AsyncRelayCommand(_ => ScanViaDialogAsync(), _ => CanScan);
            RefreshDevicesCommand = new AsyncRelayCommand(_ => LoadDevicesAsync(), _ => !IsBusy && !IsScanning);
            PickDeviceCommand = new AsyncRelayCommand(_ => PickDeviceAsync(), _ => ScannerAvailable && !IsBusy && !IsScanning);
            ImportFromFileCommand = new RelayCommand(_ => ImportFromFile(), _ => !IsBusy);
            EditPageCommand = new RelayCommand(_ => EditSelectedPage(), _ => SelectedPage != null && !IsBusy);
            DeletePageCommand = new RelayCommand(_ => DeleteSelectedPage(), _ => SelectedPage != null && !IsBusy);
            MovePageUpCommand = new RelayCommand(_ => MoveSelectedPage(-1), _ => CanMoveUp);
            MovePageDownCommand = new RelayCommand(_ => MoveSelectedPage(1), _ => CanMoveDown);
            FinishCommand = new AsyncRelayCommand(_ => FinishAsync(), _ => Pages.Count > 0 && !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false, null));

            _ = LoadDevicesAsync();
        }

        public ObservableCollection<ScanPageItemViewModel> Pages { get; }
        public ObservableCollection<ScanDeviceItemViewModel> Devices { get; }
        public int[] DpiOptions { get; }

        public bool ScannerAvailable => _scannerService.IsAvailable;

        public ScanPageItemViewModel? SelectedPage
        {
            get => _selectedPage;
            set
            {
                if (SetProperty(ref _selectedPage, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPage));
                    OnPropertyChanged(nameof(SelectedPreviewPath));
                }
            }
        }

        public ScanDeviceItemViewModel? SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        public int SelectedDpi
        {
            get => _selectedDpi;
            set => SetProperty(ref _selectedDpi, value);
        }

        public ScanColorMode SelectedColorMode
        {
            get => _selectedColorMode;
            set => SetProperty(ref _selectedColorMode, value);
        }

        public ScanSource SelectedSource
        {
            get => _selectedSource;
            set => SetProperty(ref _selectedSource, value);
        }

        public bool IsScanning
        {
            get => _isScanning;
            private set
            {
                if (SetProperty(ref _isScanning, value))
                    OnPropertyChanged(nameof(CanScan));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged(nameof(CanScan));
                    OnPropertyChanged(nameof(CanMoveUp));
                    OnPropertyChanged(nameof(CanMoveDown));
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public bool HasSelectedPage => SelectedPage != null;
        public string? SelectedPreviewPath => SelectedPage?.Page.SourcePath;
        public int PageCount => Pages.Count;
        public bool CanScan => ScannerAvailable && !IsScanning && !IsBusy;
        public bool ShowNoDevicesHint => ScannerAvailable && Devices.Count == 0 && !IsScanning;
        public bool CanMoveUp => SelectedPage != null && Pages.IndexOf(SelectedPage) > 0 && !IsBusy;
        public bool CanMoveDown => SelectedPage != null && Pages.IndexOf(SelectedPage) < Pages.Count - 1 && !IsBusy;

        public ICommand ScanPageCommand { get; }
        public ICommand ScanViaDialogCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand PickDeviceCommand { get; }
        public ICommand ImportFromFileCommand { get; }
        public ICommand EditPageCommand { get; }
        public ICommand DeletePageCommand { get; }
        public ICommand MovePageUpCommand { get; }
        public ICommand MovePageDownCommand { get; }
        public ICommand FinishCommand { get; }
        public ICommand CancelCommand { get; }

        public event Action<bool, string?>? RequestClose;

        private void LoadDefaults()
        {
            var settings = _appSettingsService.Settings;
            SelectedDpi = settings.ScanDefaultDpi is 150 or 300 or 600 ? settings.ScanDefaultDpi : 300;
            SelectedColorMode = Enum.IsDefined(typeof(ScanColorMode), settings.ScanDefaultColorMode)
                ? (ScanColorMode)settings.ScanDefaultColorMode
                : ScanColorMode.Color;
            SelectedSource = Enum.IsDefined(typeof(ScanSource), settings.ScanDefaultSource)
                ? (ScanSource)settings.ScanDefaultSource
                : ScanSource.Auto;
        }

        private void SaveDefaults()
        {
            var settings = _appSettingsService.Settings;
            settings.ScanDefaultDpi = SelectedDpi;
            settings.ScanDefaultColorMode = (int)SelectedColorMode;
            settings.ScanDefaultSource = (int)SelectedSource;
            settings.ScanDefaultDeviceId = SelectedDevice?.Id ?? string.Empty;
            _ = _appSettingsService.SaveSettingsImmediate();
        }

        private async Task LoadDevicesAsync()
        {
            if (!ScannerAvailable)
            {
                StatusMessage = Res("ScanNoScanner");
                return;
            }

            try
            {
                var devices = await _scannerService.GetDevicesAsync(_lifetimeCts.Token);
                Devices.Clear();
                foreach (var device in devices)
                    Devices.Add(new ScanDeviceItemViewModel(device));

                var savedId = _appSettingsService.Settings.ScanDefaultDeviceId;
                SelectedDevice = Devices.FirstOrDefault(d => d.Id == savedId) ?? Devices.FirstOrDefault();
                StatusMessage = Devices.Count > 0
                    ? string.Format(Res("ScanDevicesFound"), Devices.Count)
                    : Res("ScanNoDevicesHint");
                OnPropertyChanged(nameof(ShowNoDevicesHint));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DocumentScanViewModel.LoadDevicesAsync", ex.Message);
                StatusMessage = Res("ScanNoScanner");
            }
        }

        private ScanSettings BuildScanSettings() => new()
        {
            Dpi = SelectedDpi,
            ColorMode = SelectedColorMode,
            Source = SelectedSource,
            DeviceId = SelectedDevice?.Id,
            Provider = SelectedDevice?.Provider ?? "WIA"
        };

        private async Task ScanViaDialogAsync()
        {
            ErrorMessage = string.Empty;
            IsScanning = true;
            StatusMessage = Res("ScanWorking");

            try
            {
                var scannedPath = await _scannerService.ScanViaDialogAsync(_sessionStore.SessionFolder, _lifetimeCts.Token);
                AddPages(_sessionStore.AddPagesFromFile(scannedPath));
                StatusMessage = string.Format(Res("ScanPageAdded"), Pages.Count);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = Res("ScanCancelled");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DocumentScanViewModel.ScanViaDialogAsync", ex);
                ErrorMessage = Res("ScanFailed");
                StatusMessage = string.Empty;
            }
            finally
            {
                IsScanning = false;
                DisposeSessionIfClosing();
            }
        }

        private async Task PickDeviceAsync()
        {
            ErrorMessage = string.Empty;
            StatusMessage = Res("ScanPickDeviceWorking");

            try
            {
                var picked = await _scannerService.PickDeviceViaDialogAsync(_lifetimeCts.Token);
                if (picked == null)
                {
                    StatusMessage = Res("ScanPickDeviceCancelled");
                    return;
                }

                var existing = Devices.FirstOrDefault(d => d.Id == picked.Id);
                if (existing == null)
                {
                    Devices.Add(new ScanDeviceItemViewModel(picked));
                    existing = Devices.Last();
                }

                SelectedDevice = existing;
                _appSettingsService.Settings.ScanDefaultDeviceId = existing.Id;
                _ = _appSettingsService.SaveSettingsImmediate();
                OnPropertyChanged(nameof(ShowNoDevicesHint));
                StatusMessage = string.Format(Res("ScanDeviceSelected"), existing.Name);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = Res("ScanPickDeviceCancelled");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DocumentScanViewModel.PickDeviceAsync", ex.Message);
                ErrorMessage = Res("ScanPickDeviceFailed");
            }
        }

        private async Task ScanPageAsync()
        {
            ErrorMessage = string.Empty;
            IsScanning = true;
            StatusMessage = Res("ScanWorking");

            try
            {
                string scannedPath;
                if (SelectedDevice != null)
                {
                    scannedPath = await _scannerService.ScanToFileAsync(
                        BuildScanSettings(),
                        _sessionStore.SessionFolder,
                        _lifetimeCts.Token);
                }
                else
                {
                    scannedPath = await _scannerService.ScanViaDialogAsync(_sessionStore.SessionFolder, _lifetimeCts.Token);
                }

                AddPages(_sessionStore.AddPagesFromFile(scannedPath));
                StatusMessage = string.Format(Res("ScanPageAdded"), Pages.Count);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = Res("ScanCancelled");
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DocumentScanViewModel.ScanPageAsync", ex);
                ErrorMessage = Res("ScanFailed");
                StatusMessage = string.Empty;
            }
            finally
            {
                IsScanning = false;
                DisposeSessionIfClosing();
            }
        }

        private void ImportFromFile()
        {
            ErrorMessage = string.Empty;
            var dialog = new OpenFileDialog
            {
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf",
                Title = Res("ScanImportTitle")
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                AddPages(_sessionStore.AddPagesFromFile(dialog.FileName));
                StatusMessage = string.Format(Res("ScanPageAdded"), Pages.Count);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DocumentScanViewModel.ImportFromFile", ex);
                ErrorMessage = Res("ScanImportFailed");
            }
        }

        private void AddPages(IReadOnlyList<ScanPage> pages)
        {
            foreach (var page in pages)
            {
                var vm = new ScanPageItemViewModel(page);
                Pages.Add(vm);
            }

            ReindexPages();
            SelectedPage = Pages.LastOrDefault();
            OnPropertyChanged(nameof(PageCount));
            OnPropertyChanged(nameof(ShowNoDevicesHint));
        }

        private void EditSelectedPage()
        {
            if (SelectedPage == null)
                return;

            var owner = _ownerProvider();
            ImageEditorWindow editor;
            try
            {
                editor = new ImageEditorWindow(SelectedPage.Page.SourcePath);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DocumentScanViewModel.EditSelectedPage", ex);
                ErrorMessage = Res("ScanEditFailed");
                return;
            }

            if (owner != null)
                editor.Owner = owner;

            if (editor.ShowDialog() == true && editor.Saved && !string.IsNullOrWhiteSpace(editor.ResultPath))
            {
                try
                {
                    _sessionStore.ReplacePageFile(SelectedPage.Page, editor.ResultPath);
                    SelectedPage.RefreshThumbnail();
                    OnPropertyChanged(nameof(SelectedPreviewPath));
                    StatusMessage = string.Format(Res("ScanPageEdited"), SelectedPage.Order);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("DocumentScanViewModel.EditSelectedPage.Save", ex);
                    ErrorMessage = Res("ScanEditFailed");
                }
            }
        }

        private void DeleteSelectedPage()
        {
            if (SelectedPage == null)
                return;

            var index = Pages.IndexOf(SelectedPage);
            _sessionStore.RemovePage(SelectedPage.Page);
            Pages.RemoveAt(index);
            ReindexPages();
            SelectedPage = Pages.Count == 0
                ? null
                : Pages[Math.Min(index, Pages.Count - 1)];
            OnPropertyChanged(nameof(PageCount));
            StatusMessage = Pages.Count > 0
                ? string.Format(Res("ScanPagesCount"), Pages.Count)
                : Res("ScanNoPages");
        }

        private void MoveSelectedPage(int delta)
        {
            if (SelectedPage == null)
                return;

            var index = Pages.IndexOf(SelectedPage);
            var newIndex = index + delta;
            if (newIndex < 0 || newIndex >= Pages.Count)
                return;

            Pages.Move(index, newIndex);
            ReindexPages();
            OnPropertyChanged(nameof(CanMoveUp));
            OnPropertyChanged(nameof(CanMoveDown));
        }

        private void ReindexPages()
        {
            _sessionStore.Reorder(Pages.Select(p => p.Page).ToList());
            foreach (var page in Pages)
                page.RefreshOrder();
        }

        private async Task FinishAsync()
        {
            if (Pages.Count == 0)
                return;

            ErrorMessage = string.Empty;
            IsBusy = true;
            StatusMessage = Res("ScanExporting");

            try
            {
                SaveDefaults();
                var exportFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AgencyContractor",
                    "ScanExports");
                Directory.CreateDirectory(exportFolder);

                var pagePaths = Pages.OrderBy(p => p.Order).Select(p => p.Page.SourcePath).ToList();
                var resultPath = await _assemblyService.ExportAsync(
                    pagePaths,
                    exportFolder,
                    new ScanExportOptions());

                // Clear busy before close: OnClosing blocks while IsBusy,
                // and RequestClose triggers Close() synchronously.
                IsBusy = false;
                StatusMessage = string.Empty;
                RequestClose?.Invoke(true, resultPath);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("DocumentScanViewModel.FinishAsync", ex);
                ErrorMessage = Res("ScanExportFailed");
                StatusMessage = string.Empty;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void PrepareClose()
        {
            _isClosing = true;
            _lifetimeCts.Cancel();
        }

        public void NotifyCloseBlocked()
        {
            ErrorMessage = string.Empty;
            StatusMessage = IsBusy
                ? Res("ScanExportCloseBlocked")
                : Res("ScanCloseBlocked");
        }

        // Kept for any callers that still use the scan-specific name.
        public void NotifyScanCloseBlocked() => NotifyCloseBlocked();

        public void Cleanup()
        {
            PrepareClose();
            DisposeSessionIfClosing();
        }

        private void DisposeSessionIfClosing()
        {
            if (!_isClosing || IsScanning || IsBusy)
                return;

            try
            {
                _sessionStore.Dispose();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("DocumentScanViewModel.DisposeSessionIfClosing", ex.Message);
            }
        }
    }
}

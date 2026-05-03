using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class CandidateDetailsViewModel : ViewModelBase
    {
        private readonly CandidateService _service;
        private readonly ActivityLogService _activityLogService;
        private readonly string _folder;

        public event Action? RequestClose;

        public ICommand SaveCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand BrowsePhotoCommand { get; }
        public ICommand BrowsePassportCommand { get; }

        private CandidateData _data = new();
        public CandidateData Data
        {
            get => _data;
            set => SetProperty(ref _data, value);
        }

        public string FullName => $"{Data.FirstName} {Data.LastName}";

        private BitmapImage? _photoImage;
        public BitmapImage? PhotoImage
        {
            get => _photoImage;
            set => SetProperty(ref _photoImage, value);
        }

        private string _passportPreviewPath = "";
        public string PassportPreviewPath
        {
            get => _passportPreviewPath;
            set => SetProperty(ref _passportPreviewPath, value);
        }

        private bool _isAllCzech = true;
        public bool IsAllCzech
        {
            get => _isAllCzech;
            set
            {
                if (SetProperty(ref _isAllCzech, value))
                {
                    Data.LocationPreference = value ? "all" : "specific";
                    OnPropertyChanged(nameof(IsSpecificLocation));
                }
            }
        }

        public bool IsSpecificLocation
        {
            get => !_isAllCzech;
            set => IsAllCzech = !value;
        }

        private bool _isDeleteConfirmOpen;
        public bool IsDeleteConfirmOpen
        {
            get => _isDeleteConfirmOpen;
            set => SetProperty(ref _isDeleteConfirmOpen, value);
        }

        public CandidateDetailsViewModel(
            string candidateFolder,
            CandidateService? candidateService = null,
            ActivityLogService? activityLogService = null)
        {
            _service = candidateService ?? throw new InvalidOperationException("CandidateService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _folder = candidateFolder;

            var loaded = _service.LoadData(candidateFolder);
            if (loaded != null)
            {
                Data = loaded;
                IsAllCzech = Data.LocationPreference != "specific";
            }

            LoadImages();

            SaveCommand = new RelayCommand(o => Save());
            DeleteCommand = new RelayCommand(async o =>
            {
                var result = MessageBox.Show(
                    string.Format(Res("CandDeleteConfirm"), FullName),
                    Res("TitleWarning"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    await PerformDeleteAsync();
            });
            CloseCommand = new RelayCommand(o => RequestClose?.Invoke());
            BrowsePhotoCommand = new RelayCommand(o => BrowseAndReplacePhoto());
            BrowsePassportCommand = new RelayCommand(o => BrowseAndReplacePassport());
        }

        private void LoadImages()
        {
            if (!string.IsNullOrEmpty(Data.Files.Photo))
            {
                var photoPath = Path.Combine(_folder, Data.Files.Photo);
                if (File.Exists(photoPath))
                    PhotoImage = LoadBitmap(photoPath);
            }

            if (!string.IsNullOrEmpty(Data.Files.Passport))
            {
                var passPath = Path.Combine(_folder, Data.Files.Passport);
                if (File.Exists(passPath))
                    PassportPreviewPath = passPath;
            }
        }

        private static BitmapImage? LoadBitmap(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = stream;
                bmp.DecodePixelWidth = 200;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex) { LoggingService.LogWarning("CandidateDetailsViewModel.LoadBitmap", $"Load image failed: {ex.Message}"); return null; }
        }

        private void Save()
        {
            _service.SaveData(_folder, Data);
            _activityLogService.Log("CandidateUpdated", "Candidate", "",
                FullName, $"Оновлено кандидата: {FullName}");
            RequestClose?.Invoke();
        }

        private async Task PerformDeleteAsync()
        {
            PhotoImage = null;
            PassportPreviewPath = "";

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var name = FullName;
            await _service.DeleteCandidateAsync(_folder);

            _activityLogService.Log("CandidateDeleted", "Candidate", "",
                name, $"Видалено кандидата: {name}");

            RequestClose?.Invoke();
        }

        private void BrowseAndReplacePhoto()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp",
                Title = Res("CandSelectPhoto")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var ext = Path.GetExtension(dlg.FileName);
                var dest = Path.Combine(_folder, $"photo{ext}");
                SafeFileService.CopyFile(dlg.FileName, dest);
                Data.Files.Photo = $"photo{ext}";
                _service.SaveData(_folder, Data);
                PhotoImage = LoadBitmap(dest);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateDetails.BrowsePhoto", ex);
            }
        }

        private void BrowseAndReplacePassport()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp|PDF|*.pdf|All|*.*",
                Title = Res("CandSelectPassport")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var ext = Path.GetExtension(dlg.FileName);
                var dest = Path.Combine(_folder, $"passport{ext}");
                SafeFileService.CopyFile(dlg.FileName, dest);
                Data.Files.Passport = $"passport{ext}";
                _service.SaveData(_folder, Data);
                PassportPreviewPath = dest;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("CandidateDetails.BrowsePassport", ex);
            }
        }
    }
}

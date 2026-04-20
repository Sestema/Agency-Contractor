using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public class RecentlyDeletedViewModel : ViewModelBase
    {
        private readonly NavigationService _navigationService;
        private readonly RecentlyDeletedService _recentlyDeletedService;
        private readonly CurrentProfileService _currentProfileService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly ActivityLogService _activityLogService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private List<RecentlyDeletedItem> _allItems = new();

        public ICommand GoBackCommand { get; }
        public ICommand ViewProfileCommand { get; }
        public ICommand ArchiveCommand { get; }
        public ICommand ConfirmArchiveCommand { get; }
        public ICommand CancelArchiveCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteForeverCommand { get; }

        private ObservableCollection<RecentlyDeletedItem> _items = new();
        public ObservableCollection<RecentlyDeletedItem> Items
        {
            get => _items;
            set => SetProperty(ref _items, value);
        }

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                    ApplyFilter();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        private int _expiringSoonCount;
        public int ExpiringSoonCount
        {
            get => _expiringSoonCount;
            set => SetProperty(ref _expiringSoonCount, value);
        }

        public int FilteredCount => Items.Count;
        public bool HasItems => Items.Count > 0;

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        private bool _isArchiveDialogOpen;
        public bool IsArchiveDialogOpen
        {
            get => _isArchiveDialogOpen;
            set => SetProperty(ref _isArchiveDialogOpen, value);
        }

        private RecentlyDeletedItem? _employeeToArchive;
        public RecentlyDeletedItem? EmployeeToArchive
        {
            get => _employeeToArchive;
            set => SetProperty(ref _employeeToArchive, value);
        }

        private string _archiveDate = DateTime.Today.ToString("dd.MM.yyyy");
        public string ArchiveDate
        {
            get => _archiveDate;
            set => SetProperty(ref _archiveDate, value);
        }

        private string _archiveStatus = string.Empty;
        public string ArchiveStatus
        {
            get => _archiveStatus;
            set => SetProperty(ref _archiveStatus, value);
        }

        public RecentlyDeletedViewModel(
            RecentlyDeletedService? recentlyDeletedService = null,
            NavigationService? navigationService = null,
            CurrentProfileService? currentProfileService = null,
            ProfileAuthService? profileAuthService = null,
            ActivityLogService? activityLogService = null,
            EmployeeDetailsViewModelFactory? employeeDetailsViewModelFactory = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _recentlyDeletedService = recentlyDeletedService ?? throw new InvalidOperationException("RecentlyDeletedService is not initialized.");
            _currentProfileService = currentProfileService ?? throw new InvalidOperationException("CurrentProfileService is not initialized.");
            _profileAuthService = profileAuthService ?? throw new InvalidOperationException("ProfileAuthService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _employeeDetailsViewModelFactory = employeeDetailsViewModelFactory ?? throw new InvalidOperationException("EmployeeDetailsViewModelFactory is not initialized.");

            GoBackCommand = new RelayCommand(_ => _navigationService.NavigateTo<MainViewModel>());
            ViewProfileCommand = new RelayCommand(o => OpenProfile(o as RecentlyDeletedItem), o => o is RecentlyDeletedItem);
            ArchiveCommand = new RelayCommand(o => OpenArchiveDialog(o as RecentlyDeletedItem), o => o is RecentlyDeletedItem);
            ConfirmArchiveCommand = new AsyncRelayCommand(_ => ConfirmArchiveAsync());
            CancelArchiveCommand = new RelayCommand(_ => IsArchiveDialogOpen = false);
            RestoreCommand = new AsyncRelayCommand(async o => await RestoreAsync(o as RecentlyDeletedItem), o => o is RecentlyDeletedItem);
            DeleteForeverCommand = new AsyncRelayCommand(async o => await DeleteForeverAsync(o as RecentlyDeletedItem), o => o is RecentlyDeletedItem);

            LoadItems();
        }

        private void LoadItems()
        {
            try
            {
                IsLoading = true;
                _allItems = _recentlyDeletedService.GetAllItems();
                TotalCount = _allItems.Count;
                ExpiringSoonCount = _allItems.Count(item => item.DaysRemaining <= 7);
                ApplyFilter();
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyFilter()
        {
            var query = SearchQuery?.Trim() ?? string.Empty;
            var filtered = string.IsNullOrWhiteSpace(query)
                ? _allItems
                : _allItems.Where(item =>
                    item.FullName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.FirmName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.PositionTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            Items = new ObservableCollection<RecentlyDeletedItem>(filtered.OrderByDescending(item => item.DeletedAtUtc));
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(HasItems));
        }

        private async Task RestoreAsync(RecentlyDeletedItem? item)
        {
            if (item == null)
                return;

            var message = string.Format(
                TryL("RecentlyDeletedConfirmRestoreMessage") ?? "Restore employee \"{0}\"?",
                item.FullName);
            var title = TryL("RecentlyDeletedTitle") ?? "Recently Deleted";
            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            IsLoading = true;
            var result = await Task.Run(() => _recentlyDeletedService.RestoreEmployee(item.Id));
            IsLoading = false;

            if (!result.Success)
            {
                MessageBox.Show(
                    string.Format(TryL("RecentlyDeletedRestoreFailed") ?? "Failed to restore employee: {0}", result.Message),
                    TryL("TitleError") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _activityLogService.Log(
                "EmployeeRestoredFromRecentlyDeleted",
                "Employee",
                item.FirmName,
                item.FullName,
                string.Format(TryL("RecentlyDeletedActionRestoredDescription") ?? "Employee {0} was restored from Recently Deleted.", item.FullName),
                employeeFolder: item.OriginalEmployeeFolder);

            ToastService.Instance.Success(string.Format(
                TryL("RecentlyDeletedRestoreSuccess") ?? "Employee {0} was restored.",
                item.FullName));
            ClosePreviewIfMatches(item);
            LoadItems();
        }

        private void OpenProfile(RecentlyDeletedItem? item)
        {
            if (item == null)
                return;

            CleanupDetailsVm();
            EmployeeDetailsVm = _employeeDetailsViewModelFactory.Create(item.FirmName, item.DeletedEmployeeFolder, isReadOnlyMode: true);
            EmployeeDetailsVm.RequestClose += OnDetailsClose;
            IsEmployeeDetailsOpen = true;
        }

        private void CleanupDetailsVm()
        {
            if (EmployeeDetailsVm != null)
                EmployeeDetailsVm.RequestClose -= OnDetailsClose;
        }

        private void OnDetailsClose() => IsEmployeeDetailsOpen = false;

        private void OpenArchiveDialog(RecentlyDeletedItem? item)
        {
            if (item == null)
                return;

            EmployeeToArchive = item;
            ArchiveDate = DateTime.Today.ToString("dd.MM.yyyy");
            ArchiveStatus = string.Empty;
            IsArchiveDialogOpen = true;
        }

        private async Task ConfirmArchiveAsync()
        {
            if (EmployeeToArchive == null)
            {
                ArchiveStatus = TryL("MsgNoEmployeeSelected") ?? "Employee is not selected.";
                return;
            }

            if (string.IsNullOrWhiteSpace(ArchiveDate))
            {
                ArchiveStatus = TryL("MsgEnterArchiveDate") ?? "Enter end of cooperation date.";
                return;
            }

            IsLoading = true;
            var result = await _recentlyDeletedService.ArchiveEmployeeAsync(EmployeeToArchive.Id, ArchiveDate);
            IsLoading = false;

            if (!result.Success)
            {
                ArchiveStatus = string.Format(TryL("RecentlyDeletedArchiveFailed") ?? "Failed to archive employee: {0}", result.Message);
                return;
            }

            _activityLogService.Log(
                "EmployeeArchivedFromRecentlyDeleted",
                "Archive",
                EmployeeToArchive.FirmName,
                EmployeeToArchive.FullName,
                string.Format(TryL("RecentlyDeletedActionArchivedDescription") ?? "Employee {0} was archived from Recently Deleted.", EmployeeToArchive.FullName));

            ToastService.Instance.Success(string.Format(
                TryL("RecentlyDeletedArchiveSuccess") ?? "Employee {0} was moved to archive.",
                EmployeeToArchive.FullName));

            ClosePreviewIfMatches(EmployeeToArchive);
            IsArchiveDialogOpen = false;
            EmployeeToArchive = null;
            LoadItems();
        }

        private async Task DeleteForeverAsync(RecentlyDeletedItem? item)
        {
            if (item == null)
                return;

            var currentProfile = _currentProfileService.CurrentProfile;
            if (currentProfile == null || string.IsNullOrWhiteSpace(currentProfile.ClientId))
            {
                MessageBox.Show(
                    TryL("ConfirmPasswordNoProfile") ?? "User profile was not found. Deletion is blocked.",
                    TryL("ConfirmPasswordTitle") ?? "Confirm password",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var passwordDialog = new ConfirmPasswordWindow
            {
                Owner = Application.Current?.MainWindow
            };

            var confirmed = passwordDialog.ShowDialog() == true && passwordDialog.IsConfirmed;
            if (!confirmed)
                return;

            var authResult = await _profileAuthService.AuthenticateAsync(currentProfile.ClientId, passwordDialog.EnteredPassword);
            if (!authResult.Success)
            {
                MessageBox.Show(
                    TryL("ConfirmPasswordFailed") ?? "Wrong password.",
                    TryL("ConfirmPasswordTitle") ?? "Confirm password",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var message = string.Format(
                TryL("RecentlyDeletedConfirmDeleteForeverMessage") ?? "Delete employee \"{0}\" forever?",
                item.FullName);
            var title = TryL("RecentlyDeletedTitle") ?? "Recently Deleted";
            if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            IsLoading = true;
            var result = await Task.Run(() => _recentlyDeletedService.DeletePermanently(item.Id));
            IsLoading = false;

            if (!result.Success)
            {
                MessageBox.Show(
                    string.Format(TryL("RecentlyDeletedDeleteForeverFailed") ?? "Failed to delete employee forever: {0}", result.Message),
                    TryL("TitleError") ?? "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            ToastService.Instance.Success(string.Format(
                TryL("RecentlyDeletedDeleteForeverSuccess") ?? "Employee {0} was deleted forever.",
                item.FullName));
            ClosePreviewIfMatches(item);
            LoadItems();
        }

        private void ClosePreviewIfMatches(RecentlyDeletedItem? item)
        {
            if (item == null || EmployeeDetailsVm == null)
                return;

            if (!string.Equals(EmployeeDetailsVm.EmployeeFolderPath, item.DeletedEmployeeFolder, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(EmployeeDetailsVm.EmployeeFolderPath, item.OriginalEmployeeFolder, StringComparison.OrdinalIgnoreCase))
                return;

            CleanupDetailsVm();
            EmployeeDetailsVm = null;
            IsEmployeeDetailsOpen = false;
        }

        private static string? TryL(string key)
        {
            try
            {
                return Application.Current.FindResource(key) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}

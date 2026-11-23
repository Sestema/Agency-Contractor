using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One line of a period card: date and what happened.</summary>
    public sealed class HistoryCleanupEventItem
    {
        public string DateText { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string Kind { get; init; } = "other";
    }

    /// <summary>A firm period card in the history cleanup window.</summary>
    public sealed class HistoryCleanupPeriodItem
    {
        public HistoryCleanupPeriod Period { get; init; } = new();
        public string FirmName => Period.FirmName;
        public string DatesText { get; init; } = string.Empty;
        public string DurationText { get; init; } = string.Empty;
        public bool IsCurrent => Period.IsCurrent;
        public bool CanDelete => Period.CanDelete;
        public string BlockReason => Period.BlockReason;
        public List<HistoryCleanupEventItem> Events { get; init; } = new();
        public List<string> MoneyFound => Period.MoneyFound;
        public bool HasMoney => Period.MoneyFound.Count > 0;
        public bool HasNoEvents => Events.Count == 0;
    }

    /// <summary>A card with records about a firm that has no period in the profile.</summary>
    public sealed class HistoryCleanupOrphanItem
    {
        public HistoryCleanupOrphanGroup Group { get; init; } = new();
        public string FirmName => Group.FirmName;
        public List<HistoryCleanupEventItem> Events { get; init; } = new();
        public string CountText { get; init; } = string.Empty;
    }

    public sealed class EmployeeHistoryCleanupViewModel : ViewModelBase
    {
        private readonly EmployeeHistoryCleanupService _service;
        private List<HistoryCleanupPerson> _people = new();
        private int _loadGeneration;

        public EmployeeHistoryCleanupViewModel(EmployeeHistoryCleanupService service)
        {
            _service = service;
            SetFilterCommand = new RelayCommand(o => Filter = o?.ToString() ?? "all");
            AskDeleteCommand = new RelayCommand(o => AskDelete(o as HistoryCleanupPeriodItem));
            AskDeleteOrphansCommand = new RelayCommand(o => AskDeleteOrphans(o as HistoryCleanupOrphanItem));
            CancelDeleteCommand = new RelayCommand(_ => CloseConfirm());
            ConfirmDeleteCommand = new AsyncRelayCommand(_ => ConfirmDeleteAsync(), _ => !IsBusy && _pendingDelete != null);
        }

        public bool IsSupported => _service.IsSupported;
        public bool IsNotSupported => !_service.IsSupported;

        public ObservableCollection<HistoryCleanupPerson> VisiblePeople { get; } = new();
        public ObservableCollection<HistoryCleanupPeriodItem> Periods { get; } = new();
        public ObservableCollection<HistoryCleanupOrphanItem> Orphans { get; } = new();
        public bool HasOrphans => Orphans.Count > 0;

        public ICommand SetFilterCommand { get; }
        public ICommand AskDeleteCommand { get; }
        public ICommand AskDeleteOrphansCommand { get; }
        public ICommand CancelDeleteCommand { get; }
        public ICommand ConfirmDeleteCommand { get; }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    ApplyFilter();
            }
        }

        private string _filter = "all";
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterActive));
                    OnPropertyChanged(nameof(IsFilterArchived));
                    ApplyFilter();
                }
            }
        }

        public bool IsFilterAll => Filter == "all";
        public bool IsFilterActive => Filter == "active";
        public bool IsFilterArchived => Filter == "archived";

        private HistoryCleanupPerson? _selectedPerson;
        public HistoryCleanupPerson? SelectedPerson
        {
            get => _selectedPerson;
            set
            {
                if (SetProperty(ref _selectedPerson, value))
                {
                    OnPropertyChanged(nameof(HasSelectedPerson));
                    OnPropertyChanged(nameof(HasNoSelectedPerson));
                    OnPropertyChanged(nameof(SelectedPersonSubtitle));
                    _ = LoadPeriodsAsync();
                }
            }
        }

        public bool HasSelectedPerson => SelectedPerson != null;
        public bool HasNoSelectedPerson => SelectedPerson == null;

        public string SelectedPersonSubtitle => SelectedPerson == null
            ? string.Empty
            : SelectedPerson.IsArchived
                ? string.Format(Res("HistCleanupArchivedFromFmt") ?? "In the archive · {0}", SelectedPerson.CurrentFirm)
                : string.Format(Res("HistCleanupNowFmt") ?? "Now: {0}", SelectedPerson.CurrentFirm);

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        private string _statusText = string.Empty;
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        // ----- confirmation -----
        private Func<Task<HistoryCleanupResult>>? _pendingDelete;
        private string _pendingFirm = string.Empty;

        private bool _isConfirmOpen;
        public bool IsConfirmOpen
        {
            get => _isConfirmOpen;
            private set => SetProperty(ref _isConfirmOpen, value);
        }

        private string _confirmTitle = string.Empty;
        public string ConfirmTitle
        {
            get => _confirmTitle;
            private set => SetProperty(ref _confirmTitle, value);
        }

        private string _confirmSubtitle = string.Empty;
        public string ConfirmSubtitle
        {
            get => _confirmSubtitle;
            private set => SetProperty(ref _confirmSubtitle, value);
        }

        public ObservableCollection<string> ConfirmLines { get; } = new();

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                _people = await Task.Run(() => _service.LoadPeople());
                ApplyFilter();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeHistoryCleanupViewModel.LoadAsync", ex);
                StatusText = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyFilter()
        {
            var query = SearchText?.Trim() ?? string.Empty;
            var items = _people.Where(person =>
                (Filter == "all" || (Filter == "archived") == person.IsArchived)
                && (query.Length == 0
                    || person.FullName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || person.CurrentFirm.Contains(query, StringComparison.CurrentCultureIgnoreCase)));

            VisiblePeople.Clear();
            foreach (var person in items)
                VisiblePeople.Add(person);
        }

        private async Task LoadPeriodsAsync()
        {
            var generation = ++_loadGeneration;
            Periods.Clear();
            Orphans.Clear();
            OnPropertyChanged(nameof(HasOrphans));
            StatusText = string.Empty;
            var person = SelectedPerson;
            if (person == null)
                return;

            IsBusy = true;
            try
            {
                var details = await Task.Run(() => _service.LoadDetails(person));
                if (generation != _loadGeneration)
                    return;

                foreach (var period in details.Periods)
                    Periods.Add(ToItem(period));
                foreach (var group in details.Orphans)
                    Orphans.Add(ToOrphanItem(group));
                OnPropertyChanged(nameof(HasOrphans));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeHistoryCleanupViewModel.LoadPeriodsAsync", ex);
                StatusText = ex.Message;
            }
            finally
            {
                if (generation == _loadGeneration)
                    IsBusy = false;
            }
        }

        private void AskDelete(HistoryCleanupPeriodItem? item)
        {
            if (item == null || !item.CanDelete)
                return;

            ConfirmLines.Clear();
            ConfirmLines.Add(Res("HistCleanupConfirmPeriod") ?? "The period in the profile");
            if (item.Period.HistoryEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmHistoryFmt") ?? "{0} profile history entries", item.Period.HistoryEntries.Count));
            if (item.Period.ArchiveEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmArchiveFmt") ?? "{0} archive log entries", item.Period.ArchiveEntries.Count));
            if (item.Period.ActivityEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmActivityFmt") ?? "{0} activity log entries", item.Period.ActivityEntries.Count));
            if (item.Period.SalaryRows.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmSalaryFmt") ?? "{0} empty salary rows", item.Period.SalaryRows.Count));
            var person = SelectedPerson;
            if (person == null)
                return;
            OpenConfirm(
                string.Format(Res("HistCleanupConfirmTitleFmt") ?? "Delete the period in {0}?", item.FirmName),
                string.Format(Res("HistCleanupConfirmSubtitleFmt") ?? "{0} · {1}. The program will no longer see that the person was in this firm.",
                    person.FullName, item.DatesText),
                item.FirmName,
                () => _service.DeletePeriodAsync(person, item.Period.Entry));
        }

        private void AskDeleteOrphans(HistoryCleanupOrphanItem? item)
        {
            var person = SelectedPerson;
            if (item == null || person == null)
                return;

            ConfirmLines.Clear();
            if (item.Group.HistoryEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmHistoryFmt") ?? "{0} profile history entries", item.Group.HistoryEntries.Count));
            if (item.Group.ArchiveEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmArchiveFmt") ?? "{0} archive log entries", item.Group.ArchiveEntries.Count));
            if (item.Group.ActivityEntries.Count > 0)
                ConfirmLines.Add(string.Format(Res("HistCleanupConfirmActivityFmt") ?? "{0} activity log entries", item.Group.ActivityEntries.Count));
            OpenConfirm(
                string.Format(Res("HistCleanupOrphanConfirmTitleFmt") ?? "Delete the records about {0}?", item.FirmName),
                string.Format(Res("HistCleanupOrphanConfirmSubtitleFmt") ?? "{0}. Only log records are removed; the profile and the payroll stay as they are.", person.FullName),
                item.FirmName,
                () => _service.DeleteOrphansAsync(person, item.FirmName));
        }

        private void OpenConfirm(string title, string subtitle, string firm, Func<Task<HistoryCleanupResult>> action)
        {
            ConfirmTitle = title;
            ConfirmSubtitle = subtitle;
            _pendingFirm = firm;
            _pendingDelete = action;
            IsConfirmOpen = true;
            CommandManager.InvalidateRequerySuggested();
        }

        private void CloseConfirm()
        {
            _pendingDelete = null;
            _pendingFirm = string.Empty;
            IsConfirmOpen = false;
            ConfirmLines.Clear();
        }

        private async Task ConfirmDeleteAsync()
        {
            var action = _pendingDelete;
            var firm = _pendingFirm;
            if (action == null)
                return;
            if (!PolicyService.EnsureWriteAllowed(Res("HistCleanupTitle") ?? "History cleanup"))
                return;

            IsBusy = true;
            try
            {
                var result = await action();
                CloseConfirm();
                if (result.Success)
                {
                    ToastService.Instance.Success(string.Format(Res("HistCleanupDoneFmt") ?? "Removed: {0}", firm));
                    StatusText = string.Format(Res("HistCleanupBackupFmt") ?? "Backup: {0}", result.BackupPath);
                }
                else
                {
                    ToastService.Instance.Warning(result.Message);
                    StatusText = result.Message;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeHistoryCleanupViewModel.ConfirmDeleteAsync", ex);
                ToastService.Instance.Error(ex.Message);
                StatusText = ex.Message;
                CloseConfirm();
            }
            finally
            {
                IsBusy = false;
            }

            await LoadPeriodsAsync();
        }

        private static HistoryCleanupEventItem ToEventItem(HistoryCleanupEvent item) => new()
        {
            DateText = item.Kind == "salary"
                ? item.When.ToString("MM.yyyy", CultureInfo.InvariantCulture)
                : item.When.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture),
            Text = item.Text,
            Kind = item.Kind
        };

        private static HistoryCleanupOrphanItem ToOrphanItem(HistoryCleanupOrphanGroup group) => new()
        {
            Group = group,
            Events = group.Events.Select(ToEventItem).ToList(),
            CountText = string.Format(Res("HistCleanupOrphanCountFmt") ?? "{0} records",
                group.HistoryEntries.Count + group.ActivityEntries.Count + group.ArchiveEntries.Count)
        };

        private static HistoryCleanupPeriodItem ToItem(HistoryCleanupPeriod period)
        {
            var start = DateParsingHelper.TryParseDate(period.Entry.StartDate ?? string.Empty);
            var end = DateParsingHelper.TryParseDate(period.Entry.EndDate ?? string.Empty);
            var startText = start?.ToString("dd.MM.yyyy") ?? period.Entry.StartDate;
            var datesText = period.IsCurrent
                ? string.Format(Res("HistCleanupFromNowFmt") ?? "{0} — now", startText)
                : $"{startText} — {end?.ToString("dd.MM.yyyy") ?? period.Entry.EndDate}";

            var duration = string.Empty;
            if (start != null && end != null && end.Value >= start.Value)
            {
                duration = DashboardViewModel.FormatWorkedDuration(start.Value, end.Value,
                    Res("DashMovementYearsShort") ?? "р.",
                    Res("DashMovementMonthsShort") ?? "міс.",
                    Res("DashMovementDaysShort") ?? "дн.");
            }

            return new HistoryCleanupPeriodItem
            {
                Period = period,
                DatesText = datesText,
                DurationText = duration,
                Events = period.Events.Select(ToEventItem).ToList()
            };
        }
    }
}

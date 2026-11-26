using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>"Вільні позиції": what firms need, who was found and what is still missing.</summary>
    public class VacanciesViewModel : ViewModelBase, ICleanable
    {
        public const string FilterActive = "active";
        public const string FilterClosed = "closed";
        public const string FilterAll = "all";

        private readonly VacancyService _service;
        private readonly EmployeeService _employeeService;
        private readonly CompanyService _companyService;
        private readonly NavigationService _navigationService;
        private readonly GeminiApiService? _gemini;
        private readonly Dictionary<string, List<EmployeeSummary>> _employeesByFirm = new(StringComparer.OrdinalIgnoreCase);
        private List<VacancyItemViewModel> _all = new();
        private int _loadGeneration;

        public VacanciesViewModel(
            VacancyService? vacancyService = null,
            EmployeeService? employeeService = null,
            CompanyService? companyService = null,
            NavigationService? navigationService = null,
            GeminiApiService? geminiApiService = null)
        {
            _gemini = geminiApiService;
            _service = vacancyService ?? throw new InvalidOperationException("VacancyService is not initialized.");
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");

            GoBackCommand = new RelayCommand(_ => _navigationService.NavigateTo<MainViewModel>());
            RefreshCommand = new RelayCommand(_ => _ = LoadAsync());
            SetFilterCommand = new RelayCommand(o => Filter = o as string ?? FilterActive);
            AddCommand = new RelayCommand(_ => OpenEditor(null));
            EditCommand = new RelayCommand(_ => { if (Selected != null) OpenEditor(Selected); });
            SaveEditorCommand = new RelayCommand(_ => SaveEditor());
            CancelEditorCommand = new RelayCommand(_ => CloseEditor());
            DeleteCommand = new RelayCommand(_ => DeleteSelected());
            ToggleClosedCommand = new RelayCommand(_ => SetStatus(Selected?.IsClosed == true ? VacancyStatus.Open : VacancyStatus.Closed));
            TogglePausedCommand = new RelayCommand(_ => SetStatus(Selected?.IsPaused == true ? VacancyStatus.Open : VacancyStatus.Paused));
            CopyAdCommand = new RelayCommand(_ => CopyAd());
            RemovePersonCommand = new RelayCommand(o => RemovePerson(o as VacancyFoundRow));
            SaveContactCommand = new RelayCommand(_ => SaveContact());
            ExportPosterCommand = new RelayCommand(_ => ExportPoster(), _ => Selected != null);
            ExportListCommand = new RelayCommand(_ => ExportList());

            _ = LoadAsync();
        }

        public ICommand GoBackCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand SaveEditorCommand { get; }
        public ICommand CancelEditorCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand ToggleClosedCommand { get; }
        public ICommand TogglePausedCommand { get; }
        public ICommand CopyAdCommand { get; }
        public ICommand RemovePersonCommand { get; }
        public ICommand SaveContactCommand { get; }
        public ICommand ExportPosterCommand { get; }
        public ICommand ExportListCommand { get; }


        public ObservableCollection<VacancyItemViewModel> Items { get; } = new();

        private VacancyItemViewModel? _selected;
        public VacancyItemViewModel? Selected
        {
            get => _selected;
            set
            {
                if (!SetProperty(ref _selected, value))
                    return;
                OnPropertyChanged(nameof(HasSelected));
                LoadFirmNote();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelected => Selected != null;
        public bool HasItems => Items.Count > 0;

        private string _filter = FilterActive;
        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    ApplyFilter();
            }
        }

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

        private string _summaryText = string.Empty;
        public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; private set => SetProperty(ref _isLoading, value); }

        // ===== details panel =====
        private string _firmNote = string.Empty;
        /// <summary>Note of the selected firm, shown on the page; edited in the position window.</summary>
        public string FirmNote
        {
            get => _firmNote;
            private set
            {
                if (SetProperty(ref _firmNote, value))
                    OnPropertyChanged(nameof(HasFirmNote));
            }
        }
        public bool HasFirmNote => !string.IsNullOrWhiteSpace(FirmNote);


        // ===== contact for the PDF (shared by all PCs) =====
        private string _contactPhone = string.Empty;
        public string ContactPhone { get => _contactPhone; set => SetProperty(ref _contactPhone, value); }
        private string _contactNote = string.Empty;
        public string ContactNote { get => _contactNote; set => SetProperty(ref _contactNote, value); }
        private string _agencyName = string.Empty;
        public string AgencyName { get => _agencyName; set => SetProperty(ref _agencyName, value); }

        private bool _isContactOpen;
        public bool IsContactOpen { get => _isContactOpen; set => SetProperty(ref _isContactOpen, value); }

        // ===== editor =====
        private VacancyEditorViewModel? _editor;
        public VacancyEditorViewModel? Editor { get => _editor; private set => SetProperty(ref _editor, value); }
        public bool IsEditorOpen => Editor != null;

        public void Cleanup()
        {
            Interlocked.Increment(ref _loadGeneration);
        }

        private async Task LoadAsync(string? selectId = null)
        {
            var generation = Interlocked.Increment(ref _loadGeneration);
            IsLoading = true;
            selectId ??= Selected?.Id;
            try
            {
                var snapshot = await Task.Run(() =>
                {
                    var vacancies = _service.GetAll();
                    var employees = new Dictionary<string, List<EmployeeSummary>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var firm in vacancies.Select(v => v.FirmName).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        try
                        {
                            employees[firm] = _employeeService.GetEmployeesForFirm(firm, allowIndexRepair: false) ?? new List<EmployeeSummary>();
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogWarning("VacanciesViewModel.LoadAsync", $"Employees of '{firm}' not loaded: {ex.Message}");
                            employees[firm] = new List<EmployeeSummary>();
                        }
                    }
                    var settings = _service.GetPageSettings();
                    return (vacancies, employees, settings);
                }).ConfigureAwait(true);

                if (generation != Volatile.Read(ref _loadGeneration))
                    return;

                _employeesByFirm.Clear();
                foreach (var pair in snapshot.employees)
                    _employeesByFirm[pair.Key] = pair.Value;

                ContactPhone = snapshot.settings.ContactPhone;
                ContactNote = snapshot.settings.ContactNote;
                AgencyName = snapshot.settings.AgencyName;

                _all = snapshot.vacancies.Select(v => new VacancyItemViewModel(v)).ToList();
                Recompute();
                ApplyFilter(selectId);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.LoadAsync", ex);
                ToastService.Instance.Warning(Res("VacLoadError"));
            }
            finally
            {
                if (generation == Volatile.Read(ref _loadGeneration))
                    IsLoading = false;
            }
        }

        /// <summary>Recounts who was found, per firm, from the employees already loaded.</summary>
        private void Recompute()
        {
            foreach (var firmGroup in _all.GroupBy(item => item.FirmName, StringComparer.OrdinalIgnoreCase))
            {
                var employees = _employeesByFirm.TryGetValue(firmGroup.Key, out var list) ? list : new List<EmployeeSummary>();
                var progress = VacancyMatcher.Compute(firmGroup.Select(item => item.Model), employees.Select(ToMatchEmployee));
                foreach (var item in firmGroup)
                    item.Apply(item.Model, progress.TryGetValue(item.Id, out var p) ? p : null);
            }

            var active = _all.Where(item => !item.IsClosed).ToList();
            SummaryText = string.Format(Res("VacSummaryFmt"), active.Count, active.Sum(item => item.Remaining));
        }

        internal static VacancyEmployee ToMatchEmployee(EmployeeSummary employee)
        {
            var start = employee.ParsedStartDate;
            if (start == null && DateTime.TryParse(employee.StartDate, CultureInfo.GetCultureInfo("cs-CZ"), DateTimeStyles.None, out var parsed))
                start = parsed;
            var name = string.IsNullOrWhiteSpace(employee.FullName) ? $"{employee.FirstName} {employee.LastName}".Trim() : employee.FullName;
            return new VacancyEmployee(employee.UniqueId, name, employee.Gender, start);
        }

        private void ApplyFilter(string? selectId = null)
        {
            selectId ??= Selected?.Id;
            var query = (SearchText ?? string.Empty).Trim();
            var filtered = _all
                .Where(item => Filter switch
                {
                    FilterClosed => item.IsClosed,
                    FilterAll => true,
                    _ => !item.IsClosed
                })
                .Where(item => query.Length == 0
                    || item.FirmName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || item.Position.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || item.City.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(item => item.IsClosed)
                .ThenBy(item => item.IsFilled)
                .ThenByDescending(item => item.Model.CreatedAt)
                .ToList();

            Items.Clear();
            foreach (var item in filtered)
                Items.Add(item);
            OnPropertyChanged(nameof(HasItems));

            var keep = filtered.FirstOrDefault(item => item.Id == selectId) ?? filtered.FirstOrDefault();
            if (!ReferenceEquals(keep, _selected))
                Selected = keep;
        }

        // ===== editor =====

        private List<string> FirmNames()
            => _companyService.Companies
                .Where(company => _companyService.IsCompanyVisible(company))
                .Select(company => company.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        private Dictionary<string, IReadOnlyList<VacancyEditorViewModel.FirmPosition>> FirmPositions()
        {
            var result = new Dictionary<string, IReadOnlyList<VacancyEditorViewModel.FirmPosition>>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in _companyService.Companies)
            {
                if (string.IsNullOrWhiteSpace(company.Name) || result.ContainsKey(company.Name))
                    continue;
                result[company.Name] = (company.Positions ?? new ObservableCollection<Position>())
                    .Where(position => !string.IsNullOrWhiteSpace(position.Title))
                    .Select(position => new VacancyEditorViewModel.FirmPosition(position.Title.Trim(), position.HourlySalary))
                    .ToList();
            }
            return result;
        }

        private void OpenEditor(VacancyItemViewModel? item)
        {
            if (!PolicyService.EnsureWriteAllowed(Res("VacEditorNewTitle")))
                return;
            var firms = FirmNames();
            if (item != null && !firms.Contains(item.FirmName, StringComparer.OrdinalIgnoreCase))
                firms.Insert(0, item.FirmName);
            Editor = new VacancyEditorViewModel(item?.Model, firms, FirmPositions(), WriteWithAiAsync, ReadFirmNote);
            if (item == null && _companyService.SelectedCompany?.Name is { Length: > 0 } current && firms.Contains(current))
                Editor.FirmName = current;
            OnPropertyChanged(nameof(IsEditorOpen));
        }

        private void CloseEditor()
        {
            Editor = null;
            OnPropertyChanged(nameof(IsEditorOpen));
        }

        private void SaveEditor()
        {
            if (Editor == null)
                return;
            var existing = _all.FirstOrDefault(item => item.Id == Editor.Id)?.Model;
            var target = existing ?? new Vacancy { Id = Editor.Id };
            if (!Editor.TryApply(target))
                return;
            if (!TrySave(target))
                return;
            if (Editor.IsFirmNoteChanged)
            {
                try
                {
                    _service.SaveFirmNote(target.FirmName, Editor.FirmNote ?? string.Empty);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("VacanciesViewModel.SaveFirmNote", ex);
                    ToastService.Instance.Warning(Res("VacSaveError"));
                }
            }
            var id = target.Id;
            CloseEditor();
            LoadFirmNote();
            if (Filter == FilterClosed && target.Status != VacancyStatus.Closed)
                _filter = FilterActive;
            OnPropertyChanged(nameof(Filter));
            _ = LoadAsync(id);
        }

        private bool TrySave(Vacancy vacancy)
        {
            try
            {
                _service.Save(vacancy);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.Save", ex);
                ToastService.Instance.Warning(Res("VacSaveError"));
                return false;
            }
        }

        private void SaveSelectedAndRefresh()
        {
            if (Selected == null || !TrySave(Selected.Model))
                return;
            Recompute();
            ApplyFilter(Selected.Id);
        }

        private void DeleteSelected()
        {
            if (Selected == null || !PolicyService.EnsureWriteAllowed(Res("VacDelete")))
                return;
            var answer = MessageBox.Show(string.Format(Res("VacDeleteConfirmFmt"), Selected.Title), Res("TitleWarning"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;
            try
            {
                _service.Delete(Selected.Id);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.Delete", ex);
                ToastService.Instance.Warning(Res("VacSaveError"));
                return;
            }
            _all.RemoveAll(item => item.Id == Selected.Id);
            Selected = null;
            Recompute();
            ApplyFilter();
        }

        private void SetStatus(string status)
        {
            if (Selected == null)
                return;
            Selected.Model.Status = status;
            Selected.Model.ClosedAt = status == VacancyStatus.Closed ? DateTime.Now : null;
            SaveSelectedAndRefresh();
        }

        // ===== found people =====

        private void RemovePerson(VacancyFoundRow? row)
        {
            if (Selected == null || row == null)
                return;
            var model = Selected.Model;
            if (row.IsAuto)
            {
                if (!model.ExcludedEmployeeIds.Contains(row.Person.EmployeeId, StringComparer.OrdinalIgnoreCase))
                    model.ExcludedEmployeeIds.Add(row.Person.EmployeeId);
            }
            else
            {
                model.ManualFound.RemoveAll(person => person.Id == row.Person.ManualId);
            }
            SaveSelectedAndRefresh();
        }

        // ===== advert =====

        /// <summary>Asks the AI for a poster description and a Facebook post. Null when it could not.</summary>
        private async Task<(string Description, string Ad)?> WriteWithAiAsync(Vacancy vacancy, string draft)
        {
            if (_gemini == null || !_gemini.IsConfigured)
            {
                ToastService.Instance.Warning(Res("AIChatNoModel"));
                return null;
            }
            try
            {
                var request = VacancyAiWriter.BuildRequest(vacancy, draft, ContactPhone ?? string.Empty, ContactNote ?? string.Empty);
                var answer = await _gemini.ChatAsync(request, VacancyAiWriter.SystemPrompt);
                if (GeminiApiService.IsFailureResponse(answer))
                {
                    LoggingService.LogWarning("VacanciesViewModel.WriteWithAiAsync", answer);
                    ToastService.Instance.Warning(Res(VacancyAiWriter.ErrorKey(answer)));
                    return null;
                }
                var result = VacancyAiWriter.ParseAnswer(answer);
                return string.IsNullOrWhiteSpace(result.Ad) && string.IsNullOrWhiteSpace(result.Description) ? null : result;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.WriteWithAiAsync", ex);
                ToastService.Instance.Warning(Res("VacAiError"));
                return null;
            }
        }

        internal static string ComposeAd(Vacancy v, VacancyItemViewModel? progress, string phone, string phoneNote)
        {
            var sb = new StringBuilder();
            var place = string.IsNullOrWhiteSpace(v.City) ? string.Empty : $", {v.City}";
            var who = PeopleText(v, progress);
            var position = string.IsNullOrWhiteSpace(v.Position) ? Res("VacAdWork") : v.Position;
            sb.AppendLine(string.Format(Res("VacAdHeadFmt"), who, position, place).Trim());
            if (!string.IsNullOrWhiteSpace(v.Pay))
                sb.AppendLine(string.Format(Res("VacAdPayFmt"), v.Pay));
            var from = VacancyItemViewModel.FormatDate(v.NeededFrom);
            if (from.Length > 0)
                sb.AppendLine(string.Format(Res("VacAdFromFmt"), from));
            foreach (var line in SplitLines(v.Conditions))
                sb.AppendLine("✔ " + line);
            if (!string.IsNullOrWhiteSpace(v.Description))
            {
                sb.AppendLine();
                sb.AppendLine(v.Description.Trim());
            }
            if (!string.IsNullOrWhiteSpace(phone))
            {
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(phoneNote) ? $"📞 {phone}" : $"📞 {phone} ({phoneNote})");
            }
            return sb.ToString().TrimEnd();
        }

        private static string PeopleText(Vacancy v, VacancyItemViewModel? progress)
        {
            var parts = new List<string>();
            if (v.Men > 0) parts.Add(string.Format(Res("VacChipMenFmt"), v.Men));
            if (v.Women > 0) parts.Add(string.Format(Res("VacChipWomenFmt"), v.Women));
            if (v.Couples > 0) parts.Add(string.Format(Res("VacChipCouplesFmt"), v.Couples));
            if (v.Anyone > 0) parts.Add(string.Format(Res("VacChipAnyoneFmt"), v.Anyone));
            return string.Join(", ", parts);
        }

        internal static List<string> SplitLines(string? text)
            => (text ?? string.Empty).Split('\n')
                .Select(line => line.Trim().TrimStart('•', '-', '✔', '✓').Trim())
                .Where(line => line.Length > 0)
                .ToList();

        private void CopyAd()
        {
            var text = Selected == null ? string.Empty
                : string.IsNullOrWhiteSpace(Selected.AdText) ? ComposeAd(Selected.Model, Selected, ContactPhone, ContactNote)
                : Selected.AdText;
            if (string.IsNullOrWhiteSpace(text))
                return;
            try
            {
                Clipboard.SetText(text);
                ToastService.Instance.Success(Res("VacAdCopied"));
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("VacanciesViewModel.CopyAd", ex.Message);
            }
        }

        // ===== firm note and contact =====

        private string ReadFirmNote(string firmName)
        {
            try
            {
                return _service.GetFirmNote(firmName);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("VacanciesViewModel.ReadFirmNote", ex.Message);
                return string.Empty;
            }
        }

        private void LoadFirmNote()
        {
            try
            {
                FirmNote = Selected == null ? string.Empty : _service.GetFirmNote(Selected.FirmName);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("VacanciesViewModel.LoadFirmNote", ex.Message);
                FirmNote = string.Empty;
            }
        }

        private void SaveContact()
        {
            try
            {
                _service.SavePageSettings(new VacancyPageSettings
                {
                    ContactPhone = (ContactPhone ?? string.Empty).Trim(),
                    ContactNote = (ContactNote ?? string.Empty).Trim(),
                    AgencyName = (AgencyName ?? string.Empty).Trim()
                });
                IsContactOpen = false;
                ToastService.Instance.Success(Res("VacSaved"));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.SaveContact", ex);
                ToastService.Instance.Warning(Res("VacSaveError"));
            }
        }

        // ===== PDF =====

        private VacancyPageSettings Contact() => new()
        {
            ContactPhone = ContactPhone ?? string.Empty,
            ContactNote = ContactNote ?? string.Empty,
            AgencyName = AgencyName ?? string.Empty
        };

        private static VacancyPdfLabels PdfLabels() => new()
        {
            Urgent = Res("VacPdfUrgent"),
            FromFmt = Res("VacPdfFromFmt"),
            MenFmt = Res("VacChipMenFmt"),
            WomenFmt = Res("VacChipWomenFmt"),
            CouplesFmt = Res("VacChipCouplesFmt"),
            AnyoneFmt = Res("VacChipAnyoneFmt"),
            ListTitle = Res("VacPdfListTitle"),
            NeedFmt = Res("VacPdfNeedFmt"),
            Conditions = Res("VacPdfConditions"),
            Contact = Res("VacPdfContact")
        };

        /// <summary>Public view of a position: firm name and notes stay out; people count = still needed.</summary>
        internal static VacancyPdfItem ToPdfItem(VacancyItemViewModel item)
        {
            var v = item.Model;
            // Still missing people; before anybody is found that is the full order.
            return new VacancyPdfItem
            {
                FirmName = v.FirmName,
                Position = string.IsNullOrWhiteSpace(v.Position) ? Res("VacAdWork") : v.Position,
                City = v.City,
                Pay = v.Pay,
                NeededFrom = VacancyItemViewModel.FormatDate(v.NeededFrom),
                Description = v.Description,
                Conditions = SplitLines(v.Conditions),
                Men = item.MenLeft,
                Women = item.WomenLeft,
                Couples = item.CouplesLeft,
                Anyone = item.AnyoneLeft
            };
        }

        private void ExportPoster()
        {
            if (Selected == null)
                return;
            var name = VacancyService.SafeFileName(string.IsNullOrWhiteSpace(Selected.Position) ? Selected.FirmName : Selected.Position);
            var path = AskPdfPath($"{Res("VacPdfFilePrefix")} {name}.pdf");
            if (path == null)
                return;
            RunExport(path, () => VacancyPdfExportService.GeneratePoster(path, ToPdfItem(Selected), Contact(), PdfLabels()));
        }

        private void ExportList()
        {
            var items = _all.Where(item => !item.IsClosed && !item.IsPaused && item.Remaining > 0)
                .OrderBy(item => item.Position, StringComparer.CurrentCultureIgnoreCase)
                .Select(ToPdfItem)
                .ToList();
            if (items.Count == 0)
            {
                ToastService.Instance.Warning(Res("VacPdfNothing"));
                return;
            }
            var path = AskPdfPath($"{Res("VacPdfListTitle")} {DateTime.Now:yyyy-MM-dd}.pdf");
            if (path == null)
                return;
            var subtitle = DateTime.Now.ToString("MMMM yyyy", CultureInfo.CurrentUICulture);
            RunExport(path, () => VacancyPdfExportService.GenerateList(path, items, Contact(), PdfLabels(), subtitle));
        }

        private static string? AskPdfPath(string fileName)
        {
            var dialog = new SaveFileDialog
            {
                FileName = fileName,
                DefaultExt = ".pdf",
                Filter = "PDF (*.pdf)|*.pdf"
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static void RunExport(string path, Action generate)
        {
            try
            {
                generate();
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (IOException ex)
            {
                LoggingService.LogWarning("VacanciesViewModel.Export", ex.Message);
                ToastService.Instance.Warning(Res("VacPdfFileBusy"));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("VacanciesViewModel.Export", ex);
                ToastService.Instance.Warning(Res("VacPdfError"));
            }
        }
    }
}

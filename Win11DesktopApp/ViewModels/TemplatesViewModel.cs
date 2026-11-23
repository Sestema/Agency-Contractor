using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TemplatesViewModel : ViewModelBase, ICleanable
    {
        private readonly NavigationService _navigationService;
        private readonly TemplateService _templateService;
        private readonly TemplateViewModelFactory _templateViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly CompanyService _companyService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private int _loadGeneration;
        private ObservableCollection<TemplateEntry> _templates = new ObservableCollection<TemplateEntry>();
        public ObservableCollection<TemplateEntry> Templates
        {
            get => _templates;
            set { SetProperty(ref _templates, value); OnPropertyChanged(nameof(HasTemplates)); }
        }
        public bool HasTemplates => Templates.Count > 0;

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _firmName;
        public string Title => string.Format(GetString("TitleTemplates") ?? "Шаблони: {0}", _firmName);

        public ICommand AddTemplateCommand { get; }
        public ICommand GoBackCommand { get; }
        public ICommand DeleteTemplateCommand { get; }

        private bool _isAddDialogOpen;
        public bool IsAddDialogOpen
        {
            get => _isAddDialogOpen;
            set => SetProperty(ref _isAddDialogOpen, value);
        }

        private AddTemplateViewModel? _addTemplateVm;
        public AddTemplateViewModel? AddTemplateVm
        {
            get => _addTemplateVm;
            set => SetProperty(ref _addTemplateVm, value);
        }

        public ICommand CloseAddDialogCommand { get; }
        public ICommand EditTemplateCommand { get; }
        public ICommand RenameTemplateCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand CopyTemplateToCompanyCommand { get; }
        public ICommand ConfirmCopyTemplateCommand { get; }
        public ICommand CancelCopyTemplateCommand { get; }

        private bool _isRenameDialogOpen;
        public bool IsRenameDialogOpen
        {
            get => _isRenameDialogOpen;
            set => SetProperty(ref _isRenameDialogOpen, value);
        }

        private string _renameText = string.Empty;
        public string RenameText
        {
            get => _renameText;
            set => SetProperty(ref _renameText, value);
        }

        private TemplateEntry? _renamingTemplate;

        private bool _isCopyDialogOpen;
        public bool IsCopyDialogOpen
        {
            get => _isCopyDialogOpen;
            set => SetProperty(ref _isCopyDialogOpen, value);
        }

        private List<CopyTargetFirmItem> _copyFirmItems = new();
        private Dictionary<string, HashSet<string>> _copyFirmTemplateNames = new(System.StringComparer.OrdinalIgnoreCase);
        private int _copyDialogGeneration;

        private ObservableCollection<CopyTargetFirmItem> _visibleCopyFirms = new();
        public ObservableCollection<CopyTargetFirmItem> VisibleCopyFirms
        {
            get => _visibleCopyFirms;
            private set => SetProperty(ref _visibleCopyFirms, value);
        }

        private string _copyFirmSearch = string.Empty;
        public string CopyFirmSearch
        {
            get => _copyFirmSearch;
            set
            {
                if (SetProperty(ref _copyFirmSearch, value))
                    ApplyCopyFirmFilter();
            }
        }

        private string _copyTemplateName = string.Empty;
        public string CopyTemplateName
        {
            get => _copyTemplateName;
            set
            {
                if (SetProperty(ref _copyTemplateName, value))
                    RefreshSameNameMarks();
            }
        }

        private string _copySourceText = string.Empty;
        public string CopySourceText
        {
            get => _copySourceText;
            private set => SetProperty(ref _copySourceText, value);
        }

        private string _copySourceName = string.Empty;
        public string CopySourceName
        {
            get => _copySourceName;
            private set => SetProperty(ref _copySourceName, value);
        }

        private bool _isCopying;
        public bool IsCopying
        {
            get => _isCopying;
            private set => SetProperty(ref _isCopying, value);
        }

        public int SelectedCopyFirmCount => _copyFirmItems.Count(item => item.IsSelected);
        public bool HasNoVisibleCopyFirms => VisibleCopyFirms.Count == 0;
        public bool CanCopyTemplate => SelectedCopyFirmCount > 0 && !IsCopying && _copyingTemplate != null;

        public string SelectedCopyFirmsText =>
            string.Format(GetString("TplCopySelectedFmt") ?? "Вибрано фірм: {0}", SelectedCopyFirmCount);

        public string CopyButtonText => SelectedCopyFirmCount > 1
            ? string.Format(GetString("TplCopyToManyFmt") ?? "Скопіювати ({0})", SelectedCopyFirmCount)
            : (GetString("TplCopyDialogBtn") ?? "Скопіювати");

        private TemplateEntry? _copyingTemplate;

        // Tags panel
        private bool _isTagsPanelOpen;
        public bool IsTagsPanelOpen
        {
            get => _isTagsPanelOpen;
            set => SetProperty(ref _isTagsPanelOpen, value);
        }

        private ObservableCollection<TagEntry> _availableTags = new ObservableCollection<TagEntry>();
        public ObservableCollection<TagEntry> AvailableTags
        {
            get => _availableTags;
            set => SetProperty(ref _availableTags, value);
        }

        public ICommand CloseTagsPanelCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand OpenTemplateFileCommand { get; }

        private string _pendingTemplateFilePath = string.Empty;

        public TemplatesViewModel(
            EmployerCompany? company,
            TemplateService? templateService = null,
            NavigationService? navigationService = null,
            TemplateViewModelFactory? templateViewModelFactory = null,
            ActivityLogService? activityLogService = null,
            CompanyService? companyService = null,
            TagCatalogService? tagCatalogService = null,
            AppSettingsService? appSettingsService = null)
        {
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _templateViewModelFactory = templateViewModelFactory ?? throw new InvalidOperationException("TemplateViewModelFactory is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");

            if (company == null)
            {
                _firmName = "Unknown";
            }
            else
            {
                _firmName = company.Name;
                _ = LoadTemplatesAsync();
            }

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo<MainViewModel>());

            AddTemplateCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Додати шаблон"))
                    return;

                CleanupAddTemplateVm();
                AddTemplateVm = _templateViewModelFactory.CreateAddTemplate(_firmName);
                AddTemplateVm.RequestClose += OnAddTemplateClose;
                IsAddDialogOpen = true;
            });

            EditTemplateCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Редагувати шаблон"))
                    return;

                if (o is TemplateEntry template)
                {
                    var fullPath = _templateService.GetTemplateFullPath(_firmName, template.FilePath);
                    var format = (template.Format ?? "").ToUpper();

                    if (format == "DOCX")
                    {
                        // DOCX may not have a source file — editor handles this gracefully
                        _navigationService.NavigateTo(_templateViewModelFactory.CreateTemplateEditor(_firmName, template));
                    }
                    else if (format == "XLSX")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        _navigationService.NavigateTo(_templateViewModelFactory.CreateXlsxEditor(_firmName, template));
                    }
                    else if (format == "PDF")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        _navigationService.NavigateTo(_templateViewModelFactory.CreatePdfEditor(_firmName, template));
                    }
                    else
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        _pendingTemplateFilePath = fullPath;
                        LoadAvailableTags();
                        IsTagsPanelOpen = true;
                    }
                }
            });

            DeleteTemplateCommand = new AsyncRelayCommand(async o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Видалити шаблон"))
                    return;

                if (o is TemplateEntry template)
                {
                    var msgFormat = GetString("MsgConfirmDeleteTemplate") ?? "Ви впевнені, що хочете видалити шаблон '{0}'?";
                    var title = GetString("TitleConfirmDelete") ?? "Підтвердження";

                    var result = MessageBox.Show(string.Format(msgFormat, template.Name),
                                                 title,
                                                 MessageBoxButton.YesNo,
                                                 MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var tName = template.Name;
                            await _templateService.DeleteTemplateAsync(_firmName, template);
                            Templates.Remove(template);
                            _activityLogService.Log("TemplateDeleted", "Template", _firmName, "",
                                $"Видалено шаблон «{tName}» з {_firmName}");
                        }
                        catch (System.Exception ex)
                        {
                            MessageBox.Show(string.Format(GetString("MsgDeleteError") ?? "Delete error: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            });

            CloseAddDialogCommand = new RelayCommand(o =>
            {
                IsAddDialogOpen = false;
                CleanupAddTemplateVm();
            });

            RenameTemplateCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Перейменувати шаблон"))
                    return;

                if (o is TemplateEntry template)
                {
                    _renamingTemplate = template;
                    RenameText = template.Name;
                    IsRenameDialogOpen = true;
                }
            });

            ConfirmRenameCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Перейменувати шаблон"))
                    return;

                if (_renamingTemplate == null || string.IsNullOrWhiteSpace(RenameText)) return;
                var newName = RenameText.Trim();
                if (newName == _renamingTemplate.Name)
                {
                    IsRenameDialogOpen = false;
                    return;
                }
                try
                {
                    var oldName = _renamingTemplate.Name;
                    var result = _templateService.RenameTemplate(_firmName, _renamingTemplate, newName);
                    if (result == TemplateRenameResult.NameTaken)
                    {
                        MessageBox.Show(
                            GetString("TplRenameExists") ?? "A template with this name already exists. Choose another name.",
                            GetString("TplRenameTitle") ?? "Rename Template",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    _activityLogService.Log("TemplateRenamed", "Template", _firmName, "",
                        $"Перейменовано шаблон «{oldName}» → «{newName}» ({_firmName})",
                        oldName, newName);
                    IsRenameDialogOpen = false;
                    _ = LoadTemplatesAsync();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(string.Format(GetString("MsgRenameError") ?? "Rename error: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            CancelRenameCommand = new RelayCommand(o =>
            {
                IsRenameDialogOpen = false;
                _renamingTemplate = null;
            });

            CopyTemplateToCompanyCommand = new RelayCommand(o =>
            {
                if (!PolicyService.EnsureWriteAllowed("Копіювати шаблон"))
                    return;

                if (o is not TemplateEntry template)
                    return;

                // Hidden firms and firms this user cannot open are not offered as targets.
                var companies = _companyService.VisibleCompanies
                    .Where(PolicyService.CanAccessCompany)
                    .Where(c => !string.Equals(c.Name, _firmName, System.StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Name)
                    .ToList();

                if (companies.Count == 0)
                {
                    MessageBox.Show(
                        GetString("TplCopyNoCompanies") ?? "There are no other companies available for copying the template.",
                        GetString("TitleError") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _copyingTemplate = template;
                OpenCopyDialog(template, companies.Select(company => company.Name));
            });

            ConfirmCopyTemplateCommand = new AsyncRelayCommand(_ => CopyToSelectedFirmsAsync(), _ => CanCopyTemplate);

            CancelCopyTemplateCommand = new RelayCommand(o => CloseCopyDialog());

            CloseTagsPanelCommand = new RelayCommand(o => IsTagsPanelOpen = false);

            CopyTagCommand = new RelayCommand(o =>
            {
                if (o is TagEntry tag)
                {
                    Clipboard.SetText($"${{{tag.Tag}}}");
                }
            });

            OpenTemplateFileCommand = new RelayCommand(o =>
            {
                if (string.IsNullOrEmpty(_pendingTemplateFilePath) || !File.Exists(_pendingTemplateFilePath)) return;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _pendingTemplateFilePath,
                        UseShellExecute = true
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(string.Format(GetString("MsgOpenFileError") ?? "Could not open file: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void OnAddTemplateClose()
        {
            var createdTemplate = AddTemplateVm?.CreatedTemplate;
            IsAddDialogOpen = false;
            CleanupAddTemplateVm();

            if (createdTemplate != null
                && string.Equals(createdTemplate.Format, "DOCX", System.StringComparison.OrdinalIgnoreCase))
            {
                _navigationService.NavigateTo(_templateViewModelFactory.CreateTemplateEditor(_firmName, createdTemplate));
                return;
            }

            _ = LoadTemplatesAsync();
        }

        private void CleanupAddTemplateVm()
        {
            if (AddTemplateVm != null)
                AddTemplateVm.RequestClose -= OnAddTemplateClose;
        }

        public void Cleanup()
        {
            LoggingService.LogInfo("TemplatesViewModel.Cleanup", "Invalidated load and closed template dialogs.");
            _loadGeneration++;
            IsAddDialogOpen = false;
            CleanupAddTemplateVm();
            AddTemplateVm = null;
            CloseCopyDialog();
        }

        private void OpenCopyDialog(TemplateEntry template, IEnumerable<string> firmNames)
        {
            var generation = ++_copyDialogGeneration;
            _copyFirmTemplateNames = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
            _copyFirmItems = firmNames
                .Select(name => new CopyTargetFirmItem(name, OnCopyFirmSelectionChanged))
                .ToList();

            CopySourceName = template.Name;
            CopySourceText = string.Format(GetString("TplCopyFromFmt") ?? "з фірми {0}", _firmName);
            _copyFirmSearch = string.Empty;
            OnPropertyChanged(nameof(CopyFirmSearch));
            _copyTemplateName = template.Name;
            OnPropertyChanged(nameof(CopyTemplateName));
            ApplyCopyFirmFilter();
            OnCopyFirmSelectionChanged();
            IsCopyDialogOpen = true;

            // Reading every firm's template list is file work, so it runs after the dialog is shown.
            var names = _copyFirmItems.Select(item => item.Name).ToList();
            _ = Task.Run(() => names.ToDictionary(
                    name => name,
                    name => _templateService.GetTemplates(name)
                        .Select(existing => existing.Name?.Trim() ?? string.Empty)
                        .ToHashSet(System.StringComparer.OrdinalIgnoreCase),
                    System.StringComparer.OrdinalIgnoreCase))
                .ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        LoggingService.LogWarning("TemplatesViewModel.OpenCopyDialog", task.Exception?.GetBaseException().Message ?? string.Empty);
                        return;
                    }

                    if (generation != _copyDialogGeneration || !IsCopyDialogOpen)
                        return;

                    _copyFirmTemplateNames = task.Result;
                    RefreshSameNameMarks();
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ApplyCopyFirmFilter()
        {
            var query = CopyFirmSearch?.Trim() ?? string.Empty;
            VisibleCopyFirms = new ObservableCollection<CopyTargetFirmItem>(string.IsNullOrEmpty(query)
                ? _copyFirmItems
                : _copyFirmItems.Where(item => item.Name.Contains(query, System.StringComparison.CurrentCultureIgnoreCase)));
            OnPropertyChanged(nameof(HasNoVisibleCopyFirms));
        }

        private void RefreshSameNameMarks()
        {
            var name = CopyTemplateName?.Trim() ?? string.Empty;
            foreach (var item in _copyFirmItems)
                item.HasSameName = name.Length > 0
                    && _copyFirmTemplateNames.TryGetValue(item.Name, out var existing)
                    && existing.Contains(name);
        }

        private void OnCopyFirmSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCopyFirmCount));
            OnPropertyChanged(nameof(SelectedCopyFirmsText));
            OnPropertyChanged(nameof(CopyButtonText));
            OnPropertyChanged(nameof(CanCopyTemplate));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task CopyToSelectedFirmsAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("Копіювати шаблон"))
                return;

            var template = _copyingTemplate;
            var targets = _copyFirmItems.Where(item => item.IsSelected).Select(item => item.Name).ToList();
            if (template == null || targets.Count == 0)
                return;

            var baseName = string.IsNullOrWhiteSpace(CopyTemplateName) ? template.Name : CopyTemplateName.Trim();
            var plan = targets
                .Select(firm => (Firm: firm, Name: BuildUniqueTemplateName(baseName,
                    _copyFirmTemplateNames.TryGetValue(firm, out var existing) ? existing : null)))
                .ToList();
            var sourceFirm = _firmName;

            IsCopying = true;
            OnCopyFirmSelectionChanged();
            try
            {
                var results = await Task.Run(() => plan.Select(step =>
                {
                    try
                    {
                        var created = _templateService.CopyTemplateToCompany(sourceFirm, template, step.Firm, step.Name);
                        return (step.Firm, Created: (TemplateEntry?)created, Error: (string?)null);
                    }
                    catch (System.Exception ex)
                    {
                        return (step.Firm, Created: (TemplateEntry?)null, Error: (string?)ex.Message);
                    }
                }).ToList());

                foreach (var result in results.Where(result => result.Created != null))
                {
                    _activityLogService.Log(
                        "TemplateCopiedToCompany",
                        "Template",
                        result.Firm,
                        "",
                        $"Скопійовано шаблон «{template.Name}» з {sourceFirm} у {result.Firm} як «{result.Created!.Name}»");
                }

                var copied = results.Where(result => result.Created != null).Select(result => result.Firm).ToList();
                var failed = results.Where(result => result.Created == null).ToList();
                foreach (var failure in failed)
                    LoggingService.LogWarning("TemplatesViewModel.CopyToSelectedFirmsAsync", $"{failure.Firm}: {failure.Error}");

                if (failed.Count == 0)
                {
                    ToastService.Instance.Success(string.Format(GetString("TplCopyDoneFmt") ?? "Скопійовано в: {0}", string.Join(", ", copied)));
                    CloseCopyDialog();
                    return;
                }

                ToastService.Instance.Warning(string.Format(
                    GetString("TplCopyPartialFmt") ?? "Скопійовано в: {0}. Не вдалося: {1}",
                    copied.Count == 0 ? "—" : string.Join(", ", copied),
                    string.Join(", ", failed.Select(failure => failure.Firm))));

                // Keep the dialog open with only the firms that still need the template.
                foreach (var item in _copyFirmItems)
                    item.IsSelected = failed.Any(failure => string.Equals(failure.Firm, item.Name, System.StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                IsCopying = false;
                OnCopyFirmSelectionChanged();
            }
        }

        /// <summary>"Name", or "Name (2)", "Name (3)"... when the target firm already has that name.</summary>
        internal static string BuildUniqueTemplateName(string name, ICollection<string>? existingNames)
        {
            var baseName = name.Trim();
            if (existingNames == null || !existingNames.Contains(baseName))
                return baseName;

            for (var number = 2; ; number++)
            {
                var candidate = $"{baseName} ({number})";
                if (!existingNames.Contains(candidate))
                    return candidate;
            }
        }

        private void CloseCopyDialog()
        {
            _copyDialogGeneration++;
            IsCopyDialogOpen = false;
            _copyingTemplate = null;
            _copyFirmItems = new List<CopyTargetFirmItem>();
            _copyFirmTemplateNames = new Dictionary<string, HashSet<string>>(System.StringComparer.OrdinalIgnoreCase);
            VisibleCopyFirms = new ObservableCollection<CopyTargetFirmItem>();
            _copyFirmSearch = string.Empty;
            OnPropertyChanged(nameof(CopyFirmSearch));
            _copyTemplateName = string.Empty;
            OnPropertyChanged(nameof(CopyTemplateName));
            OnCopyFirmSelectionChanged();
        }

        /// <summary>Puts <paramref name="source"/> before (or after) <paramref name="target"/> and saves the new order.</summary>
        public void MoveTemplate(TemplateEntry? source, TemplateEntry? target, bool placeAfter = false)
        {
            if (source == null || target == null || ReferenceEquals(source, target))
                return;

            var list = Templates.ToList();
            var from = list.IndexOf(source);
            if (from < 0 || !list.Contains(target))
                return;

            list.RemoveAt(from);
            var to = list.IndexOf(target) + (placeAfter ? 1 : 0);
            list.Insert(to, source);
            if (list.SequenceEqual(Templates))
                return;

            if (!PolicyService.EnsureWriteAllowed("Змінити порядок шаблонів"))
                return;

            Templates = new ObservableCollection<TemplateEntry>(list);
            _ = SaveTemplateOrderAsync(list.Select(template => template.FilePath).ToList());
        }

        private async Task SaveTemplateOrderAsync(IReadOnlyList<string> orderedPaths)
        {
            var firmName = _firmName;
            try
            {
                await Task.Run(() => _templateService.ReorderTemplates(firmName, orderedPaths));
            }
            catch (System.Exception ex)
            {
                LoggingService.LogError("TemplatesViewModel.SaveTemplateOrderAsync", ex);
                ToastService.Instance.Warning(GetString("TplOrderSaveFailed") ?? "Не вдалося зберегти порядок шаблонів.");
                await LoadTemplatesAsync();
            }
        }

        private async Task LoadTemplatesAsync()
        {
            if (string.IsNullOrWhiteSpace(_firmName) || string.Equals(_firmName, "Unknown", System.StringComparison.OrdinalIgnoreCase))
                return;

            var generation = ++_loadGeneration;
            try
            {
                var firmName = _firmName;
                IsLoading = true;
                var list = await Task.Run(() => _templateService.GetTemplates(firmName));
                if (generation != _loadGeneration || !string.Equals(_firmName, firmName, System.StringComparison.OrdinalIgnoreCase))
                    return;

                Templates = new ObservableCollection<TemplateEntry>(list);
            }
            catch (System.Exception ex)
            {
                LoggingService.LogError("TemplatesViewModel.LoadTemplatesAsync", ex);
            }
            finally
            {
                if (generation == _loadGeneration)
                    IsLoading = false;
            }
        }

        private void LoadAvailableTags()
        {
            var tags = _tagCatalogService.GetAllTagDefinitions() ?? new List<TagEntry>();
            var hiddenTags = _appSettingsService.Settings?.HiddenTags;
            if (hiddenTags != null && hiddenTags.Count > 0)
            {
                var hidden = new HashSet<string>(hiddenTags);
                tags = tags.Where(t => !hidden.Contains(t.Tag)).ToList();
            }
            AvailableTags = new ObservableCollection<TagEntry>(tags);
        }

        private string? GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string;
        }
    }
}

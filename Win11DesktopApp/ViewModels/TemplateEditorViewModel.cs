using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TemplateEditorViewModel : ViewModelBase
    {
        private readonly string _firmName;
        private readonly TemplateEntry _template;
        private readonly TemplateService _templateService;
        private readonly NavigationService _navigationService;
        private readonly TemplateViewModelFactory _templateViewModelFactory;
        private readonly CompanyService _companyService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private readonly DocumentGenerationService _documentGenerationService = new();
        private bool _templateUnavailable;
        private bool _templateUnavailableNotified;
        private bool _navigateBackScheduled;

        private static new string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            try { return string.Format(fmt, args); }
            catch { return fmt; }
        }

        public string Title => ResF("EditorTitleFmt", _template.Name);
        public string RtfFilePath { get; }
        public string NativeDocumentPath { get; }
        public string TemplateFolderPath { get; }
        public string LayoutSettingsPath { get; }
        public string OriginalTemplatePath { get; }

        public ObservableCollection<TagGroupViewModel> TagGroups { get; }

        private string _tagSearchQuery = string.Empty;
        public string TagSearchQuery
        {
            get => _tagSearchQuery;
            set
            {
                if (SetProperty(ref _tagSearchQuery, value))
                    OnPropertyChanged(nameof(FilteredTagGroups));
            }
        }

        public ObservableCollection<TagGroupViewModel> FilteredTagGroups
            => TagGroups != null ? TagGroupViewModel.FilterTagGroups(TagGroups, TagSearchQuery) : new ObservableCollection<TagGroupViewModel>();

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand InsertTagCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand OpenInWordCommand { get; }
        public ICommand RefreshFromWordCommand { get; }

        public ObservableCollection<TemplateEditorPageSizeOption> AvailablePageSizes { get; } = new();
        public ObservableCollection<TemplateEditorPageOrientationOption> AvailablePageOrientations { get; } = new();
        public ObservableCollection<TemplateEditorPageMarginOption> AvailablePageMargins { get; } = new();

        public event Action<string>? RequestInsertTag;
        public event Action? RequestReloadDocument;
        public Func<Task<byte[]?>>? RequestGetDocxBytes { get; set; }

        public string EditorLocale => _appSettingsService.Settings?.LanguageCode ?? "uk";

        public IReadOnlyList<string> HighlightTagNames =>
            TagGroups.SelectMany(group => group.Tags.Select(tag => tag.Tag)).Distinct().ToList();

        private bool _isEditorLoading = true;
        public bool IsEditorLoading
        {
            get => _isEditorLoading;
            set
            {
                if (SetProperty(ref _isEditorLoading, value))
                    OnPropertyChanged(nameof(HeaderStatusText));
            }
        }

        private bool _isSaving;
        public bool IsSaving
        {
            get => _isSaving;
            set
            {
                if (SetProperty(ref _isSaving, value))
                    OnPropertyChanged(nameof(HeaderStatusText));
            }
        }

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                    OnPropertyChanged(nameof(HeaderStatusText));
            }
        }

        private DateTime? _lastSavedAt;
        public DateTime? LastSavedAt
        {
            get => _lastSavedAt;
            set
            {
                if (SetProperty(ref _lastSavedAt, value))
                    OnPropertyChanged(nameof(HeaderStatusText));
            }
        }

        public string HeaderStatusText
        {
            get
            {
                if (IsEditorLoading)
                    return Res("EditorLoading");
                if (IsSaving)
                    return Res("EditorSaving");
                if (IsWordLayoutMode)
                    return Res("EditorWordLayoutActive");
                if (IsDirty)
                    return Res("EditorUnsaved");
                if (LastSavedAt.HasValue)
                    return ResF("EditorLastSavedFmt", LastSavedAt.Value.ToString("HH:mm"));
                return Res("EditorReady");
            }
        }

        private bool _isWordLayoutMode;
        /// <summary>
        /// Soft mode: generation prefers native template.docx, but the in-app editor stays fully editable.
        /// Saving editor content clears this and returns generation to content.rtf until Refresh from Word.
        /// </summary>
        public bool IsWordLayoutMode
        {
            get => _isWordLayoutMode;
            private set
            {
                if (SetProperty(ref _isWordLayoutMode, value))
                {
                    OnPropertyChanged(nameof(HeaderStatusText));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _editorStatus = string.Empty;
        public string EditorStatus
        {
            get => _editorStatus;
            set => SetProperty(ref _editorStatus, value);
        }

        private TemplateEditorPageSizeOption? _selectedPageSize;
        public TemplateEditorPageSizeOption? SelectedPageSize
        {
            get => _selectedPageSize;
            set
            {
                if (!SetProperty(ref _selectedPageSize, value))
                    return;

                RecalculatePageLayout();
            }
        }

        private TemplateEditorPageOrientationOption? _selectedPageOrientation;
        public TemplateEditorPageOrientationOption? SelectedPageOrientation
        {
            get => _selectedPageOrientation;
            set
            {
                if (!SetProperty(ref _selectedPageOrientation, value))
                    return;

                RecalculatePageLayout();
            }
        }

        private TemplateEditorPageMarginOption? _selectedPageMargin;
        public TemplateEditorPageMarginOption? SelectedPageMargin
        {
            get => _selectedPageMargin;
            set
            {
                if (!SetProperty(ref _selectedPageMargin, value))
                    return;

                RecalculatePageLayout();
            }
        }

        private double _pagePreviewWidth = 794;
        public double PagePreviewWidth
        {
            get => _pagePreviewWidth;
            set => SetProperty(ref _pagePreviewWidth, value);
        }

        private double _pagePreviewHeight = 1123;
        public double PagePreviewHeight
        {
            get => _pagePreviewHeight;
            set => SetProperty(ref _pagePreviewHeight, value);
        }

        private Thickness _pagePadding = new(96);
        public Thickness PagePadding
        {
            get => _pagePadding;
            set => SetProperty(ref _pagePadding, value);
        }

        public TemplateEditorViewModel(
            string firmName,
            TemplateEntry template,
            TemplateService? templateService = null,
            NavigationService? navigationService = null,
            TemplateViewModelFactory? templateViewModelFactory = null,
            CompanyService? companyService = null,
            TagCatalogService? tagCatalogService = null,
            AppSettingsService? appSettingsService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _templateViewModelFactory = templateViewModelFactory ?? throw new InvalidOperationException("TemplateViewModelFactory is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                OriginalTemplatePath = fullPath;
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
                NativeDocumentPath = OriginalTemplatePath;
                LayoutSettingsPath = Path.Combine(TemplateFolderPath, "editor-layout.json");
                if (File.Exists(OriginalTemplatePath))
                    LastSavedAt = File.GetLastWriteTime(OriginalTemplatePath);
                else if (File.Exists(RtfFilePath))
                    LastSavedAt = File.GetLastWriteTime(RtfFilePath);
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
                NativeDocumentPath = string.Empty;
                LayoutSettingsPath = string.Empty;
                OriginalTemplatePath = string.Empty;
            }

            try
            {
                var allTags = _tagCatalogService.GetAllTagDefinitions() ?? new List<TagEntry>();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, _appSettingsService.Settings?.HiddenTags ?? new List<string>());
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            InitializePageLayoutOptions();
            LoadPersistedPageLayout();
            EnsureTemplateSourceAvailable();
            LoadLayoutSourceState();

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsSaving && !IsEditorLoading);
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            OpenInWordCommand = new AsyncRelayCommand(_ => OpenInWordAsync(), _ => !IsSaving && !IsEditorLoading && !_templateUnavailable);
            RefreshFromWordCommand = new RelayCommand(_ => RefreshFromWord(), _ => !IsSaving && !IsEditorLoading && !_templateUnavailable);
            StatusMessage = Res("EditorLoading");
        }

        public TemplateEditorViewModel(
            string firmName,
            TemplateEntry template,
            TagCatalogService tagCatalogService,
            TemplateService templateService,
            NavigationService? navigationService = null,
            TemplateViewModelFactory? templateViewModelFactory = null,
            CompanyService? companyService = null,
            AppSettingsService? appSettingsService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService;
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _templateViewModelFactory = templateViewModelFactory ?? throw new InvalidOperationException("TemplateViewModelFactory is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                OriginalTemplatePath = fullPath;
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
                NativeDocumentPath = OriginalTemplatePath;
                LayoutSettingsPath = Path.Combine(TemplateFolderPath, "editor-layout.json");
                if (File.Exists(OriginalTemplatePath))
                    LastSavedAt = File.GetLastWriteTime(OriginalTemplatePath);
                else if (File.Exists(RtfFilePath))
                    LastSavedAt = File.GetLastWriteTime(RtfFilePath);
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
                NativeDocumentPath = string.Empty;
                LayoutSettingsPath = string.Empty;
                OriginalTemplatePath = string.Empty;
            }

            try
            {
                var allTags = tagCatalogService.GetAllTagDefinitions();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, _appSettingsService.Settings?.HiddenTags ?? new List<string>());
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            InitializePageLayoutOptions();
            LoadPersistedPageLayout();
            EnsureTemplateSourceAvailable();
            LoadLayoutSourceState();

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsSaving && !IsEditorLoading);
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            OpenInWordCommand = new AsyncRelayCommand(_ => OpenInWordAsync(), _ => !IsSaving && !IsEditorLoading && !_templateUnavailable);
            RefreshFromWordCommand = new RelayCommand(_ => RefreshFromWord(), _ => !IsSaving && !IsEditorLoading && !_templateUnavailable);
            StatusMessage = Res("EditorLoading");
        }

        private void InitializePageLayoutOptions()
        {
            if (AvailablePageSizes.Count == 0)
            {
                AvailablePageSizes.Add(new TemplateEditorPageSizeOption("a4", "A4 (21 × 29,7 см)", 794, 1123));
                AvailablePageSizes.Add(new TemplateEditorPageSizeOption("letter", "Letter (21,59 × 27,94 см)", 816, 1056));
            }

            if (AvailablePageOrientations.Count == 0)
            {
                AvailablePageOrientations.Add(new TemplateEditorPageOrientationOption("portrait", Res("EditorOrientationPortrait"), false));
                AvailablePageOrientations.Add(new TemplateEditorPageOrientationOption("landscape", Res("EditorOrientationLandscape"), true));
            }

            if (AvailablePageMargins.Count == 0)
            {
                AvailablePageMargins.Add(new TemplateEditorPageMarginOption("normal", Res("EditorMarginsNormal"), new Thickness(96, 96, 96, 96)));
                AvailablePageMargins.Add(new TemplateEditorPageMarginOption("narrow", Res("EditorMarginsNarrow"), new Thickness(48, 48, 48, 48)));
                AvailablePageMargins.Add(new TemplateEditorPageMarginOption("wide", Res("EditorMarginsWide"), new Thickness(192, 96, 192, 96)));
            }

            SelectedPageSize ??= AvailablePageSizes.FirstOrDefault();
            SelectedPageOrientation ??= AvailablePageOrientations.FirstOrDefault();
            SelectedPageMargin ??= AvailablePageMargins.FirstOrDefault();

            RecalculatePageLayout();
        }

        private void RecalculatePageLayout()
        {
            var selectedSize = SelectedPageSize ?? AvailablePageSizes.FirstOrDefault();
            var selectedOrientation = SelectedPageOrientation ?? AvailablePageOrientations.FirstOrDefault();
            var selectedMargin = SelectedPageMargin ?? AvailablePageMargins.FirstOrDefault();

            if (selectedSize == null || selectedOrientation == null || selectedMargin == null)
                return;

            PagePreviewWidth = selectedOrientation.IsLandscape ? selectedSize.HeightPx : selectedSize.WidthPx;
            PagePreviewHeight = selectedOrientation.IsLandscape ? selectedSize.WidthPx : selectedSize.HeightPx;
            PagePadding = selectedMargin.Padding;
        }

        private void LoadPersistedPageLayout()
        {
            if (string.IsNullOrWhiteSpace(LayoutSettingsPath) || !File.Exists(LayoutSettingsPath))
                return;

            try
            {
                var settings = SafeFileService.ReadJsonOrDefault(LayoutSettingsPath, new TemplateEditorLayoutSettings());
                SelectedPageSize = AvailablePageSizes.FirstOrDefault(x => x.Key == settings.PageSizeKey) ?? SelectedPageSize;
                SelectedPageOrientation = AvailablePageOrientations.FirstOrDefault(x => x.Key == settings.OrientationKey) ?? SelectedPageOrientation;
                SelectedPageMargin = AvailablePageMargins.FirstOrDefault(x => x.Key == settings.MarginKey) ?? SelectedPageMargin;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorViewModel.LoadPersistedPageLayout", ex.Message);
            }
        }

        private void SavePersistedPageLayout()
        {
            if (string.IsNullOrWhiteSpace(LayoutSettingsPath))
                return;

            var settings = new TemplateEditorLayoutSettings
            {
                PageSizeKey = SelectedPageSize?.Key ?? "a4",
                OrientationKey = SelectedPageOrientation?.Key ?? "portrait",
                MarginKey = SelectedPageMargin?.Key ?? "normal"
            };

            SafeFileService.WriteJsonAtomic(LayoutSettingsPath, settings);
        }

        private void NavigateBack()
        {
            if (IsDirty)
            {
                var result = MessageBox.Show(
                    Res("EditorUnsavedClose"),
                    Res("EditorUnsavedTitle"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var company = _companyService.Companies.FirstOrDefault(c => c.Name == _firmName);
            if (company != null)
                _navigationService.NavigateTo(_templateViewModelFactory.CreateTemplates(company));
            else
                _navigationService.NavigateTo<MainViewModel>();
        }

        internal void HandleTemplateOpenFailure(string? details = null)
        {
            var message = string.IsNullOrWhiteSpace(details)
                ? Res("MsgTemplateNotFound")
                : ResF("MsgOpenFileError", details);
            MarkTemplateUnavailable(message);
        }

        private void EnsureTemplateSourceAvailable()
        {
            if (!string.IsNullOrWhiteSpace(NativeDocumentPath) && File.Exists(NativeDocumentPath))
                return;

            if (!string.IsNullOrWhiteSpace(RtfFilePath) && File.Exists(RtfFilePath))
                return;

            if (!string.IsNullOrWhiteSpace(OriginalTemplatePath) && File.Exists(OriginalTemplatePath))
                return;

            MarkTemplateUnavailable(Res("MsgTemplateNotFound"));
        }

        private void MarkTemplateUnavailable(string message)
        {
            _templateUnavailable = true;
            StatusMessage = message;
            if (_templateUnavailableNotified)
                return;

            _templateUnavailableNotified = true;
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                ToastService.Instance.Warning(message);
                if (_navigateBackScheduled)
                    return;

                _navigateBackScheduled = true;
                NavigateBack();
            }));
        }

        private async Task SaveAsync()
        {
            if (!PolicyService.EnsureWriteAllowed("зберегти шаблон"))
                return;

            await Task.Yield();

            try
            {
                if (_templateUnavailable)
                    return;

                IsSaving = true;
                StatusMessage = Res("EditorSaving");
                CommandManager.InvalidateRequerySuggested();

                if (string.IsNullOrEmpty(TemplateFolderPath))
                {
                    StatusMessage = Res("EditorErrPath");
                    return;
                }

                Directory.CreateDirectory(TemplateFolderPath);
                var docxBytes = RequestGetDocxBytes == null ? null : await RequestGetDocxBytes();
                if (docxBytes == null || docxBytes.Length == 0)
                {
                    StatusMessage = Res("EditorErrEmpty");
                    return;
                }

                if (string.IsNullOrWhiteSpace(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorErrPath");
                    return;
                }

                SafeFileService.WriteBytesAtomic(OriginalTemplatePath, docxBytes);
                SavePersistedPageLayout();

                // Soft mode: editor content is now the source of truth until user refreshes from Word again.
                var clearedWordLayout = ClearWordLayoutSourceIfNeeded();

                LastSavedAt = DateTime.Now;
                IsDirty = false;
                StatusMessage = clearedWordLayout
                    ? Res("EditorWordClearedByEditorSave")
                    : Res("EditorSaved");
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
            }
            finally
            {
                IsSaving = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool ClearWordLayoutSourceIfNeeded()
        {
            if (!IsWordLayoutMode || string.IsNullOrWhiteSpace(TemplateFolderPath))
                return false;

            _templateService.SetTemplateLayoutSource(TemplateFolderPath, TemplateLayoutSource.Editor);
            IsWordLayoutMode = false;
            return true;
        }

        private void LoadLayoutSourceState()
        {
            if (string.IsNullOrWhiteSpace(TemplateFolderPath))
            {
                IsWordLayoutMode = false;
                return;
            }

            IsWordLayoutMode = string.Equals(
                _templateService.GetTemplateLayoutSource(TemplateFolderPath),
                TemplateLayoutSource.Word,
                StringComparison.OrdinalIgnoreCase);
        }

        private async Task OpenInWordAsync()
        {
            if (_templateUnavailable)
                return;

            if (!PolicyService.EnsureWriteAllowed("відкрити шаблон у Word"))
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(TemplateFolderPath) || string.IsNullOrWhiteSpace(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorErrPath");
                    return;
                }

                // Persist editor edits first (also clears soft Word layout if content changed).
                if (IsDirty)
                    await SaveAsync();

                if (!File.Exists(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorWordDocxMissing");
                    ToastService.Instance.Warning(StatusMessage);
                    return;
                }

                if (!DocumentGenerationService.TryOpenFile(OriginalTemplatePath, out var openError))
                {
                    StatusMessage = openError ?? Res("EditorWordOpenFailedFmt");
                    ToastService.Instance.Warning(StatusMessage);
                    return;
                }

                StatusMessage = IsWordLayoutMode
                    ? Res("EditorWordOpenedHint")
                    : Res("EditorWordOpenedEditorHint");
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
                ToastService.Instance.Warning(StatusMessage);
            }
        }

        private void RefreshFromWord()
        {
            if (_templateUnavailable)
                return;

            if (!PolicyService.EnsureWriteAllowed("оновити шаблон з Word"))
                return;

            try
            {
                if (string.IsNullOrWhiteSpace(TemplateFolderPath) || string.IsNullOrWhiteSpace(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorErrPath");
                    return;
                }

                if (!File.Exists(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorWordDocxMissing");
                    ToastService.Instance.Warning(StatusMessage);
                    return;
                }

                if (!DocumentGenerationService.CanOpenAsTemplateDocx(OriginalTemplatePath))
                {
                    StatusMessage = Res("EditorWordSaveInWordFirst");
                    ToastService.Instance.Warning(StatusMessage);
                    return;
                }

                _templateService.SetTemplateLayoutSource(TemplateFolderPath, TemplateLayoutSource.Word);
                IsWordLayoutMode = true;
                IsDirty = false;
                RequestReloadDocument?.Invoke();
                StatusMessage = Res("EditorWordLayoutActive");
                ToastService.Instance.Success(Res("EditorWordRefreshOk"));
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
                ToastService.Instance.Warning(StatusMessage);
            }
        }

        private void InsertTag(object? parameter)
        {
            if (!PolicyService.EnsureWriteAllowed("вставити тег у шаблон"))
                return;

            if (parameter is string tag)
            {
                var tagText = $"${{{tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
            else if (parameter is TagEntry entry)
            {
                var tagText = $"${{{entry.Tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
        }

        public void NotifyEditorLoaded()
        {
            IsEditorLoading = false;
            if (IsWordLayoutMode)
            {
                StatusMessage = Res("EditorWordLayoutActive");
            }
            else
            {
                StatusMessage = LastSavedAt.HasValue
                    ? ResF("EditorLastSavedFmt", LastSavedAt.Value.ToString("HH:mm"))
                    : Res("EditorReady");
            }
            CommandManager.InvalidateRequerySuggested();
        }

        public void MarkDirty()
        {
            if (IsEditorLoading)
                return;

            if (!IsDirty)
                IsDirty = true;

            StatusMessage = IsWordLayoutMode
                ? Res("EditorWordWillClearOnSave")
                : Res("EditorUnsaved");
        }

        private void CopyTag(object? parameter)
        {
            try
            {
                string tagText;
                if (parameter is string tag)
                    tagText = $"${{{tag}}}";
                else if (parameter is TagEntry entry)
                    tagText = $"${{{entry.Tag}}}";
                else
                    return;

                Clipboard.SetText(tagText);
                StatusMessage = ResF("EditorCopied", tagText);
            }
            catch (Exception ex) { LoggingService.LogWarning("TemplateEditorViewModel.CopyTag", ex.Message); }
        }
    }

    public sealed class TemplateEditorPageSizeOption
    {
        public TemplateEditorPageSizeOption(string key, string displayName, double widthPx, double heightPx)
        {
            Key = key;
            DisplayName = displayName;
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public double WidthPx { get; }
        public double HeightPx { get; }
    }

    public sealed class TemplateEditorPageOrientationOption
    {
        public TemplateEditorPageOrientationOption(string key, string displayName, bool isLandscape)
        {
            Key = key;
            DisplayName = displayName;
            IsLandscape = isLandscape;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public bool IsLandscape { get; }
    }

    public sealed class TemplateEditorPageMarginOption
    {
        public TemplateEditorPageMarginOption(string key, string displayName, Thickness padding)
        {
            Key = key;
            DisplayName = displayName;
            Padding = padding;
        }

        public string Key { get; }
        public string DisplayName { get; }
        public Thickness Padding { get; }
    }
}

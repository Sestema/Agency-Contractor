using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
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
        private readonly GeminiApiService _geminiApiService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly StarterTemplateCatalogService _starterTemplateCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private readonly AiWindowFactory _aiWindowFactory;
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
        internal AiWindowFactory AiWindowFactory => _aiWindowFactory;

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand InsertTagCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand AIInsertTagsCommand { get; private set; }
        public ICommand CloseAITagsCommand { get; private set; }
        public ICommand OpenStarterTemplatesCommand { get; }
        public ICommand CloseStarterTemplatesCommand { get; }
        public ICommand ApplySelectedStarterTemplateCommand { get; }
        public ICommand SelectStarterTemplateCommand { get; }

        public ObservableCollection<TemplateEditorPageSizeOption> AvailablePageSizes { get; } = new();
        public ObservableCollection<TemplateEditorPageOrientationOption> AvailablePageOrientations { get; } = new();
        public ObservableCollection<TemplateEditorPageMarginOption> AvailablePageMargins { get; } = new();

        public event Action<string>? RequestInsertTag;
        public event Action<string>? RequestApplyStarterTemplate;
        public event Action<List<(string ContextBefore, string ReplaceWhat, string Tag)>>? RequestReplaceTagsInDocument;
        public Func<string?>? RequestGetRtfContent { get; set; }
        public Func<byte[]?>? RequestGetXamlPackageContent { get; set; }
        public Func<string?>? RequestGetPlainText { get; set; }

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
                if (IsDirty)
                    return Res("EditorUnsaved");
                if (LastSavedAt.HasValue)
                    return ResF("EditorLastSavedFmt", LastSavedAt.Value.ToString("HH:mm"));
                return Res("EditorReady");
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

        // ---- AI Insert Tags ----
        private bool _isAITagsRunning;
        public bool IsAITagsRunning
        {
            get => _isAITagsRunning;
            set => SetProperty(ref _isAITagsRunning, value);
        }

        private bool _isAITagsOpen;
        public bool IsAITagsOpen
        {
            get => _isAITagsOpen;
            set => SetProperty(ref _isAITagsOpen, value);
        }

        private string _aiTagsStatus = string.Empty;
        public string AITagsStatus
        {
            get => _aiTagsStatus;
            set => SetProperty(ref _aiTagsStatus, value);
        }

        private bool _isStarterTemplatesOpen;
        public bool IsStarterTemplatesOpen
        {
            get => _isStarterTemplatesOpen;
            set => SetProperty(ref _isStarterTemplatesOpen, value);
        }

        private ObservableCollection<StarterTemplateCatalogEntry> _starterTemplates = new();
        public ObservableCollection<StarterTemplateCatalogEntry> StarterTemplates
        {
            get => _starterTemplates;
            set => SetProperty(ref _starterTemplates, value);
        }

        private StarterTemplateCatalogEntry? _selectedStarterTemplate;
        public StarterTemplateCatalogEntry? SelectedStarterTemplate
        {
            get => _selectedStarterTemplate;
            set
            {
                if (!SetProperty(ref _selectedStarterTemplate, value))
                    return;

                LoadSelectedStarterTemplatePreview();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _selectedStarterTemplateRtf = string.Empty;
        public string SelectedStarterTemplateRtf
        {
            get => _selectedStarterTemplateRtf;
            set => SetProperty(ref _selectedStarterTemplateRtf, value);
        }

        private string _starterTemplatesStatus = string.Empty;
        public string StarterTemplatesStatus
        {
            get => _starterTemplatesStatus;
            set => SetProperty(ref _starterTemplatesStatus, value);
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
            GeminiApiService? geminiApiService = null,
            TagCatalogService? tagCatalogService = null,
            StarterTemplateCatalogService? starterTemplateCatalogService = null,
            AppSettingsService? appSettingsService = null,
            AiWindowFactory? aiWindowFactory = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? throw new InvalidOperationException("TemplateService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _templateViewModelFactory = templateViewModelFactory ?? throw new InvalidOperationException("TemplateViewModelFactory is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _starterTemplateCatalogService = starterTemplateCatalogService ?? throw new InvalidOperationException("StarterTemplateCatalogService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _aiWindowFactory = aiWindowFactory ?? throw new InvalidOperationException("AiWindowFactory is not initialized.");

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                OriginalTemplatePath = fullPath;
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
                NativeDocumentPath = Path.Combine(TemplateFolderPath, "content.xamlpackage");
                LayoutSettingsPath = Path.Combine(TemplateFolderPath, "editor-layout.json");
                if (File.Exists(RtfFilePath))
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

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsSaving && !IsEditorLoading);
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            AIInsertTagsCommand = new RelayCommand(o => RunAIInsertTags(), o => !IsAITagsRunning);
            CloseAITagsCommand = new RelayCommand(o => IsAITagsOpen = false);
            OpenStarterTemplatesCommand = new RelayCommand(o => OpenStarterTemplates());
            CloseStarterTemplatesCommand = new RelayCommand(o => IsStarterTemplatesOpen = false);
            ApplySelectedStarterTemplateCommand = new RelayCommand(o => ApplySelectedStarterTemplate(), o => SelectedStarterTemplate != null);
            SelectStarterTemplateCommand = new RelayCommand(o => SelectStarterTemplate(o));
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
            GeminiApiService? geminiApiService = null,
            StarterTemplateCatalogService? starterTemplateCatalogService = null,
            AppSettingsService? appSettingsService = null,
            AiWindowFactory? aiWindowFactory = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService;
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _templateViewModelFactory = templateViewModelFactory ?? throw new InvalidOperationException("TemplateViewModelFactory is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _geminiApiService = geminiApiService ?? throw new InvalidOperationException("GeminiApiService is not initialized.");
            _tagCatalogService = tagCatalogService ?? throw new InvalidOperationException("TagCatalogService is not initialized.");
            _starterTemplateCatalogService = starterTemplateCatalogService ?? throw new InvalidOperationException("StarterTemplateCatalogService is not initialized.");
            _appSettingsService = appSettingsService ?? throw new InvalidOperationException("AppSettingsService is not initialized.");
            _aiWindowFactory = aiWindowFactory ?? throw new InvalidOperationException("AiWindowFactory is not initialized.");

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                OriginalTemplatePath = fullPath;
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
                NativeDocumentPath = Path.Combine(TemplateFolderPath, "content.xamlpackage");
                LayoutSettingsPath = Path.Combine(TemplateFolderPath, "editor-layout.json");
                if (File.Exists(RtfFilePath))
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

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsSaving && !IsEditorLoading);
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            AIInsertTagsCommand = new RelayCommand(o => RunAIInsertTags(), o => !IsAITagsRunning);
            CloseAITagsCommand = new RelayCommand(o => IsAITagsOpen = false);
            OpenStarterTemplatesCommand = new RelayCommand(o => OpenStarterTemplates());
            CloseStarterTemplatesCommand = new RelayCommand(o => IsStarterTemplatesOpen = false);
            ApplySelectedStarterTemplateCommand = new RelayCommand(o => ApplySelectedStarterTemplate(), o => SelectedStarterTemplate != null);
            SelectStarterTemplateCommand = new RelayCommand(o => SelectStarterTemplate(o));
            StatusMessage = Res("EditorLoading");
        }

        private void InitializePageLayoutOptions()
        {
            if (AvailablePageSizes.Count == 0)
            {
                AvailablePageSizes.Add(new TemplateEditorPageSizeOption("a4", "A4", 794, 1123));
                AvailablePageSizes.Add(new TemplateEditorPageSizeOption("letter", "Letter", 816, 1056));
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

        private void OpenStarterTemplates()
        {
            LoadStarterTemplatesIfNeeded();
            IsStarterTemplatesOpen = true;
        }

        private void LoadStarterTemplatesIfNeeded()
        {
            if (StarterTemplates.Count > 0)
                return;

            var templates = _starterTemplateCatalogService?.GetContractTemplates() ?? Array.Empty<StarterTemplateCatalogEntry>();
            StarterTemplates = new ObservableCollection<StarterTemplateCatalogEntry>(templates);

            if (StarterTemplates.Count == 0)
            {
                StarterTemplatesStatus = Res("EditorSamplesEmpty");
                SelectedStarterTemplate = null;
                SelectedStarterTemplateRtf = string.Empty;
                return;
            }

            StarterTemplatesStatus = Res("EditorSamplesHint");
            SelectedStarterTemplate = StarterTemplates[0];
        }

        private void SelectStarterTemplate(object? parameter)
        {
            if (parameter is StarterTemplateCatalogEntry entry)
                SelectedStarterTemplate = entry;
        }

        private void LoadSelectedStarterTemplatePreview()
        {
            if (SelectedStarterTemplate == null)
            {
                SelectedStarterTemplateRtf = string.Empty;
                return;
            }

            var rtf = _starterTemplateCatalogService?.LoadTemplateRtf(SelectedStarterTemplate);
            if (string.IsNullOrWhiteSpace(rtf))
            {
                SelectedStarterTemplateRtf = string.Empty;
                StarterTemplatesStatus = Res("EditorSamplesLoadError");
                return;
            }

            SelectedStarterTemplateRtf = rtf;
            StarterTemplatesStatus = Res("EditorSamplesHint");
        }

        private void ApplySelectedStarterTemplate()
        {
            if (!PolicyService.EnsureWriteAllowed("застосувати стартовий шаблон"))
                return;

            if (SelectedStarterTemplate == null)
                return;

            var rtf = _starterTemplateCatalogService?.LoadTemplateRtf(SelectedStarterTemplate);
            if (string.IsNullOrWhiteSpace(rtf))
            {
                StarterTemplatesStatus = Res("EditorSamplesLoadError");
                return;
            }

            RequestApplyStarterTemplate?.Invoke(rtf);
            IsStarterTemplatesOpen = false;
            StatusMessage = ResF("EditorStarterApplied", SelectedStarterTemplate.Title);
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
                var xamlPackageContent = RequestGetXamlPackageContent?.Invoke();
                var rtfContent = RequestGetRtfContent?.Invoke();
                if ((xamlPackageContent == null || xamlPackageContent.Length == 0)
                    && string.IsNullOrEmpty(rtfContent))
                {
                    StatusMessage = Res("EditorErrEmpty");
                    return;
                }

                if (xamlPackageContent != null && xamlPackageContent.Length > 0)
                    SafeFileService.WriteBytesAtomic(NativeDocumentPath, xamlPackageContent);

                if (!string.IsNullOrEmpty(rtfContent))
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    var ansiEncoding = Encoding.GetEncoding(1252);
                    SafeFileService.WriteTextAtomic(RtfFilePath, rtfContent, ansiEncoding);
                }

                SavePersistedPageLayout();
                StatusMessage = Res("EditorSaved");
                LastSavedAt = DateTime.Now;
                IsDirty = false;
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
            StatusMessage = LastSavedAt.HasValue
                ? ResF("EditorLastSavedFmt", LastSavedAt.Value.ToString("HH:mm"))
                : Res("EditorReady");
            CommandManager.InvalidateRequerySuggested();
        }

        public void MarkDirty()
        {
            if (IsEditorLoading)
                return;

            if (!IsDirty)
                IsDirty = true;

            StatusMessage = Res("EditorUnsaved");
        }

        private async void RunAIInsertTags()
        {
            if (!PolicyService.EnsureWriteAllowed("змінити шаблон через AI"))
                return;

            var geminiService = _geminiApiService;
            if (geminiService == null || !geminiService.IsConfigured)
            {
                AITagsStatus = Res("AIChatNoModel");
                IsAITagsOpen = true;
                return;
            }

            var plainText = RequestGetPlainText?.Invoke();
            if (string.IsNullOrWhiteSpace(plainText))
            {
                AITagsStatus = "Документ порожній. Спочатку напишіть текст шаблону.";
                IsAITagsOpen = true;
                return;
            }

            IsAITagsRunning = true;
            AITagsStatus = Res("AIChatThinking");
            IsAITagsOpen = true;

            try
            {
                var allTags = _tagCatalogService?.GetAllTagDefinitions() ?? new List<TagEntry>();
                var tagListSb = new StringBuilder();
                foreach (var t in allTags)
                    tagListSb.AppendLine($"  {t.Tag} — {t.Description}");

                var prompt = new StringBuilder();
                prompt.AppendLine("You are a smart document analyzer for Czech/Slovak employment contract templates.");
                prompt.AppendLine("Read the ENTIRE document carefully and find ALL places where template tags should be inserted.");
                prompt.AppendLine();
                prompt.AppendLine("The document has blank placeholders (em-dash '—', en-dash '–', underscores '___', or just empty space after a label).");
                prompt.AppendLine("Some positions may already have ${...} tags — leave those unchanged.");
                prompt.AppendLine();
                prompt.AppendLine("Available template tags (use ONLY these exact tag names):");
                prompt.Append(tagListSb);
                prompt.AppendLine();
                prompt.AppendLine("Return ONLY a valid JSON array. Each element:");
                prompt.AppendLine("  \"context_before\" — 2-6 words of text immediately BEFORE the placeholder (the label, e.g. 'zaměstnanec:' or 'č. dokladu:')");
                prompt.AppendLine("  \"replace_what\"   — the EXACT placeholder characters (e.g. '—' or '–' or '___'), copy exactly as in the document");
                prompt.AppendLine("  \"tag\"            — tag name from the list above");
                prompt.AppendLine();
                prompt.AppendLine("Example for a contract like:");
                prompt.AppendLine("  zaměstnanec:   —   , č. dokladu:  —  ,");
                prompt.AppendLine("  narozen/á:  —  ,    místo narození:  —  ,");
                prompt.AppendLine("  Druh práce (funkce):  —  .");
                prompt.AppendLine("Return:");
                prompt.AppendLine("[");
                prompt.AppendLine("  {\"context_before\": \"zaměstnanec:\",        \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_FullName\"},");
                prompt.AppendLine("  {\"context_before\": \"č. dokladu:\",         \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_PassportNumber\"},");
                prompt.AppendLine("  {\"context_before\": \"narozen/á:\",          \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_BirthDate\"},");
                prompt.AppendLine("  {\"context_before\": \"místo narození:\",      \"replace_what\": \"—\", \"tag\": \"EMPLOYEE_PassportCity\"},");
                prompt.AppendLine("  {\"context_before\": \"Druh práce (funkce):\",\"replace_what\": \"—\", \"tag\": \"EMPLOYEE_Position\"}");
                prompt.AppendLine("]");
                prompt.AppendLine();
                prompt.AppendLine("Critical rules:");
                prompt.AppendLine("- 'context_before' must be text that actually appears in the document, immediately before the placeholder");
                prompt.AppendLine("- 'replace_what' must be the EXACT placeholder character(s) as they appear in the document — copy them exactly");
                prompt.AppendLine("- Analyze the WHOLE document, not just the beginning");
                prompt.AppendLine("- Do NOT replace structural/legal text, article titles, or real content");
                prompt.AppendLine("- Skip any position that already has ${...}");
                prompt.AppendLine("- Cover salary, dates, addresses, positions, contract type — everything that needs data");
                prompt.AppendLine("- If no placeholders found, return []");
                prompt.AppendLine();
                prompt.AppendLine("IMPORTANT — Czech employment contract field mapping (use EXACTLY these tags for these labels):");
                prompt.AppendLine("  'Zaměstnavatel:'                        → AGENCY_Name  (legal employer = the employment agency)");
                prompt.AppendLine("  'se sídlem:', 'sídlem:'                 → AGENCY_FullAddress  (agency address)");
                prompt.AppendLine("  'IČO:', 'IČ:'                           → AGENCY_ICO");
                prompt.AppendLine("  'zaměstnanec:', 'Zaměstnanec:'          → EMPLOYEE_FullName");
                prompt.AppendLine("  'č. dokladu:', 'číslo dokladu:'         → EMPLOYEE_PassportNumber");
                prompt.AppendLine("  'typ cestovního dokladu:'               → EMPLOYEE_PrimaryDocumentType");
                prompt.AppendLine("  'číslo a typ cestovního dokladu:'       → EMPLOYEE_PrimaryDocumentType + EMPLOYEE_PassportNumber");
                prompt.AppendLine("  'narozen/á:', 'datum narození:'         → EMPLOYEE_BirthDate");
                prompt.AppendLine("  'místo narození:'                       → EMPLOYEE_PassportCity");
                prompt.AppendLine("  'státní občanství:'                     → EMPLOYEE_Citizenship");
                prompt.AppendLine("  'země vydání:', 'stát vydání:'          → EMPLOYEE_IssuingCountry");
                prompt.AppendLine("  'typ pobytového dokladu:'               → EMPLOYEE_ResidenceDocumentType");
                prompt.AppendLine("  'bydliště v ČR:', 'adresa v ČR:'        → EMPLOYEE_LocalAddress_Full  (employee's Czech Republic address)");
                prompt.AppendLine("  'trvalé bydliště:', 'adresa v zemi původu:' → EMPLOYEE_AbroadAddress_Full  (employee's home country address)");
                prompt.AppendLine("  'Druh práce', 'druh práce (funkce):'    → EMPLOYEE_Position");
                prompt.AppendLine("  'Místo výkonu práce:'                   → EMPLOYEE_WorkAddress");
                prompt.AppendLine("  'uživatel:', 'Uživatel:'                → COMPANY_Name  (the user company where employee actually works)");
                prompt.AppendLine("  'Den nástupu do práce:', 'od:'          → EMPLOYEE_StartDate");
                prompt.AppendLine("  'základní mzda', 'měsíční mzda'         → EMPLOYEE_SalaryBrutto");
                prompt.AppendLine("  'hodinová mzda', 'hodinový výdělek'     → EMPLOYEE_HourlySalary");
                prompt.AppendLine("  'V [city] dne', 'dne' (signature line)  → EMPLOYEE_ContractSignDate  (signing date, NOT start date)");
                prompt.AppendLine("  'zastoupený/á:'                         → leave as-is (real person's name, do not replace)");
                prompt.AppendLine();
                prompt.AppendLine("Document text (analyze completely):");
                prompt.AppendLine(plainText);

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var response = await geminiService.ChatAsync(prompt.ToString(), null, cts.Token);

                var replacements = new List<(string ContextBefore, string ReplaceWhat, string Tag)>();
                var jsonMatch = Regex.Match(response, @"\[[\s\S]*?\]", RegexOptions.Singleline);
                if (jsonMatch.Success)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(jsonMatch.Value);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var contextBefore = elem.TryGetProperty("context_before", out var cb) ? (cb.GetString() ?? string.Empty) : string.Empty;
                            var replaceWhat = elem.TryGetProperty("replace_what", out var rw) ? (rw.GetString() ?? string.Empty) : string.Empty;
                            var tag = elem.TryGetProperty("tag", out var tg) ? (tg.GetString()?.Trim() ?? string.Empty) : string.Empty;

                            if (!string.IsNullOrEmpty(replaceWhat) && !string.IsNullOrEmpty(tag)
                                && allTags.Any(t => t.Tag == tag))
                                replacements.Add((contextBefore, replaceWhat, $"${{{tag}}}"));
                        }
                    }
                    catch (Exception ex) { LoggingService.LogWarning("TemplateEditorViewModel.RunAIInsertTags", $"AI JSON parse error: {ex.Message}"); }
                }

                if (replacements.Count == 0)
                {
                    AITagsStatus = "AI не знайшов місць для тегів. Переконайтесь що в документі є порожні заглушки (—, ___, пробіли після лейблів).";
                }
                else
                {
                    RequestReplaceTagsInDocument?.Invoke(replacements);

                    var sb = new StringBuilder();
                    sb.AppendLine($"✅ Вставлено {replacements.Count} тегів:");
                    foreach (var (ctx, what, tag) in replacements)
                        sb.AppendLine($"  • після \"{ctx}\" → {tag}");
                    AITagsStatus = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                AITagsStatus = $"Помилка: {ex.Message}";
                LoggingService.LogError("TemplateEditorViewModel.RunAIInsertTags", ex);
            }
            finally
            {
                IsAITagsRunning = false;
            }
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

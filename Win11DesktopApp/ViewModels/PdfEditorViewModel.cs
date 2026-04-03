using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharp.Pdf.IO;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class PdfPlacementAlignmentOption
    {
        public string Key { get; set; } = "left";
        public string Label { get; set; } = string.Empty;
    }

    public class PdfEditorModeOption
    {
        public string Key { get; set; } = "overlay";
        public string Label { get; set; } = string.Empty;
    }

    public class PdfFormFieldBindingViewModel : INotifyPropertyChanged
    {
        public PdfFormFieldBinding Model { get; }

        public PdfFormFieldBindingViewModel(PdfFormFieldBinding model)
        {
            Model = model;
        }

        public string FieldName => Model.FieldName;
        public string FieldType
        {
            get => Model.FieldType;
            set
            {
                Model.FieldType = value;
                OnPropertyChanged(nameof(FieldType));
            }
        }

        public string TemplateText
        {
            get => Model.TemplateText;
            set
            {
                Model.TemplateText = value;
                OnPropertyChanged(nameof(TemplateText));
                OnPropertyChanged(nameof(DisplayText));
            }
        }

        public int Page => Model.Page;
        public double X => Model.X;
        public double Y => Model.Y;
        public double Width => Model.Width;
        public double Height => Model.Height;
        public bool HasBounds => Page >= 0 && Width > 0 && Height > 0;
        public string LocationText => Page >= 0
            ? $"P{Page + 1} | X {Math.Round(X, 0)} Y {Math.Round(Y, 0)}"
            : "P?";

        public string DisplayText
        {
            get
            {
                var text = TemplateText?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(text) ? string.Empty : (text.Length > 60 ? text[..57] + "..." : text);
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PdfPlacementViewModel : INotifyPropertyChanged
    {
        public PdfTagPlacement Model { get; }

        public PdfPlacementViewModel(PdfTagPlacement model)
        {
            Model = model;
        }

        private static string Res(string key)
        {
            try { return Application.Current.FindResource(key) as string ?? key; }
            catch { return key; }
        }

        public string Kind => string.IsNullOrWhiteSpace(Model.Kind) ? "tag" : Model.Kind;
        public bool IsInlineText => string.Equals(Kind, "inline_text", StringComparison.OrdinalIgnoreCase);
        public bool IsField => string.Equals(Kind, "field", StringComparison.OrdinalIgnoreCase);
        public bool IsTemplatePlacement => IsInlineText || IsField;
        public string Tag => Model.Tag;
        public string TemplateText
        {
            get => Model.TemplateText;
            set
            {
                Model.TemplateText = value;
                OnPropertyChanged(nameof(TemplateText));
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(OverlayText));
            }
        }
        public string Description => Model.Description;
        public int Page { get => Model.Page; set { Model.Page = value; OnPropertyChanged(nameof(Page)); } }
        public double X
        {
            get => Model.X;
            set { Model.X = value; OnPropertyChanged(nameof(X)); }
        }
        public double Y
        {
            get => Model.Y;
            set { Model.Y = value; OnPropertyChanged(nameof(Y)); }
        }
        public double FontSize
        {
            get => Model.FontSize;
            set { Model.FontSize = value; OnPropertyChanged(nameof(FontSize)); }
        }
        public string FontFamily
        {
            get => Model.FontFamily;
            set { Model.FontFamily = value; OnPropertyChanged(nameof(FontFamily)); }
        }
        public double MaxWidth
        {
            get => Model.MaxWidth;
            set { Model.MaxWidth = value; OnPropertyChanged(nameof(MaxWidth)); }
        }
        public double BoxHeight
        {
            get => Model.BoxHeight;
            set { Model.BoxHeight = value; OnPropertyChanged(nameof(BoxHeight)); }
        }
        public string TextAlign
        {
            get => string.IsNullOrWhiteSpace(Model.TextAlign) ? "left" : Model.TextAlign;
            set { Model.TextAlign = value; OnPropertyChanged(nameof(TextAlign)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string DisplayLabel => IsField
            ? Res("PdfFieldLabel")
            : IsInlineText
                ? Res("PdfInlineTextRowLabel")
                : $"${{{Tag}}}";

        public string OverlayText
        {
            get
            {
                if (!IsTemplatePlacement)
                    return $"${{{Tag}}}";

                var text = TemplateText?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    return IsField ? Res("PdfFieldTemplateHint") : Res("PdfInlineTemplateHint");

                return text.Length > 60 ? text[..57] + "..." : text;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class PdfEditorViewModel : ViewModelBase
    {
        private readonly string _firmName;
        private readonly TemplateEntry _template;
        private readonly TemplateService _templateService;
        private readonly string _pdfFilePath;
        private readonly string _tagMapPath;
        private bool _templateUnavailable;
        private bool _templateUnavailableNotified;
        private bool _navigateBackScheduled;
        private bool _formFieldsLoaded;
        private bool _formFieldsLoadStarted;

        private List<BitmapSource> _pageImages = new();

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

        public string Title => ResF("PdfEditorTitle", _template.Name);

        private int _pageCount;
        public int PageCount
        {
            get => _pageCount;
            set => SetProperty(ref _pageCount, value);
        }

        private int _currentPageIndex;
        public int CurrentPageIndex
        {
            get => _currentPageIndex;
            set
            {
                if (SetProperty(ref _currentPageIndex, value))
                {
                    OnPropertyChanged(nameof(CurrentPageDisplay));
                    OnPropertyChanged(nameof(CurrentPageImage));
                    OnPropertyChanged(nameof(CurrentPagePlacements));
                }
            }
        }

        public int CurrentPageDisplay => _currentPageIndex + 1;

        public BitmapSource? CurrentPageImage =>
            _currentPageIndex >= 0 && _currentPageIndex < _pageImages.Count
                ? _pageImages[_currentPageIndex]
                : null;

        private string _pdfMode = "overlay";
        public string PdfMode
        {
            get => _pdfMode;
            set
            {
                var normalized = string.Equals(value, "form", StringComparison.OrdinalIgnoreCase) ? "form" : "overlay";
                if (SetProperty(ref _pdfMode, normalized))
                {
                    if (!IsOverlayMode)
                        SelectedPlacement = null;
                    if (IsOverlayMode)
                        SelectedFormFieldBinding = null;

                    OnPropertyChanged(nameof(IsOverlayMode));
                    OnPropertyChanged(nameof(IsFormMode));

                    if (IsFormMode)
                        _ = EnsurePdfFormModeReadyAsync();
                }
            }
        }

        public bool IsOverlayMode => !string.Equals(PdfMode, "form", StringComparison.OrdinalIgnoreCase);
        public bool IsFormMode => string.Equals(PdfMode, "form", StringComparison.OrdinalIgnoreCase);
        public List<PdfEditorModeOption> AvailablePdfModes { get; } = new()
        {
            new PdfEditorModeOption { Key = "overlay", Label = Res("PdfModeOverlay") },
            new PdfEditorModeOption { Key = "form", Label = Res("PdfModeForm") }
        };

        private ObservableCollection<PdfPlacementViewModel> _allPlacements = new();
        public ObservableCollection<PdfPlacementViewModel> AllPlacements
        {
            get => _allPlacements;
            set => SetProperty(ref _allPlacements, value);
        }

        private ObservableCollection<PdfFormFieldBindingViewModel> _formFieldBindings = new();
        public ObservableCollection<PdfFormFieldBindingViewModel> FormFieldBindings
        {
            get => _formFieldBindings;
            set => SetProperty(ref _formFieldBindings, value);
        }

        private PdfFormFieldBindingViewModel? _selectedFormFieldBinding;
        public PdfFormFieldBindingViewModel? SelectedFormFieldBinding
        {
            get => _selectedFormFieldBinding;
            set
            {
                if (_selectedFormFieldBinding != null)
                    _selectedFormFieldBinding.IsSelected = false;

                if (SetProperty(ref _selectedFormFieldBinding, value))
                {
                    if (value != null)
                    {
                        value.IsSelected = true;

                        if (value.Page >= 0 && value.Page < PageCount && value.Page != CurrentPageIndex)
                            CurrentPageIndex = value.Page;

                        CoordinateText = value.HasBounds
                            ? $"Field: {value.LocationText} | {Math.Round(value.Width, 0)} x {Math.Round(value.Height, 0)}"
                            : value.Page >= 0 ? $"Field: P{value.Page + 1}" : string.Empty;
                    }
                    else if (IsFormMode)
                    {
                        ClearCoordinateText();
                    }

                    OnPropertyChanged(nameof(IsFormFieldSelected));
                    RequestRenderOverlays?.Invoke();
                }
            }
        }
        public bool IsFormFieldSelected => SelectedFormFieldBinding != null;

        public IEnumerable<PdfPlacementViewModel> CurrentPagePlacements =>
            _allPlacements.Where(p => p.Page == _currentPageIndex);

        private PdfPlacementViewModel? _selectedPlacement;
        private bool _isLoadingSelection;
        private string _inlineTemplateText = string.Empty;
        private int _inlineTemplateCaretIndex;
        private bool _isInlineTemplateEditorFocused;
        private PdfFormFieldBindingViewModel? _formFieldEditorBinding;
        private int _formFieldCaretIndex;
        private bool _isFormFieldEditorFocused;
        private double _newFieldHeight = 18;
        private string _selectedTextAlign = "left";
        public PdfPlacementViewModel? SelectedPlacement
        {
            get => _selectedPlacement;
            set
            {
                if (_selectedPlacement != null) _selectedPlacement.IsSelected = false;
                if (SetProperty(ref _selectedPlacement, value))
                {
                    if (value != null)
                    {
                        value.IsSelected = true;
                        _isLoadingSelection = true;
                        NewTagFontSize = value.FontSize;
                        NewTagFontFamily = value.FontFamily;
                        NewTagMaxWidth = value.MaxWidth;
                        NewFieldHeight = value.BoxHeight;
                        SelectedTextAlign = value.TextAlign;
                        InlineTemplateText = value.IsTemplatePlacement ? value.TemplateText : string.Empty;
                        _isLoadingSelection = false;
                    }
                    else
                    {
                        _isLoadingSelection = true;
                        InlineTemplateText = string.Empty;
                        _isLoadingSelection = false;
                    }
                    OnPropertyChanged(nameof(IsEditingPlacement));
                    OnPropertyChanged(nameof(IsEditingInlinePlacement));
                    OnPropertyChanged(nameof(IsEditingFieldPlacement));
                    OnPropertyChanged(nameof(IsEditingTemplatePlacement));
                    OnPropertyChanged(nameof(EditingTagLabel));
                    OnPropertyChanged(nameof(WidthEditorLabel));
                    OnPropertyChanged(nameof(TemplateEditorLabel));
                }
            }
        }

        public bool IsEditingPlacement => _selectedPlacement != null;
        public bool IsEditingInlinePlacement => _selectedPlacement?.IsInlineText == true;
        public bool IsEditingFieldPlacement => _selectedPlacement?.IsField == true;
        public bool IsEditingTemplatePlacement => _selectedPlacement?.IsTemplatePlacement == true;
        public string EditingTagLabel => _selectedPlacement == null
            ? string.Empty
            : _selectedPlacement.IsField
                ? Res("PdfFieldLabel")
                : _selectedPlacement.IsInlineText
                ? Res("PdfInlineTextRowLabel")
                : $"${{{_selectedPlacement.Tag}}}";
        public string WidthEditorLabel => IsEditingFieldPlacement ? Res("PdfFieldWidth") : Res("EditorMaxWidth");
        public string TemplateEditorLabel => IsEditingFieldPlacement ? Res("PdfFieldTemplate") : Res("PdfInlineTemplate");

        public string InlineTemplateText
        {
            get => _inlineTemplateText;
            set
            {
                if (!SetProperty(ref _inlineTemplateText, value))
                    return;

                if (!_isLoadingSelection && _selectedPlacement?.IsTemplatePlacement == true)
                {
                    _selectedPlacement.TemplateText = value;
                    RequestRenderOverlays?.Invoke();
                }
            }
        }

        public int InlineTemplateCaretIndex
        {
            get => _inlineTemplateCaretIndex;
            set => SetProperty(ref _inlineTemplateCaretIndex, value);
        }

        public bool IsInlineTemplateEditorFocused
        {
            get => _isInlineTemplateEditorFocused;
            set => SetProperty(ref _isInlineTemplateEditorFocused, value);
        }

        public int FormFieldCaretIndex
        {
            get => _formFieldCaretIndex;
            set => SetProperty(ref _formFieldCaretIndex, value);
        }

        public bool IsFormFieldEditorFocused
        {
            get => _isFormFieldEditorFocused;
            set => SetProperty(ref _isFormFieldEditorFocused, value);
        }

        public double NewFieldHeight
        {
            get => _newFieldHeight;
            set
            {
                if (SetProperty(ref _newFieldHeight, value) && !_isLoadingSelection && _selectedPlacement?.IsField == true)
                    _selectedPlacement.BoxHeight = value;
            }
        }

        public string SelectedTextAlign
        {
            get => _selectedTextAlign;
            set
            {
                if (SetProperty(ref _selectedTextAlign, value) && !_isLoadingSelection && _selectedPlacement?.IsField == true)
                    _selectedPlacement.TextAlign = value;
            }
        }

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

        private double _newTagFontSize = 10;
        public double NewTagFontSize
        {
            get => _newTagFontSize;
            set
            {
                if (SetProperty(ref _newTagFontSize, value) && !_isLoadingSelection && _selectedPlacement != null)
                    _selectedPlacement.FontSize = value;
            }
        }

        private string _newTagFontFamily = "Arial";
        public string NewTagFontFamily
        {
            get => _newTagFontFamily;
            set
            {
                if (SetProperty(ref _newTagFontFamily, value) && !_isLoadingSelection && _selectedPlacement != null)
                    _selectedPlacement.FontFamily = value;
            }
        }

        private double _newTagMaxWidth;
        public double NewTagMaxWidth
        {
            get => _newTagMaxWidth;
            set
            {
                if (SetProperty(ref _newTagMaxWidth, value) && !_isLoadingSelection && _selectedPlacement != null)
                    _selectedPlacement.MaxWidth = value;
            }
        }

        public List<string> AvailableFonts { get; } = new() { "Arial", "Times New Roman", "Courier New", "Verdana", "Calibri", "Tahoma" };
        public List<PdfPlacementAlignmentOption> AvailableTextAlignments { get; } = new()
        {
            new PdfPlacementAlignmentOption { Key = "left", Label = Res("EditorAlignLeft") },
            new PdfPlacementAlignmentOption { Key = "center", Label = Res("EditorAlignCenter") },
            new PdfPlacementAlignmentOption { Key = "right", Label = Res("EditorAlignRight") }
        };

        private double _zoomLevel = 1.0;
        public double ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                var clamped = Math.Clamp(value, 0.25, 4.0);
                if (SetProperty(ref _zoomLevel, clamped))
                {
                    OnPropertyChanged(nameof(ZoomPercent));
                    RequestRenderOverlays?.Invoke();
                }
            }
        }

        public string ZoomPercent => $"{(int)(ZoomLevel * 100)}%";

        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand ZoomResetCommand { get; }
        public ICommand DeselectCommand { get; }

        private bool _showGrid;
        public bool ShowGrid
        {
            get => _showGrid;
            set { if (SetProperty(ref _showGrid, value)) RequestRenderOverlays?.Invoke(); }
        }

        private bool _snapToGrid = true;
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => SetProperty(ref _snapToGrid, value);
        }

        private double _gridSpacingPt = 12;
        public double GridSpacingPt
        {
            get => _gridSpacingPt;
            set { if (SetProperty(ref _gridSpacingPt, value)) RequestRenderOverlays?.Invoke(); }
        }

        private string _coordinateText = string.Empty;
        public string CoordinateText
        {
            get => _coordinateText;
            set => SetProperty(ref _coordinateText, value);
        }

        public void NudgeSelected(double dxPercent, double dyPercent)
        {
            if (_selectedPlacement == null) return;
            _selectedPlacement.X = Math.Clamp(_selectedPlacement.X + dxPercent, 0, 1);
            _selectedPlacement.Y = Math.Clamp(_selectedPlacement.Y + dyPercent, 0, 1);
            UpdateCoordinateText(_selectedPlacement);
            RequestRenderOverlays?.Invoke();
        }

        public double SnapYPercent(double yPercent)
        {
            if (!_snapToGrid || _pdfPageHeight <= 0 || _gridSpacingPt <= 0) return yPercent;
            double yPt = yPercent * _pdfPageHeight;
            double snapped = Math.Round(yPt / _gridSpacingPt) * _gridSpacingPt;
            return Math.Clamp(snapped / _pdfPageHeight, 0, 1);
        }

        public double SnapXPercent(double xPercent)
        {
            if (!_snapToGrid || _pdfPageWidth <= 0 || _gridSpacingPt <= 0) return xPercent;
            double xPt = xPercent * _pdfPageWidth;
            double snapped = Math.Round(xPt / _gridSpacingPt) * _gridSpacingPt;
            return Math.Clamp(snapped / _pdfPageWidth, 0, 1);
        }

        public void UpdateCoordinateText(PdfPlacementViewModel p)
        {
            double xPt = Math.Round(p.X * _pdfPageWidth, 1);
            double yPt = Math.Round(p.Y * _pdfPageHeight, 1);
            CoordinateText = $"X: {xPt}pt  Y: {yPt}pt";
        }

        public void ClearCoordinateText() => CoordinateText = string.Empty;

        private double _pdfPageWidth;
        public double PdfPageWidth
        {
            get => _pdfPageWidth;
            set => SetProperty(ref _pdfPageWidth, value);
        }

        private double _pdfPageHeight;
        public double PdfPageHeight
        {
            get => _pdfPageHeight;
            set => SetProperty(ref _pdfPageHeight, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PlaceTagCommand { get; }
        public ICommand AddInlineTextRowCommand { get; }
        public ICommand AddFieldCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand RemovePlacementCommand { get; }

        public Action<TagEntry>? RequestPlaceTagOnClick { get; set; }
        public Action? RequestPlaceInlineTextRowOnClick { get; set; }
        public Action? RequestPlaceFieldOnClick { get; set; }
        public Action<int>? RequestFocusInlineTemplateEditor { get; set; }
        public Action<PdfFormFieldBindingViewModel, int>? RequestFocusFormFieldEditor { get; set; }

        /// <summary>Raised when overlays need to be re-rendered in the View.</summary>
        public event Action? RequestRenderOverlays;

        public PdfEditorViewModel(string firmName, TemplateEntry template, TemplateService? templateService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? App.TemplateService;
            _pdfFilePath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
            _tagMapPath = Path.ChangeExtension(_pdfFilePath, ".tags.json");

            try
            {
                var allTags = App.TagCatalogService.GetAllTagDefinitions();
                var groups = TagGroupViewModel.BuildTagGroups(allTags);
                TagGroups = TagGroupViewModel.ApplyHiddenTagsFilter(groups, App.AppSettingsService.Settings.HiddenTags);
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            PrevPageCommand = new RelayCommand(o =>
            {
                if (CurrentPageIndex > 0) CurrentPageIndex--;
            }, o => CurrentPageIndex > 0);
            NextPageCommand = new RelayCommand(o =>
            {
                if (CurrentPageIndex < PageCount - 1) CurrentPageIndex++;
            }, o => CurrentPageIndex < PageCount - 1);
            PlaceTagCommand = new RelayCommand(o => OnPlaceTag(o));
            AddInlineTextRowCommand = new RelayCommand(_ => BeginPlaceInlineTextRow());
            AddFieldCommand = new RelayCommand(_ => BeginPlaceField());
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            RemovePlacementCommand = new RelayCommand(o => RemovePlacement(o));
            ZoomInCommand = new RelayCommand(_ => ZoomLevel += 0.25);
            ZoomOutCommand = new RelayCommand(_ => ZoomLevel -= 0.25);
            ZoomResetCommand = new RelayCommand(_ => ZoomLevel = 1.0);
            DeselectCommand = new RelayCommand(_ => SelectedPlacement = null);

            LoadPdf();
            LoadTagMap();
            AttachPlacementCollection(AllPlacements);
        }

        private void LoadPdf()
        {
            try
            {
                if (!File.Exists(_pdfFilePath))
                {
                    MarkTemplateUnavailable(Res("PdfFileNotFound"));
                    return;
                }

                RenderPagesWithDocnet();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("PdfEditorViewModel.LoadPdf", ex);
                MarkTemplateUnavailable(ResF("EditorErrFmt", ex.Message));
            }
        }

        private void RenderPagesWithDocnet()
        {
            _pageImages.Clear();

            // Read real PDF page dimensions in points via PdfSharp
            try
            {
                using var pdfDoc = PdfReader.Open(_pdfFilePath, PdfDocumentOpenMode.Import);
                if (pdfDoc.PageCount > 0)
                {
                    var firstPage = pdfDoc.Pages[0];
                    PdfPageWidth = firstPage.Width.Point;
                    PdfPageHeight = firstPage.Height.Point;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PdfEditor", $"Could not read page dimensions, using A4 defaults. {ex.Message}");
                PdfPageWidth = 595.28;
                PdfPageHeight = 841.89;
            }

            // Render page images with Docnet at higher quality (2.0x scale)
            using var docReader = DocLib.Instance.GetDocReader(_pdfFilePath, new PageDimensions(2));
            PageCount = docReader.GetPageCount();

            for (int i = 0; i < PageCount; i++)
            {
                using var pageReader = docReader.GetPageReader(i);
                var rawBytes = pageReader.GetImage();
                var width = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();

                if (width <= 0 || height <= 0) continue;

                var stride = width * 4;
                var bmp = BitmapSource.Create(
                    width, height, 96, 96,
                    PixelFormats.Bgra32, null,
                    rawBytes, stride);
                bmp.Freeze();
                _pageImages.Add(bmp);
            }

            if (PageCount > 0)
                CurrentPageIndex = 0;
        }

        private void LoadTagMap()
        {
            try
            {
                if (File.Exists(_tagMapPath))
                {
                    var json = SafeFileService.ReadAllText(_tagMapPath, System.Text.Encoding.UTF8);
                    var map = JsonSerializer.Deserialize<PdfTagMap>(json);
                    if (map != null)
                    {
                        PdfMode = map.Mode;

                        AllPlacements = new ObservableCollection<PdfPlacementViewModel>(
                            (map.Placements ?? new List<PdfTagPlacement>()).Select(p => new PdfPlacementViewModel(p)));
                        AttachPlacementCollection(AllPlacements);
                        OnPropertyChanged(nameof(CurrentPagePlacements));

                        MergeFormFieldBindings(map.FormFields ?? new List<PdfFormFieldBinding>());
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("PdfEditorViewModel.LoadExistingTags", ex); }
        }

        private async Task EnsureFormFieldsLoadedAsync()
        {
            if (_formFieldsLoaded || _formFieldsLoadStarted || _templateUnavailable)
                return;

            _formFieldsLoadStarted = true;
            var stopwatch = Stopwatch.StartNew();
            LoggingService.LogInfo("PdfEditorViewModel.LoadPdfFormFields", $"Starting async field detection for {_pdfFilePath}");

            try
            {
                var detected = await Task.Run(() =>
                {
                    var netPdfFields = NetPdfFormHelper.ReadFieldBindings(_pdfFilePath).ToList();
                    if (netPdfFields.Count > 0)
                        return netPdfFields;

                    using var pdfDoc = PdfReader.Open(_pdfFilePath, PdfDocumentOpenMode.Modify);
                    return PdfFormFieldReflectionHelper.EnumerateFields(pdfDoc)
                            .Select(field => new PdfFormFieldBinding
                            {
                                FieldName = field.Name,
                                FieldType = field.FieldType
                            })
                            .ToList();
                });

                if (detected.Count == 0)
                {
                    LoggingService.LogInfo(
                        "PdfEditorViewModel.LoadPdfFormFields",
                        $"Field detection finished in {stopwatch.ElapsedMilliseconds} ms with 0 fields.");
                    _formFieldsLoaded = true;
                    return;
                }

                MergeFormFieldBindings(detected);
                _formFieldsLoaded = true;
                LoggingService.LogInfo(
                    "PdfEditorViewModel.LoadPdfFormFields",
                    $"Field detection finished in {stopwatch.ElapsedMilliseconds} ms with {detected.Count} fields.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("PdfEditorViewModel.LoadPdfFormFields", ex.Message);
            }
            finally
            {
                _formFieldsLoadStarted = false;
            }
        }

        private async Task EnsurePdfFormModeReadyAsync()
        {
            if (!IsFormMode)
                return;

            if (!NetPdfFormHelper.IsJavaRuntimeAvailable())
            {
                await HandleMissingJavaRuntimeAsync();
                return;
            }

            await EnsureFormFieldsLoadedAsync();
        }

        private async Task HandleMissingJavaRuntimeAsync()
        {
            var result = MessageBox.Show(
                Res("PdfJavaRequiredMessage"),
                Res("TitleWarning"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                StatusMessage = Res("PdfJavaDownloading");
                var started = await NetPdfFormHelper.DownloadAndLaunchJavaInstallerAsync();
                if (started)
                {
                    StatusMessage = Res("PdfJavaInstallerStarted");
                    ToastService.Instance.Info(Res("PdfJavaInstallerStarted"));
                }
                else
                {
                    StatusMessage = Res("PdfJavaInstallerFailed");
                    ToastService.Instance.Warning(Res("PdfJavaInstallerFailed"));
                    NetPdfFormHelper.OpenJavaDownloadPage();
                }
            }
            else
            {
                StatusMessage = Res("PdfJavaRequiredMessage");
            }

            if (IsFormMode)
                PdfMode = "overlay";
        }

        private void MergeFormFieldBindings(IEnumerable<PdfFormFieldBinding> sourceBindings)
        {
            var allBindings = FormFieldBindings.Select(f => new PdfFormFieldBinding
                {
                    FieldName = f.FieldName,
                    FieldType = f.FieldType,
                    TemplateText = f.TemplateText,
                    Page = f.Page,
                    X = f.X,
                    Y = f.Y,
                    Width = f.Width,
                    Height = f.Height
                })
                .Concat(sourceBindings)
                .Where(b => !string.IsNullOrWhiteSpace(b.FieldName))
                .GroupBy(b => b.FieldName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    var withTemplate = g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.TemplateText));
                    var withType = g.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.FieldType));
                    var withBounds = g.FirstOrDefault(HasFieldLocation);
                    return new PdfFormFieldBinding
                    {
                        FieldName = first.FieldName,
                        FieldType = withType?.FieldType ?? first.FieldType,
                        TemplateText = withTemplate?.TemplateText ?? first.TemplateText,
                        Page = withBounds?.Page ?? first.Page,
                        X = withBounds?.X ?? first.X,
                        Y = withBounds?.Y ?? first.Y,
                        Width = withBounds?.Width ?? first.Width,
                        Height = withBounds?.Height ?? first.Height
                    };
                })
                .OrderBy(b => b.FieldName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var merged = new List<PdfFormFieldBindingViewModel>();
            foreach (var binding in allBindings)
            {
                merged.Add(new PdfFormFieldBindingViewModel(new PdfFormFieldBinding
                {
                    FieldName = binding.FieldName,
                    FieldType = binding.FieldType,
                    TemplateText = binding.TemplateText,
                    Page = binding.Page,
                    X = binding.X,
                    Y = binding.Y,
                    Width = binding.Width,
                    Height = binding.Height
                }));
            }

            FormFieldBindings = new ObservableCollection<PdfFormFieldBindingViewModel>(merged);
        }

        private void OnPlaceTag(object? param)
        {
            if (!PolicyService.EnsureWriteAllowed("додати тег у PDF-шаблон"))
                return;

            if (param is TagEntry tag)
            {
                if (SelectedPlacement?.IsTemplatePlacement == true)
                {
                    AppendTagToTemplatePlacement(tag);
                    return;
                }

                if (IsFormMode)
                {
                    AppendTagToFormField(tag);
                    return;
                }

                RequestPlaceTagOnClick?.Invoke(tag);
            }
        }

        private void BeginPlaceInlineTextRow()
        {
            if (!PolicyService.EnsureWriteAllowed("додати текстовий рядок у PDF-шаблон"))
                return;

            RequestPlaceInlineTextRowOnClick?.Invoke();
            StatusMessage = Res("PdfPlaceInlineTextRow");
        }

        private void BeginPlaceField()
        {
            if (!PolicyService.EnsureWriteAllowed("додати поле у PDF-шаблон"))
                return;

            RequestPlaceFieldOnClick?.Invoke();
            StatusMessage = Res("PdfPlaceField");
        }

        public void PlaceTagAtPosition(TagEntry tag, double xPercent, double yPercent)
        {
            if (!PolicyService.EnsureWriteAllowed("розмістити тег у PDF-шаблоні"))
                return;

            var placement = new PdfTagPlacement
            {
                Tag = tag.Tag,
                Description = tag.Description ?? tag.Tag,
                Page = _currentPageIndex,
                X = xPercent,
                Y = yPercent,
                FontSize = NewTagFontSize,
                FontFamily = NewTagFontFamily,
                MaxWidth = NewTagMaxWidth,
                PdfPageWidth = PdfPageWidth,
                PdfPageHeight = PdfPageHeight
            };

            var vm = new PdfPlacementViewModel(placement);
            AllPlacements.Add(vm);
            SelectedPlacement = vm;
            OnPropertyChanged(nameof(CurrentPagePlacements));
            StatusMessage = ResF("PdfTagPlaced", $"${{{tag.Tag}}}", _currentPageIndex + 1);
        }

        public void PlaceInlineTextRowAtPosition(double xPercent, double yPercent)
        {
            if (!PolicyService.EnsureWriteAllowed("розмістити текстовий рядок у PDF-шаблоні"))
                return;

            var placement = new PdfTagPlacement
            {
                Kind = "inline_text",
                Description = Res("PdfInlineTextRowDescription"),
                Page = _currentPageIndex,
                X = xPercent,
                Y = yPercent,
                FontSize = NewTagFontSize,
                FontFamily = NewTagFontFamily,
                MaxWidth = NewTagMaxWidth,
                PdfPageWidth = PdfPageWidth,
                PdfPageHeight = PdfPageHeight
            };

            var vm = new PdfPlacementViewModel(placement);
            AllPlacements.Add(vm);
            SelectedPlacement = vm;
            OnPropertyChanged(nameof(CurrentPagePlacements));
            StatusMessage = ResF("PdfInlineTextRowPlaced", _currentPageIndex + 1);
        }

        public void PlaceFieldAtPosition(double xPercent, double yPercent)
        {
            if (!PolicyService.EnsureWriteAllowed("розмістити поле у PDF-шаблоні"))
                return;

            var placement = new PdfTagPlacement
            {
                Kind = "field",
                Description = Res("PdfFieldDescription"),
                TemplateText = string.Empty,
                Page = _currentPageIndex,
                X = xPercent,
                Y = yPercent,
                FontSize = NewTagFontSize,
                FontFamily = NewTagFontFamily,
                MaxWidth = NewTagMaxWidth > 0 ? NewTagMaxWidth : 160,
                BoxHeight = NewFieldHeight > 0 ? NewFieldHeight : 18,
                TextAlign = SelectedTextAlign,
                PdfPageWidth = PdfPageWidth,
                PdfPageHeight = PdfPageHeight
            };

            var vm = new PdfPlacementViewModel(placement);
            AllPlacements.Add(vm);
            SelectedPlacement = vm;
            OnPropertyChanged(nameof(CurrentPagePlacements));
            RequestFocusInlineTemplateEditor?.Invoke(0);
            StatusMessage = ResF("PdfFieldPlaced", _currentPageIndex + 1);
        }

        /// <summary>
        /// Updates a placement's position after drag. Called by the View.
        /// </summary>
        public void UpdatePlacementPosition(PdfPlacementViewModel placement, double newXPercent, double newYPercent)
        {
            if (!PolicyService.EnsureWriteAllowed("змінити розташування тегу в PDF-шаблоні"))
                return;

            placement.X = Math.Clamp(newXPercent, 0, 1);
            placement.Y = Math.Clamp(newYPercent, 0, 1);
            RequestRenderOverlays?.Invoke();
        }

        private void RemovePlacement(object? param)
        {
            if (!PolicyService.EnsureWriteAllowed("видалити тег із PDF-шаблону"))
                return;

            if (param is PdfPlacementViewModel p)
            {
                AllPlacements.Remove(p);
                if (SelectedPlacement == p) SelectedPlacement = null;
                OnPropertyChanged(nameof(CurrentPagePlacements));
                RequestRenderOverlays?.Invoke();
                StatusMessage = ResF("PdfTagRemoved", p.DisplayLabel);
            }
        }

        private void CopyTag(object? param)
        {
            if (param is TagEntry tag)
            {
                var tagText = $"${{{tag.Tag}}}";
                Clipboard.SetText(tagText);
                StatusMessage = ResF("PdfTagCopied", tagText);
            }
        }

        private void Save()
        {
            if (!PolicyService.EnsureWriteAllowed("зберегти PDF-шаблон"))
                return;

            try
            {
                if (_templateUnavailable)
                    return;

                var map = new PdfTagMap
                {
                    Mode = PdfMode,
                    Placements = AllPlacements.Select(p => p.Model).ToList(),
                    FormFields = FormFieldBindings.Select(f => f.Model).ToList()
                };

                SafeFileService.WriteJsonAtomic(
                    _tagMapPath,
                    map,
                    new JsonSerializerOptions { WriteIndented = true },
                    System.Text.Encoding.UTF8);
                StatusMessage = Res("EditorSaved");
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
            }
        }

        private void NavigateBack()
        {
            var company = App.CompanyService.Companies.FirstOrDefault(c => c.Name == _firmName);
            if (company != null)
                App.NavigationService.NavigateTo(new TemplatesViewModel(company));
            else
                App.NavigationService.NavigateTo(new MainViewModel());
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

        private void AttachPlacementCollection(ObservableCollection<PdfPlacementViewModel> placements)
        {
            placements.CollectionChanged -= Placements_CollectionChanged;
            placements.CollectionChanged += Placements_CollectionChanged;
        }

        private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(CurrentPagePlacements));
        }

        private void AppendTagToTemplatePlacement(TagEntry tag)
        {
            if (SelectedPlacement?.IsTemplatePlacement != true)
                return;

            var token = $"${{{tag.Tag}}}";
            var current = SelectedPlacement.TemplateText ?? string.Empty;
            var insertIndex = Math.Clamp(InlineTemplateCaretIndex, 0, current.Length);
            var updated = current.Insert(insertIndex, token);

            InlineTemplateText = updated;
            InlineTemplateCaretIndex = insertIndex + token.Length;
            RequestFocusInlineTemplateEditor?.Invoke(InlineTemplateCaretIndex);
            StatusMessage = ResF("PdfInlineTagInserted", token);
        }

        public void SelectFormField(PdfFormFieldBindingViewModel? binding)
        {
            SelectedFormFieldBinding = binding;
            if (binding != null)
            {
                StatusMessage = binding.HasBounds
                    ? $"Selected field: {binding.FieldName} ({binding.LocationText})"
                    : $"Selected field: {binding.FieldName}";
            }
        }

        public void UpdateFormFieldEditorState(PdfFormFieldBindingViewModel? binding, int caretIndex, bool isFocused)
        {
            _formFieldEditorBinding = binding;
            FormFieldCaretIndex = Math.Max(0, caretIndex);
            IsFormFieldEditorFocused = isFocused;
        }

        private void AppendTagToFormField(TagEntry tag)
        {
            var target = SelectedFormFieldBinding ?? FormFieldBindings.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.TemplateText));
            if (target == null)
            {
                StatusMessage = Res("PdfFormSelectFieldFirst");
                return;
            }

            var token = $"${{{tag.Tag}}}";
            var current = target.TemplateText ?? string.Empty;
            var insertIndex = ReferenceEquals(target, _formFieldEditorBinding)
                ? Math.Clamp(FormFieldCaretIndex, 0, current.Length)
                : current.Length;
            target.TemplateText = current.Insert(insertIndex, token);
            _formFieldEditorBinding = target;
            FormFieldCaretIndex = insertIndex + token.Length;
            SelectFormField(target);
            RequestFocusFormFieldEditor?.Invoke(target, FormFieldCaretIndex);
            StatusMessage = ResF("PdfFormFieldMapped", target.FieldName);
        }

        private static bool HasFieldLocation(PdfFormFieldBinding binding)
            => binding.Page >= 0 && binding.Width > 0 && binding.Height > 0;
    }
}

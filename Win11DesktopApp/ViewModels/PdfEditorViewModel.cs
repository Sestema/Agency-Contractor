using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using PdfSharp.Pdf.IO;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class PdfPlacementViewModel : INotifyPropertyChanged
    {
        public PdfTagPlacement Model { get; }

        public PdfPlacementViewModel(PdfTagPlacement model)
        {
            Model = model;
        }

        public string Tag => Model.Tag;
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

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
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

        private ObservableCollection<PdfPlacementViewModel> _allPlacements = new();
        public ObservableCollection<PdfPlacementViewModel> AllPlacements
        {
            get => _allPlacements;
            set => SetProperty(ref _allPlacements, value);
        }

        public IEnumerable<PdfPlacementViewModel> CurrentPagePlacements =>
            _allPlacements.Where(p => p.Page == _currentPageIndex);

        private PdfPlacementViewModel? _selectedPlacement;
        private bool _isLoadingSelection;
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
                        _isLoadingSelection = false;
                    }
                    OnPropertyChanged(nameof(IsEditingPlacement));
                    OnPropertyChanged(nameof(EditingTagLabel));
                }
            }
        }

        public bool IsEditingPlacement => _selectedPlacement != null;
        public string EditingTagLabel => _selectedPlacement != null ? $"${{{_selectedPlacement.Tag}}}" : "";

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
        public ICommand CopyTagCommand { get; }
        public ICommand RemovePlacementCommand { get; }

        public Action<TagEntry>? RequestPlaceTagOnClick { get; set; }

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
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
            RemovePlacementCommand = new RelayCommand(o => RemovePlacement(o));
            ZoomInCommand = new RelayCommand(_ => ZoomLevel += 0.25);
            ZoomOutCommand = new RelayCommand(_ => ZoomLevel -= 0.25);
            ZoomResetCommand = new RelayCommand(_ => ZoomLevel = 1.0);
            DeselectCommand = new RelayCommand(_ => SelectedPlacement = null);

            LoadPdf();
            LoadTagMap();
        }

        private void LoadPdf()
        {
            try
            {
                if (!File.Exists(_pdfFilePath))
                {
                    StatusMessage = Res("PdfFileNotFound");
                    return;
                }

                RenderPagesWithDocnet();
            }
            catch (Exception ex)
            {
                StatusMessage = ResF("EditorErrFmt", ex.Message);
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
                    var json = File.ReadAllText(_tagMapPath, System.Text.Encoding.UTF8);
                    var map = JsonSerializer.Deserialize<PdfTagMap>(json);
                    if (map?.Placements != null)
                    {
                        AllPlacements = new ObservableCollection<PdfPlacementViewModel>(
                            map.Placements.Select(p => new PdfPlacementViewModel(p)));
                        OnPropertyChanged(nameof(CurrentPagePlacements));
                    }
                }
            }
            catch (Exception ex) { LoggingService.LogError("PdfEditorViewModel.LoadExistingTags", ex); }
        }

        private void OnPlaceTag(object? param)
        {
            if (param is TagEntry tag)
            {
                RequestPlaceTagOnClick?.Invoke(tag);
            }
        }

        public void PlaceTagAtPosition(TagEntry tag, double xPercent, double yPercent)
        {
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

        /// <summary>
        /// Updates a placement's position after drag. Called by the View.
        /// </summary>
        public void UpdatePlacementPosition(PdfPlacementViewModel placement, double newXPercent, double newYPercent)
        {
            placement.X = Math.Clamp(newXPercent, 0, 1);
            placement.Y = Math.Clamp(newYPercent, 0, 1);
            RequestRenderOverlays?.Invoke();
        }

        private void RemovePlacement(object? param)
        {
            if (param is PdfPlacementViewModel p)
            {
                AllPlacements.Remove(p);
                if (SelectedPlacement == p) SelectedPlacement = null;
                OnPropertyChanged(nameof(CurrentPagePlacements));
                RequestRenderOverlays?.Invoke();
                StatusMessage = ResF("PdfTagRemoved", $"${{{p.Tag}}}");
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
            try
            {
                var map = new PdfTagMap
                {
                    Placements = AllPlacements.Select(p => p.Model).ToList()
                };

                var json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_tagMapPath, json, System.Text.Encoding.UTF8);
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
    }
}

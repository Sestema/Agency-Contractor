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

        public string Title => $"PDF: {_template.Name}";

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
        public PdfPlacementViewModel? SelectedPlacement
        {
            get => _selectedPlacement;
            set
            {
                if (_selectedPlacement != null) _selectedPlacement.IsSelected = false;
                SetProperty(ref _selectedPlacement, value);
                if (value != null) value.IsSelected = true;
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
            set => SetProperty(ref _newTagFontSize, value);
        }

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
                TagGroups = TagGroupViewModel.BuildTagGroups(allTags);
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

            LoadPdf();
            LoadTagMap();
        }

        private void LoadPdf()
        {
            try
            {
                if (!File.Exists(_pdfFilePath))
                {
                    StatusMessage = "PDF file not found.";
                    return;
                }

                RenderPagesWithDocnet();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void RenderPagesWithDocnet()
        {
            _pageImages.Clear();

            using var docReader = DocLib.Instance.GetDocReader(_pdfFilePath, new PageDimensions(1.5));
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

                if (i == 0)
                {
                    PdfPageWidth = width;
                    PdfPageHeight = height;
                }
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
                    var json = File.ReadAllText(_tagMapPath);
                    var map = JsonSerializer.Deserialize<PdfTagMap>(json);
                    if (map?.Placements != null)
                    {
                        AllPlacements = new ObservableCollection<PdfPlacementViewModel>(
                            map.Placements.Select(p => new PdfPlacementViewModel(p)));
                        OnPropertyChanged(nameof(CurrentPagePlacements));
                    }
                }
            }
            catch { }
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
                PdfPageWidth = PdfPageWidth,
                PdfPageHeight = PdfPageHeight
            };

            var vm = new PdfPlacementViewModel(placement);
            AllPlacements.Add(vm);
            SelectedPlacement = vm;
            OnPropertyChanged(nameof(CurrentPagePlacements));
            StatusMessage = $"${{{tag.Tag}}} placed on page {_currentPageIndex + 1}";
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
                StatusMessage = $"${{{p.Tag}}} removed";
            }
        }

        private void CopyTag(object? param)
        {
            if (param is TagEntry tag)
            {
                Clipboard.SetText($"${{{tag.Tag}}}");
                StatusMessage = $"${{{tag.Tag}}} copied";
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
                File.WriteAllText(_tagMapPath, json);
                StatusMessage = "Saved!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
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

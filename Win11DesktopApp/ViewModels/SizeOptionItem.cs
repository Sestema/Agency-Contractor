using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One size in settings, with the values that draw its preview.</summary>
    public class SizeOptionItem : ViewModelBase
    {
        private readonly string _titleKey;
        private bool _isSelected;

        private SizeOptionItem(string key, string titleKey, int columns, double tileHeight, double iconSize, double sampleFontSize)
        {
            Key = key;
            _titleKey = titleKey;
            Columns = columns;
            TileHeight = tileHeight;
            IconSize = iconSize;
            SampleFontSize = sampleFontSize;
        }

        public string Key { get; }
        public string Title => Application.Current?.TryFindResource(_titleKey) as string ?? Key;

        /// <summary>Larger sizes fit fewer tiles in a row of the miniature window.</summary>
        public int Columns { get; }
        public double TileHeight { get; }
        public double IconSize { get; }
        public double SampleFontSize { get; }
        public IReadOnlyList<int> PreviewTiles { get; } = Enumerable.Range(0, 10).ToList();

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public void RefreshLocalization() => OnPropertyChanged(nameof(Title));

        public static IEnumerable<SizeOptionItem> CreateAll() => new[]
        {
            new SizeOptionItem("Small", "SizeSmall", 5, 13, 6, 15),
            new SizeOptionItem("Medium", "SizeMedium", 4, 16, 7, 18),
            new SizeOptionItem("Large", "SizeLarge", 3, 19, 9, 21),
            new SizeOptionItem("ExtraLarge", "SizeExtraLarge", 2, 22, 10, 24)
        };
    }
}

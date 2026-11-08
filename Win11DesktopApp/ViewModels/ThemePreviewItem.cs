using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One theme in the settings picker, with the colours of its miniature window.</summary>
    public class ThemePreviewItem : ViewModelBase
    {
        private readonly string _titleKey;
        private bool _isSelected;

        private ThemePreviewItem(string key, string titleKey, string wallStart, string wallEnd,
            string window, string sidebar, string line, string text, string muted, string accent)
        {
            Key = key;
            _titleKey = titleKey;

            var wallpaper = new LinearGradientBrush(ToColor(wallStart), ToColor(wallEnd), new Point(0, 0), new Point(1, 1));
            wallpaper.Freeze();
            Wallpaper = wallpaper;
            Window = ToBrush(window);
            Sidebar = ToBrush(sidebar);
            Line = ToBrush(line);
            Text = ToBrush(text);
            Muted = ToBrush(muted);
            Accent = ToBrush(accent);
        }

        public string Key { get; }
        public string Title => Application.Current?.TryFindResource(_titleKey) as string ?? Key;
        public Brush Wallpaper { get; }
        public Brush Window { get; }
        public Brush Sidebar { get; }
        public Brush Line { get; }
        public Brush Text { get; }
        public Brush Muted { get; }
        public Brush Accent { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public void RefreshLocalization() => OnPropertyChanged(nameof(Title));

        public static IEnumerable<ThemePreviewItem> CreateAll() => new[]
        {
            new ThemePreviewItem("Light", "ThemeLight", "#BFD8FF", "#F4C9DE", "#FFFFFF", "#EEF0F4", "#DEDFE3", "#1D1D1F", "#6E6E73", "#007AFF"),
            new ThemePreviewItem("Custom", "ThemeCustom", "#F3D9A8", "#E2A77A", "#FFF9EC", "#F2E6CE", "#E6D7B8", "#3A2E1C", "#7A6A50", "#B36A2C"),
            new ThemePreviewItem("Glass", "ThemeGlass", "#D9E6FF", "#F9DDED", "#B8FFFFFF", "#66FFFFFF", "#E6FFFFFF", "#1C2430", "#4A5260", "#007AFF"),
            new ThemePreviewItem("DarkWord", "ThemeDarkWord", "#55504A", "#23211E", "#322F2B", "#292723", "#544F46", "#EDE9E1", "#B8B2A6", "#3B8EEA"),
            new ThemePreviewItem("Dark2", "ThemeDark2", "#1B2A55", "#080C1A", "#172033", "#111827", "#2B3652", "#E4ECF7", "#A3ADC2", "#8B5CF6"),
            new ThemePreviewItem("GlassDark", "ThemeGlassDark", "#5C5468", "#3C5664", "#1FFFFFFF", "#12FFFFFF", "#38FFFFFF", "#E8ECF2", "#BCC4CF", "#5FA3D4"),
            new ThemePreviewItem("VantaDark", "ThemeVantaDark", "#3A1E78", "#0A0F24", "#171C38", "#111530", "#2A3358", "#E6E9F5", "#A8B0C7", "#8B5CF6"),
            new ThemePreviewItem("AgencyTeal", "ThemeAgencyTeal", "#0F5E5A", "#061416", "#10282A", "#0B1F21", "#1E4144", "#DDF5F0", "#9DB8B3", "#2DD4BF")
        };

        private static Color ToColor(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        private static Brush ToBrush(string hex)
        {
            var brush = new SolidColorBrush(ToColor(hex));
            brush.Freeze();
            return brush;
        }
    }
}

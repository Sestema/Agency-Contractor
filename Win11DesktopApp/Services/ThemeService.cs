using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Services
{
    public class ThemeService
    {
        private static readonly string[] GlassThemes = { "Glass", "GlassDark" };
        private static readonly string[] DarkThemes = { "Dark2", "DarkWord", "GlassDark", "VantaDark" };
        private readonly AppSettingsService _appSettingsService;

        /// <summary>
        /// Curated Win11-style accent palette. First item is the theme's default (no override).
        /// </summary>
        public static readonly IReadOnlyList<AccentPreset> AccentPresets = new List<AccentPreset>
        {
            new("Default",    ""),
            new("Blue",       "#0067C0"),
            new("Teal",       "#008080"),
            new("Emerald",    "#107C10"),
            new("Mint",       "#2CA9A4"),
            new("Plum",       "#744DA9"),
            new("Violet",     "#5C2D91"),
            new("Pink",       "#C239B3"),
            new("Rose",       "#E3008C"),
            new("Orange",     "#D83B01"),
            new("Amber",      "#CA5010"),
            new("Gold",       "#986F0B"),
            new("Crimson",    "#B4009E"),
            new("Graphite",   "#4A5459")
        };

        public ThemeService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        }

        public void SetTheme(string themeName)
        {
            var dictName = $"Resources/Themes/Theme.{themeName}.xaml";
            var newDict = new ResourceDictionary { Source = new Uri(dictName, UriKind.Relative) };

            var oldDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Resources/Themes/Theme."));

            if (oldDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDict);
            }

            Application.Current.Resources.MergedDictionaries.Add(newDict);

            ApplyBackdrop(Application.Current.MainWindow, themeName);

            // Re-apply accent override (theme just reset AccentBrush to theme default).
            ApplyAccent(_appSettingsService.Settings.AccentColor);

            if (_appSettingsService.Settings.ThemeName != themeName)
            {
                _appSettingsService.Settings.ThemeName = themeName;
                _appSettingsService.SaveSettings();
            }
        }

        /// <summary>
        /// Overrides AccentBrush / AccentDarkBrush / AccentLightBrush with a user-picked color.
        /// Pass empty / null to reset to the theme's built-in accent.
        /// </summary>
        public void SetAccentColor(string hex)
        {
            ApplyAccent(hex);

            if (_appSettingsService.Settings.AccentColor != (hex ?? string.Empty))
            {
                _appSettingsService.Settings.AccentColor = hex ?? string.Empty;
                _appSettingsService.SaveSettings();
            }
        }

        public static void ApplyAccent(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                // Remove any overrides so theme defaults bubble back up.
                var res = Application.Current.Resources;
                res.Remove("AccentBrush");
                res.Remove("AccentDarkBrush");
                res.Remove("AccentLightBrush");
                return;
            }

            if (!TryParseColor(hex, out var baseColor)) return;

            var dark = Darken(baseColor, 0.22);
            var light = Lighten(baseColor, 0.85); // very pale tint for AccentLightBrush

            var res2 = Application.Current.Resources;
            res2["AccentBrush"] = new SolidColorBrush(baseColor) { Opacity = 1.0 };
            res2["AccentDarkBrush"] = new SolidColorBrush(dark);
            res2["AccentLightBrush"] = new SolidColorBrush(light);
        }

        private static bool TryParseColor(string hex, out Color color)
        {
            try
            {
                var obj = ColorConverter.ConvertFromString(hex);
                if (obj is Color c) { color = c; return true; }
            }
            catch { }
            color = Colors.Transparent;
            return false;
        }

        private static Color Darken(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            byte r = (byte)(c.R * (1 - amount));
            byte g = (byte)(c.G * (1 - amount));
            byte b = (byte)(c.B * (1 - amount));
            return Color.FromArgb(c.A, r, g, b);
        }

        private static Color Lighten(Color c, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            byte r = (byte)(c.R + (255 - c.R) * amount);
            byte g = (byte)(c.G + (255 - c.G) * amount);
            byte b = (byte)(c.B + (255 - c.B) * amount);
            return Color.FromArgb(c.A, r, g, b);
        }

        public record AccentPreset(string Name, string Hex);

        public static void ApplyBackdrop(Window window, string themeName)
        {
            if (window == null) return;

            bool isDark = Array.Exists(DarkThemes, t => t.Equals(themeName, StringComparison.OrdinalIgnoreCase));
            bool isGlass = Array.Exists(GlassThemes, t => t.Equals(themeName, StringComparison.OrdinalIgnoreCase));

            // "Custom" (sepia) relies on its warm solid tones. Mica would tint them
            // with the desktop wallpaper and kill the sepia character, so we opt out.
            bool isSolidCharacterTheme =
                string.Equals(themeName, "Custom", StringComparison.OrdinalIgnoreCase)
                || string.Equals(themeName, "VantaDark", StringComparison.OrdinalIgnoreCase);

            if (isGlass)
            {
                AcrylicHelper.EnableBackdrop(window, isDark, AcrylicHelper.BackdropKind.Acrylic);
                return;
            }

            if (isSolidCharacterTheme)
            {
                AcrylicHelper.DisableAcrylic(window);
                AcrylicHelper.ApplyImmersiveDarkMode(window, isDark);
                return;
            }

            if (AcrylicHelper.IsWindows11)
            {
                AcrylicHelper.EnableBackdrop(window, isDark, AcrylicHelper.BackdropKind.Mica);
            }
            else
            {
                AcrylicHelper.DisableAcrylic(window);
                AcrylicHelper.ApplyImmersiveDarkMode(window, isDark);
            }
        }
    }
}

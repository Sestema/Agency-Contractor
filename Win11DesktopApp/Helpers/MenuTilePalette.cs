using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Publishes the home page tile colours as application resources for the active theme.
    /// Each tile keeps its own hues; the theme decides how saturated and bright they are.
    /// </summary>
    internal static class MenuTilePalette
    {
        private sealed record TileTone(double Hue, double EndHue, double AccentHue, double SaturationScale = 1, bool IsAlert = false, bool KeepsHue = false);

        private sealed record ThemeTone(double Saturation, double Lightness, bool IsDark, bool IsCool = false);

        private static readonly Dictionary<string, TileTone> Tiles = new(StringComparer.Ordinal)
        {
            ["employees"] = new(225, 258, 195),
            ["templates"] = new(186, 212, 42),
            ["problems"] = new(38, 12, 12, IsAlert: true),
            ["finances"] = new(148, 178, 45),
            ["report"] = new(203, 232, 275),
            ["archive"] = new(208, 228, 190, SaturationScale: 0.55),
            ["activitylog"] = new(50, 30, 345),
            ["candidates"] = new(316, 338, 30),
            ["aichat"] = new(262, 300, 190),
            ["dashboard"] = new(168, 196, 240),
            ["invoices"] = new(88, 138, 185),
            ["recentlydeleted"] = new(354, 334, 334, IsAlert: true),
            ["news"] = new(282, 322, 205),

            // Weather keeps its natural colours in every theme: the sun must not turn teal.
            ["IconWeatherSun"] = new(50, 26, 18, KeepsHue: true),
            ["IconWeatherCloud"] = new(205, 228, 45, SaturationScale: 0.7, KeepsHue: true),
            ["IconWeatherRain"] = new(208, 240, 195, KeepsHue: true),
            ["IconWeatherSnow"] = new(195, 218, 200, KeepsHue: true),
            ["IconWeatherFog"] = new(212, 226, 210, SaturationScale: 0.4, KeepsHue: true),
            ["IconWeatherStorm"] = new(262, 290, 45, KeepsHue: true)
        };

        private static readonly ThemeTone LightTone = new(82, 50, false);

        private static readonly Dictionary<string, ThemeTone> Themes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Light"] = LightTone,
            ["Glass"] = LightTone,
            ["Custom"] = new(58, 42, false),
            ["Dark2"] = new(80, 58, true),
            ["DarkWord"] = new(62, 56, true),
            ["GlassDark"] = new(60, 58, true),
            ["VantaDark"] = new(85, 60, true),
            ["AgencyTeal"] = new(66, 52, true, IsCool: true)
        };

        public static string GradientKey(string tileId) => $"MenuTileGradient.{tileId}";

        public static string AccentKey(string tileId) => $"MenuTileAccentBrush.{tileId}";

        public static void Apply(string? themeName)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
                return;

            var theme = themeName != null && Themes.TryGetValue(themeName, out var found) ? found : LightTone;
            foreach (var (id, tile) in Tiles)
            {
                resources[GradientKey(id)] = CreateGradient(tile, theme);
                resources[AccentKey(id)] = CreateAccent(tile, theme);
            }
        }

        /// <summary>Covers a start-up that keeps the default Light theme without calling SetTheme.</summary>
        public static void EnsureApplied()
        {
            var resources = Application.Current?.Resources;
            if (resources != null && !resources.Contains(GradientKey("employees")))
                Apply("Light");
        }

        private static Brush CreateGradient(TileTone tile, ThemeTone theme)
        {
            var saturation = tile.IsAlert
                ? Math.Max(theme.Saturation * tile.SaturationScale, 78)
                : theme.Saturation * tile.SaturationScale;

            var brush = new LinearGradientBrush(
                FromHsl(ThemeHue(tile.Hue, tile, theme), saturation, theme.Lightness + 8),
                FromHsl(ThemeHue(tile.EndHue, tile, theme), saturation, theme.Lightness - 8),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();
            return brush;
        }

        private static Brush CreateAccent(TileTone tile, ThemeTone theme)
        {
            var brush = new SolidColorBrush(FromHsl(ThemeHue(tile.AccentHue, tile, theme), 85, theme.IsDark ? 66 : 54));
            brush.Freeze();
            return brush;
        }

        // The cool theme pulls every hue towards teal, except colours that must stay recognisable.
        private static double ThemeHue(double hue, TileTone tile, ThemeTone theme) =>
            theme.IsCool && !tile.IsAlert && !tile.KeepsHue ? 150 + (hue - 150) * 0.55 : hue;

        private static Color FromHsl(double hue, double saturationPercent, double lightnessPercent)
        {
            var h = ((hue % 360) + 360) % 360 / 60;
            var s = saturationPercent / 100;
            var l = lightnessPercent / 100;

            var chroma = (1 - Math.Abs(2 * l - 1)) * s;
            var x = chroma * (1 - Math.Abs(h % 2 - 1));
            var (r, g, b) = (int)h switch
            {
                0 => (chroma, x, 0d),
                1 => (x, chroma, 0d),
                2 => (0d, chroma, x),
                3 => (0d, x, chroma),
                4 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };

            var m = l - chroma / 2;
            return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
        }

        private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);
    }
}

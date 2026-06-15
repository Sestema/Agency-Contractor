using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Baked emerald page background (same layered look, ImageBrush for stable scrolling).
    /// </summary>
    public static class AgencyTealBackgroundBrushes
    {
        private const int BakeWidth = 1920;
        private const int BakeHeight = 1080;

        private static readonly Uri GrainUri = new("pack://application:,,,/Resources/Themes/Assets/agency-teal-grain.png");
        private static readonly string[] OverrideKeys = { "PageBackgroundBrush" };

        private static Brush? _pageBackgroundBrush;
        private static ImageBrush? _grainBrush;

        public static void ApplyToResources(ResourceDictionary? resources = null)
        {
            resources ??= Application.Current.Resources;
            resources["PageBackgroundBrush"] = GetPageBackgroundBrush();
        }

        public static void ClearOverrides(ResourceDictionary? resources = null)
        {
            resources ??= Application.Current.Resources;
            foreach (var key in OverrideKeys)
                resources.Remove(key);
        }

        private static Brush GetPageBackgroundBrush()
        {
            if (_pageBackgroundBrush != null)
                return _pageBackgroundBrush;

            var source = CreateTexturedDrawingBrush(
                Color.FromRgb(0x08, 0x18, 0x12),
                Color.FromRgb(0x0A, 0x1E, 0x1A),
                Color.FromRgb(0x0C, 0x22, 0x1E),
                glowOpacity: 0.58,
                grainOpacity: 0.09);

            _pageBackgroundBrush = BakeDrawingBrush(source);
            return _pageBackgroundBrush;
        }

        private static Brush BakeDrawingBrush(DrawingBrush source)
        {
            try
            {
                if (source.CanFreeze && !source.IsFrozen)
                    source.Freeze();

                var (width, height, dpiX, dpiY) = GetBakeMetrics();

                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                    dc.DrawRectangle(source, null, new Rect(0, 0, width, height));

                var bitmap = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                if (bitmap.CanFreeze)
                    bitmap.Freeze();

                var imageBrush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                if (imageBrush.CanFreeze)
                    imageBrush.Freeze();

                return imageBrush;
            }
            catch
            {
                return CreateFallbackBrush();
            }
        }

        private static (int width, int height, double dpiX, double dpiY) GetBakeMetrics()
        {
            double dpiX = 96;
            double dpiY = 96;

            if (Application.Current.MainWindow != null)
            {
                var dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
                dpiX = dpi.PixelsPerInchX;
                dpiY = dpi.PixelsPerInchY;
            }

            var width = (int)Math.Round(BakeWidth * dpiX / 96d);
            var height = (int)Math.Round(BakeHeight * dpiY / 96d);
            return (Math.Max(width, BakeWidth), Math.Max(height, BakeHeight), dpiX, dpiY);
        }

        private static Brush CreateFallbackBrush()
        {
            var fallback = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            fallback.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x18, 0x12), 0));
            fallback.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x1E, 0x1A), 0.52));
            fallback.GradientStops.Add(new GradientStop(Color.FromRgb(0x0C, 0x22, 0x1E), 1));
            if (fallback.CanFreeze)
                fallback.Freeze();
            return fallback;
        }

        private static DrawingBrush CreateTexturedDrawingBrush(
            Color topLeft,
            Color mid,
            Color bottomRight,
            double glowOpacity,
            double grainOpacity,
            double vignetteOpacity = 0.30)
        {
            var drawingGroup = new DrawingGroup();

            var baseGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            baseGradient.GradientStops.Add(new GradientStop(topLeft, 0));
            baseGradient.GradientStops.Add(new GradientStop(mid, 0.52));
            baseGradient.GradientStops.Add(new GradientStop(bottomRight, 1));
            if (baseGradient.CanFreeze)
                baseGradient.Freeze();

            drawingGroup.Children.Add(new GeometryDrawing(
                baseGradient,
                null,
                new RectangleGeometry(new Rect(0, 0, 1, 1))));

            drawingGroup.Children.Add(CreateLayer(CreateVignetteBrush(), vignetteOpacity));
            drawingGroup.Children.Add(CreateLayer(CreateCornerGlowBrush(), glowOpacity));
            drawingGroup.Children.Add(CreateLayer(CreateSecondaryGlowBrush(), 0.35));
            drawingGroup.Children.Add(CreateLayer(GetGrainBrush(), grainOpacity));

            if (drawingGroup.CanFreeze)
                drawingGroup.Freeze();

            return new DrawingBrush(drawingGroup)
            {
                Stretch = Stretch.UniformToFill,
                Viewport = new Rect(0, 0, 1, 1),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox
            };
        }

        private static Drawing CreateLayer(Brush brush, double opacity)
        {
            var layer = new DrawingGroup { Opacity = opacity };
            layer.Children.Add(new GeometryDrawing(
                brush,
                null,
                new RectangleGeometry(new Rect(0, 0, 1, 1))));
            if (layer.CanFreeze)
                layer.Freeze();
            return layer;
        }

        private static Brush CreateVignetteBrush()
        {
            var vignette = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.95,
                RadiusY = 0.95
            };
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.58));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
            if (vignette.CanFreeze)
                vignette.Freeze();
            return vignette;
        }

        private static Brush CreateCornerGlowBrush()
        {
            var cornerGlow = new RadialGradientBrush
            {
                Center = new Point(1, 0),
                GradientOrigin = new Point(1, 0),
                RadiusX = 0.82,
                RadiusY = 0.72
            };
            cornerGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x66, 0x2D, 0xD4, 0xBF), 0));
            cornerGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x22, 0x14, 0xB8, 0xA6), 0.45));
            cornerGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x2D, 0xD4, 0xBF), 1));
            if (cornerGlow.CanFreeze)
                cornerGlow.Freeze();
            return cornerGlow;
        }

        private static Brush CreateSecondaryGlowBrush()
        {
            var secondaryGlow = new RadialGradientBrush
            {
                Center = new Point(0, 1),
                GradientOrigin = new Point(0, 1),
                RadiusX = 0.55,
                RadiusY = 0.45
            };
            secondaryGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0x28, 0x15, 0x80, 0x70), 0));
            secondaryGlow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x15, 0x80, 0x70), 1));
            if (secondaryGlow.CanFreeze)
                secondaryGlow.Freeze();
            return secondaryGlow;
        }

        private static ImageBrush GetGrainBrush()
        {
            if (_grainBrush != null)
                return _grainBrush;

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = GrainUri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();

            var grain = new ImageBrush(image)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 512, 512),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            if (grain.CanFreeze)
                grain.Freeze();

            _grainBrush = grain;
            return grain;
        }
    }
}

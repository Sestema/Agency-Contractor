using System;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Controls
{
    /// <summary>Round flag for a language code ("uk", "cs", "ru", "en"), drawn once as static vector shapes.</summary>
    public class LanguageFlag : FrameworkElement
    {
        public static readonly DependencyProperty CodeProperty =
            DependencyProperty.Register(nameof(Code), typeof(string), typeof(LanguageFlag),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Code
        {
            get => (string)GetValue(CodeProperty);
            set => SetValue(CodeProperty, value);
        }

        private static readonly Brush Blue = Solid("#2F6FD0");
        private static readonly Brush Yellow = Solid("#FFD43B");
        private static readonly Brush White = Solid("#F4F5F7");
        private static readonly Brush Red = Solid("#D7263D");
        private static readonly Brush NavyBlue = Solid("#1E4E9C");
        private static readonly Brush RoyalBlue = Solid("#2F5FB8");
        private static readonly Brush DeepBlue = Solid("#1F3A7A");
        private static readonly Pen Outline = CreateOutline();

        protected override void OnRender(DrawingContext dc)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0)
                return;

            var left = (ActualWidth - size) / 2;
            var top = (ActualHeight - size) / 2;
            var center = new Point(left + size / 2, top + size / 2);

            dc.PushClip(new EllipseGeometry(center, size / 2, size / 2));
            switch (Code)
            {
                case "uk":
                    Stripes(dc, left, top, size, Blue, Yellow);
                    break;
                case "cs":
                    Stripes(dc, left, top, size, White, Red);
                    dc.DrawGeometry(NavyBlue, null, Triangle(left, top, size));
                    break;
                case "ru":
                    Stripes(dc, left, top, size, White, RoyalBlue, Red);
                    break;
                case "en":
                    DrawUnionFlag(dc, left, top, size);
                    break;
            }
            dc.Pop();

            dc.DrawEllipse(null, Outline, center, size / 2 - 0.25, size / 2 - 0.25);
        }

        private static void Stripes(DrawingContext dc, double left, double top, double size, params Brush[] colors)
        {
            var stripe = size / colors.Length;
            for (var i = 0; i < colors.Length; i++)
                dc.DrawRectangle(colors[i], null, new Rect(left, top + i * stripe, size, stripe + 0.5));
        }

        private static Geometry Triangle(double left, double top, double size)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(left, top), true, true);
                context.LineTo(new Point(left + size * 0.54, top + size / 2), false, false);
                context.LineTo(new Point(left, top + size), false, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static void DrawUnionFlag(DrawingContext dc, double left, double top, double size)
        {
            dc.DrawRectangle(DeepBlue, null, new Rect(left, top, size, size));

            var unit = size / 24;
            var topLeft = new Point(left, top);
            var topRight = new Point(left + size, top);
            var bottomLeft = new Point(left, top + size);
            var bottomRight = new Point(left + size, top + size);
            var middle = new Point(left + size / 2, top + size / 2);

            var whiteDiagonal = new Pen(White, 4 * unit);
            var redDiagonal = new Pen(Red, 1.6 * unit);
            foreach (var pen in new[] { whiteDiagonal, redDiagonal })
            {
                dc.DrawLine(pen, topLeft, bottomRight);
                dc.DrawLine(pen, topRight, bottomLeft);
            }

            var whiteCross = new Pen(White, 7 * unit);
            var redCross = new Pen(Red, 4 * unit);
            foreach (var pen in new[] { whiteCross, redCross })
            {
                dc.DrawLine(pen, new Point(middle.X, top), new Point(middle.X, top + size));
                dc.DrawLine(pen, new Point(left, middle.Y), new Point(left + size, middle.Y));
            }
        }

        private static Brush Solid(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private static Pen CreateOutline()
        {
            var pen = new Pen(Solid("#4D808080"), 0.5);
            pen.Freeze();
            return pen;
        }
    }
}

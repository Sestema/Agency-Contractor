using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Controls
{
    /// <summary>One coloured part of a ring or a stacked bar.</summary>
    public sealed class ChartSegment
    {
        public string Label { get; init; } = string.Empty;
        public double Value { get; init; }
        public Brush Brush { get; init; } = Brushes.Gray;
        public string ValueText { get; init; } = string.Empty;
    }

    /// <summary>One month column: the main value (to pay) with a smaller value stacked on top (expenses).</summary>
    public sealed class ChartMonthBar
    {
        public string Label { get; init; } = string.Empty;
        public double Primary { get; init; }
        public double Secondary { get; init; }
    }

    /// <summary>Stacked month columns that stretch to the space they get; labels under the columns.</summary>
    public sealed class MonthBarsChart : FrameworkElement
    {
        public static readonly DependencyProperty BarsProperty = DependencyProperty.Register(nameof(Bars), typeof(IReadOnlyList<ChartMonthBar>), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PrimaryBrushProperty = DependencyProperty.Register(nameof(PrimaryBrush), typeof(Brush), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SecondaryBrushProperty = DependencyProperty.Register(nameof(SecondaryBrush), typeof(Brush), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty OutlineModeProperty = DependencyProperty.Register(nameof(OutlineMode), typeof(bool), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(MonthBarsChart),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Thin outlined columns, only the latest month filled, with a dashed 3-month trend line.</summary>
        public bool OutlineMode { get => (bool)GetValue(OutlineModeProperty); set => SetValue(OutlineModeProperty, value); }
        public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

        public IReadOnlyList<ChartMonthBar>? Bars { get => (IReadOnlyList<ChartMonthBar>?)GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
        public Brush PrimaryBrush { get => (Brush)GetValue(PrimaryBrushProperty); set => SetValue(PrimaryBrushProperty, value); }
        public Brush SecondaryBrush { get => (Brush)GetValue(SecondaryBrushProperty); set => SetValue(SecondaryBrushProperty, value); }
        public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            var bars = Bars;
            if (bars == null || bars.Count == 0 || ActualWidth <= 0 || ActualHeight <= 20)
                return;

            const double labelHeight = 16;
            var chartHeight = ActualHeight - labelHeight;
            var max = bars.Max(bar => Math.Max(0, bar.Primary) + Math.Max(0, bar.Secondary));
            if (max <= 0)
                max = 1;

            if (OutlineMode)
            {
                RenderOutline(dc, bars, chartHeight);
                return;
            }

            var slot = ActualWidth / bars.Count;
            var barWidth = Math.Max(4, Math.Min(slot * 0.62, 46));
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var fontSize = Math.Max(9, Math.Min(11, slot / 3.2));

            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var x = i * slot + (slot - barWidth) / 2;
                var primaryHeight = Math.Max(0, bar.Primary) / max * chartHeight;
                var secondaryHeight = Math.Max(0, bar.Secondary) / max * chartHeight;
                var top = chartHeight - primaryHeight;
                if (primaryHeight > 0)
                    dc.DrawRoundedRectangle(PrimaryBrush, null, new Rect(x, top, barWidth, primaryHeight), 2, 2);
                if (secondaryHeight > 0)
                    dc.DrawRoundedRectangle(SecondaryBrush, null, new Rect(x, top - secondaryHeight - 1, barWidth, secondaryHeight), 2, 2);

                if (slot >= 18)
                {
                    var text = new FormattedText(bar.Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize, LabelBrush, dpi);
                    dc.DrawText(text, new Point(i * slot + (slot - text.Width) / 2, chartHeight + 2));
                }
            }
        }

        private void RenderOutline(DrawingContext dc, IReadOnlyList<ChartMonthBar> bars, double chartHeight)
        {
            var max = bars.Max(bar => Math.Max(0, bar.Primary));
            if (max <= 0)
                max = 1;

            var slot = ActualWidth / bars.Count;
            var barWidth = Math.Max(4, Math.Min(slot * 0.55, 34));
            var top = 4.0;
            var usable = chartHeight - top;
            var outline = new Pen(PrimaryBrush, 1.2);
            outline.Freeze();
            var baseLine = new Pen(GridBrush, 1);
            baseLine.Freeze();
            dc.DrawLine(baseLine, new Point(0, chartHeight + 0.5), new Point(ActualWidth, chartHeight + 0.5));

            // Latest month that has money is the "current" one and gets filled.
            var current = -1;
            for (var i = bars.Count - 1; i >= 0; i--)
            {
                if (bars[i].Primary > 0) { current = i; break; }
            }

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var fontSize = Math.Max(9, Math.Min(11, slot / 3.2));
            var tops = new double[bars.Count];
            for (var i = 0; i < bars.Count; i++)
            {
                var height = Math.Max(0, bars[i].Primary) / max * usable;
                var x = i * slot + (slot - barWidth) / 2;
                tops[i] = chartHeight - height;
                if (height > 0)
                {
                    var rect = new Rect(x + 0.6, chartHeight - height + 0.6, barWidth - 1.2, Math.Max(0, height - 0.6));
                    dc.DrawRoundedRectangle(i == current ? PrimaryBrush : null, outline, rect, 2, 2);
                }

                if (slot >= 18)
                {
                    var text = new FormattedText(bars[i].Label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, fontSize,
                        i == current ? PrimaryBrush : LabelBrush, dpi);
                    dc.DrawText(text, new Point(i * slot + (slot - text.Width) / 2, chartHeight + 2));
                }
            }

            // Dashed trend: 3-month moving average through the started months.
            var points = new List<Point>();
            for (var i = 0; i < bars.Count; i++)
            {
                if (bars[i].Primary <= 0)
                    continue;
                var window = Enumerable.Range(Math.Max(0, i - 2), i - Math.Max(0, i - 2) + 1).Where(k => bars[k].Primary > 0).Select(k => bars[k].Primary).ToList();
                var average = window.Average();
                points.Add(new Point(i * slot + slot / 2, chartHeight - average / max * usable));
            }

            if (points.Count >= 2)
            {
                var trend = new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
                trend.Freeze();
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(points[0], false, false);
                    context.PolyLineTo(points.Skip(1).ToList(), true, true);
                }
                geometry.Freeze();
                dc.DrawGeometry(null, trend, geometry);
            }
        }
    }

    /// <summary>A ring split into coloured parts, with the total in the middle; size follows the space it gets.</summary>
    public sealed class SegmentRing : FrameworkElement
    {
        public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IReadOnlyList<ChartSegment>), typeof(SegmentRing),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty CenterTextProperty = DependencyProperty.Register(nameof(CenterText), typeof(string), typeof(SegmentRing),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(nameof(TextBrush), typeof(Brush), typeof(SegmentRing),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(SegmentRing),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<ChartSegment>? Segments { get => (IReadOnlyList<ChartSegment>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
        public string CenterText { get => (string)GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }
        public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            if (size < 24)
                return;

            var thickness = Math.Max(6, size * 0.15);
            var radius = (size - thickness) / 2;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var segments = (Segments ?? Array.Empty<ChartSegment>()).Where(segment => segment.Value > 0).ToList();
            var total = segments.Sum(segment => segment.Value);

            if (total <= 0)
            {
                dc.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);
            }
            else
            {
                var start = 0.0;
                foreach (var segment in segments)
                {
                    var sweep = 360.0 * segment.Value / total;
                    DrawArc(dc, center, radius, thickness, segment.Brush, start, segments.Count == 1 ? 359.99 : Math.Max(0.5, sweep - 1.5));
                    start += sweep;
                }
            }

            if (!string.IsNullOrEmpty(CenterText))
            {
                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                var text = new FormattedText(CenterText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, Math.Max(10, size * 0.18), TextBrush, dpi);
                dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
            }
        }

        private static void DrawArc(DrawingContext dc, Point center, double radius, double thickness, Brush brush, double startDegrees, double sweepDegrees)
        {
            Point At(double degrees)
            {
                var radians = (degrees - 90) * Math.PI / 180;
                return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(At(startDegrees), false, false);
                context.ArcTo(At(startDegrees + sweepDegrees), new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness), geometry);
        }
    }

    /// <summary>A horizontal bar split into coloured parts by value.</summary>
    public sealed class SegmentBar : FrameworkElement
    {
        public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(nameof(Segments), typeof(IReadOnlyList<ChartSegment>), typeof(SegmentBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(SegmentBar),
            new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<ChartSegment>? Segments { get => (IReadOnlyList<ChartSegment>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            var radius = Math.Min(4, ActualHeight / 2);
            dc.PushClip(new RectangleGeometry(bounds, radius, radius));
            dc.DrawRectangle(TrackBrush, null, bounds);

            var segments = (Segments ?? Array.Empty<ChartSegment>()).Where(segment => segment.Value > 0).ToList();
            var total = segments.Sum(segment => segment.Value);
            var x = 0.0;
            foreach (var segment in segments)
            {
                var width = total <= 0 ? 0 : ActualWidth * segment.Value / total;
                dc.DrawRectangle(segment.Brush, null, new Rect(x, 0, width + 0.5, ActualHeight));
                x += width;
            }

            dc.Pop();
        }
    }
}

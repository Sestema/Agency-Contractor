using System;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Controls
{
    /// <summary>Static progress ring for a completed/total value; redrawn only when the value changes.</summary>
    public class DocumentProgressRing : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(DocumentProgressRing),
                new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(DocumentProgressRing),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProgressBrushProperty =
            DependencyProperty.Register(nameof(ProgressBrush), typeof(Brush), typeof(DocumentProgressRing),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty =
            DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(DocumentProgressRing),
                new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Brush? TrackBrush
        {
            get => (Brush?)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush? ProgressBrush
        {
            get => (Brush?)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public double RingThickness
        {
            get => (double)GetValue(RingThicknessProperty);
            set => SetValue(RingThicknessProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            var radius = (size - RingThickness) / 2;
            if (radius <= 0)
                return;

            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, new Pen(TrackBrush, RingThickness), center, radius, radius);

            var value = Math.Clamp(Value, 0, 1);
            if (value <= 0 || ProgressBrush == null)
                return;

            var pen = new Pen(ProgressBrush, RingThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (value >= 1)
            {
                dc.DrawEllipse(null, pen, center, radius, radius);
                return;
            }

            var start = new Point(center.X, center.Y - radius);
            var angle = value * 2 * Math.PI;
            var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

            var arc = new StreamGeometry();
            using (var context = arc.Open())
            {
                context.BeginFigure(start, false, false);
                context.ArcTo(end, new Size(radius, radius), 0, value > 0.5, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, pen, arc);
        }
    }
}

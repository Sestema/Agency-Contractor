using System;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Controls
{
    /// <summary>Static ring split into "added" and "archived" parts; redrawn only when values, brushes or size change.</summary>
    public class MovementRing : FrameworkElement
    {
        private const double GapDegrees = 4;

        public static readonly DependencyProperty AddedProperty =
            DependencyProperty.Register(nameof(Added), typeof(int), typeof(MovementRing),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ArchivedProperty =
            DependencyProperty.Register(nameof(Archived), typeof(int), typeof(MovementRing),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AddedBrushProperty =
            DependencyProperty.Register(nameof(AddedBrush), typeof(Brush), typeof(MovementRing),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ArchivedBrushProperty =
            DependencyProperty.Register(nameof(ArchivedBrush), typeof(Brush), typeof(MovementRing),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty =
            DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(MovementRing),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RingThicknessProperty =
            DependencyProperty.Register(nameof(RingThickness), typeof(double), typeof(MovementRing),
                new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public int Added { get => (int)GetValue(AddedProperty); set => SetValue(AddedProperty, value); }
        public int Archived { get => (int)GetValue(ArchivedProperty); set => SetValue(ArchivedProperty, value); }
        public Brush? AddedBrush { get => (Brush?)GetValue(AddedBrushProperty); set => SetValue(AddedBrushProperty, value); }
        public Brush? ArchivedBrush { get => (Brush?)GetValue(ArchivedBrushProperty); set => SetValue(ArchivedBrushProperty, value); }
        public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
        public double RingThickness { get => (double)GetValue(RingThicknessProperty); set => SetValue(RingThicknessProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            var size = Math.Min(ActualWidth, ActualHeight);
            var thickness = Math.Min(RingThickness, size / 4);
            if (size <= 0 || thickness <= 0)
                return;

            var center = new Point(ActualWidth / 2, ActualHeight / 2);
            var radius = (size - thickness) / 2;
            var added = Math.Max(0, Added);
            var archived = Math.Max(0, Archived);
            var total = added + archived;

            if (total == 0)
            {
                dc.DrawEllipse(null, new Pen(TrackBrush, thickness), center, radius, radius);
                return;
            }

            if (added == 0 || archived == 0)
            {
                dc.DrawEllipse(null, new Pen(added > 0 ? AddedBrush : ArchivedBrush, thickness), center, radius, radius);
                return;
            }

            var addedSweep = 360.0 * added / total;
            DrawArc(dc, center, radius, thickness, AddedBrush, GapDegrees / 2, addedSweep - GapDegrees);
            DrawArc(dc, center, radius, thickness, ArchivedBrush, addedSweep + GapDegrees / 2, 360 - addedSweep - GapDegrees);
        }

        private static void DrawArc(DrawingContext dc, Point center, double radius, double thickness, Brush? brush, double startDegrees, double sweepDegrees)
        {
            if (brush == null || sweepDegrees <= 0)
                return;

            var start = PointOn(center, radius, startDegrees);
            var end = PointOn(center, radius, startDegrees + sweepDegrees);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }, geometry);
        }

        /// <summary>0 degrees is the top of the ring, going clockwise.</summary>
        private static Point PointOn(Point center, double radius, double degrees)
        {
            var radians = (degrees - 90) * Math.PI / 180;
            return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }
    }
}

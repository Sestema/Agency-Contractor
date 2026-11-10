using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Win11DesktopApp.Controls
{
    /// <summary>Static monthly bars; redrawn only when the values, brushes or size change.</summary>
    public class SalaryMiniBars : FrameworkElement
    {
        private const double Gap = 5;
        private const double MaxBarWidth = 72;
        private const double CornerRadius = 4;
        private const double DimOpacity = 0.3;

        public static readonly DependencyProperty ValuesProperty =
            DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<double>), typeof(SalaryMiniBars),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BarBrushProperty =
            DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(SalaryMiniBars),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<double>? Values
        {
            get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush? BarBrush
        {
            get => (Brush?)GetValue(BarBrushProperty);
            set => SetValue(BarBrushProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var values = Values;
            if (values == null || values.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
                return;

            var max = values.Max();
            if (max <= 0)
                return;

            var count = values.Count;
            var barWidth = Math.Min((ActualWidth - Gap * (count - 1)) / count, MaxBarWidth);
            if (barWidth <= 0)
                return;

            var x = (ActualWidth - (barWidth * count + Gap * (count - 1))) / 2;
            var highlighted = false;
            var radius = Math.Min(CornerRadius, barWidth / 2);

            // The rounded bottoms extend past the clip, so bars stand flat on the card edge.
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            foreach (var value in values)
            {
                if (value > 0)
                {
                    var height = Math.Max(2, value / max * ActualHeight);
                    var isBest = !highlighted && value >= max;
                    highlighted |= isBest;
                    dc.PushOpacity(isBest ? 1 : DimOpacity);
                    dc.DrawRoundedRectangle(
                        BarBrush,
                        null,
                        new Rect(x, ActualHeight - height, barWidth, height + radius),
                        radius,
                        radius);
                    dc.Pop();
                }

                x += barWidth + Gap;
            }

            dc.Pop();
        }
    }
}

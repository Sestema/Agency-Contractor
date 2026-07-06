using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Applies bound width changes with a short ease-out animation for large jumps
    /// (tile size step x1-x6 changes) and instantly for small deltas (window resizing),
    /// so resize dragging never lags behind the mouse.
    /// </summary>
    public static class TileWidthAnimator
    {
        private const double AnimateThreshold = 40.0;
        private static readonly Duration AnimationDuration = new Duration(TimeSpan.FromMilliseconds(180));

        public static readonly DependencyProperty TargetWidthProperty =
            DependencyProperty.RegisterAttached(
                "TargetWidth",
                typeof(double),
                typeof(TileWidthAnimator),
                new PropertyMetadata(double.NaN, OnTargetWidthChanged));

        public static double GetTargetWidth(DependencyObject obj) => (double)obj.GetValue(TargetWidthProperty);
        public static void SetTargetWidth(DependencyObject obj, double value) => obj.SetValue(TargetWidthProperty, value);

        private static void OnTargetWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            var newWidth = (double)e.NewValue;
            if (double.IsNaN(newWidth) || newWidth <= 0)
                return;

            var currentWidth = double.IsNaN(element.Width) ? element.ActualWidth : element.Width;

            if (currentWidth <= 0 || double.IsNaN(currentWidth)
                || Math.Abs(newWidth - currentWidth) < AnimateThreshold)
            {
                element.BeginAnimation(FrameworkElement.WidthProperty, null);
                element.Width = newWidth;
                return;
            }

            var animation = new DoubleAnimation
            {
                From = currentWidth,
                To = newWidth,
                Duration = AnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                element.BeginAnimation(FrameworkElement.WidthProperty, null);
                element.Width = newWidth;
            };
            element.Width = newWidth;
            element.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;

namespace Win11DesktopApp.Controls
{
    /// <summary>Single row whose first and last children touch the edges, with equal gaps between all children.</summary>
    public class SpaceBetweenPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double width = 0;
            double height = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double used = 0;
            foreach (UIElement child in InternalChildren)
                used += child.DesiredSize.Width;

            var gaps = InternalChildren.Count - 1;
            var gap = gaps > 0 ? Math.Max(0, (finalSize.Width - used) / gaps) : 0;

            double x = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(x, 0, child.DesiredSize.Width, finalSize.Height));
                x += child.DesiredSize.Width + gap;
            }

            return finalSize;
        }
    }
}

using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Win11DesktopApp.Views
{
    /// <summary>A still picture of the dragged card that follows the mouse. Drawn only when it moves.</summary>
    internal sealed class DragGhostAdorner : Adorner
    {
        private readonly ImageSource _snapshot;
        private readonly Size _size;
        private readonly Point _grabOffset;
        private Point _position;

        public DragGhostAdorner(UIElement adornedElement, FrameworkElement card, Point grabOffset)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            _size = new Size(card.ActualWidth, card.ActualHeight);
            _grabOffset = grabOffset;

            var dpi = VisualTreeHelper.GetDpi(card);
            var bitmap = new RenderTargetBitmap(
                (int)System.Math.Ceiling(_size.Width * dpi.DpiScaleX),
                (int)System.Math.Ceiling(_size.Height * dpi.DpiScaleY),
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);
            bitmap.Render(card);
            bitmap.Freeze();
            _snapshot = bitmap;
        }

        public void MoveTo(Point position)
        {
            _position = position;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushOpacity(0.85);
            drawingContext.DrawImage(_snapshot, new Rect(
                new Point(_position.X - _grabOffset.X, _position.Y - _grabOffset.Y), _size));
            drawingContext.Pop();
        }
    }

    /// <summary>An accent line above or below a card that shows where the dragged template will land.</summary>
    internal sealed class InsertionLineAdorner : Adorner
    {
        private readonly Brush _brush;
        private bool _below;

        public InsertionLineAdorner(UIElement adornedElement, Brush brush)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            _brush = brush;
        }

        public bool Below
        {
            get => _below;
            set
            {
                if (_below == value)
                    return;
                _below = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var size = AdornedElement.RenderSize;
            // The cards are spaced by their margins; drawing in that gap keeps the line between rows.
            var y = _below ? size.Height + 6 : -6;
            var pen = new Pen(_brush, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            drawingContext.DrawLine(pen, new Point(8, y), new Point(size.Width - 8, y));
            drawingContext.DrawEllipse(_brush, null, new Point(8, y), 5, 5);
        }
    }
}

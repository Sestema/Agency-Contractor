using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Win11DesktopApp.Views
{
    public partial class DocumentPreviewPanel : UserControl
    {
        private Point _panStart;
        private TranslateTransform? _panTranslate;

        public DocumentPreviewPanel()
        {
            InitializeComponent();
        }

        private static TransformGroup EnsureMutableTransform(Image image)
        {
            var tg = image.RenderTransform as TransformGroup;
            if (tg != null && tg.IsFrozen)
            {
                tg = tg.Clone();
                image.RenderTransform = tg;
            }
            return tg!;
        }

        private void DocImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not Image image) return;
            var tg = EnsureMutableTransform(image);
            if (tg == null || tg.Children.Count < 2) return;

            var scale = (ScaleTransform)tg.Children[0];
            var translate = (TranslateTransform)tg.Children[1];

            double zoom = e.Delta > 0 ? 1.15 : 1 / 1.15;
            double newScaleX = scale.ScaleX * zoom;
            double newScaleY = scale.ScaleY * zoom;

            newScaleX = Math.Clamp(newScaleX, 0.5, 10.0);
            newScaleY = Math.Clamp(newScaleY, 0.5, 10.0);

            var mousePos = e.GetPosition(image);

            double offsetX = mousePos.X * (1 - zoom);
            double offsetY = mousePos.Y * (1 - zoom);

            scale.ScaleX = newScaleX;
            scale.ScaleY = newScaleY;

            translate.X = translate.X * zoom + offsetX;
            translate.Y = translate.Y * zoom + offsetY;

            e.Handled = true;
        }

        private void DocImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image image) return;
            var tg = EnsureMutableTransform(image);
            if (tg == null || tg.Children.Count < 2) return;

            _panTranslate = (TranslateTransform)tg.Children[1];
            _panStart = e.GetPosition(image.Parent as FrameworkElement);
            _panStart = new Point(
                _panStart.X - _panTranslate.X,
                _panStart.Y - _panTranslate.Y);

            image.CaptureMouse();
            image.Cursor = Cursors.Hand;
            e.Handled = true;
        }

        private void DocImage_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Image image) return;
            image.ReleaseMouseCapture();
            image.Cursor = Cursors.Arrow;
            _panTranslate = null;
            e.Handled = true;
        }

        private void DocImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (_panTranslate == null) return;
            if (sender is not Image image) return;
            if (e.RightButton != MouseButtonState.Pressed) return;

            var pos = e.GetPosition(image.Parent as FrameworkElement);
            _panTranslate.X = pos.X - _panStart.X;
            _panTranslate.Y = pos.Y - _panStart.Y;
            e.Handled = true;
        }

        private void DocImage_ResetZoom(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is not Image image) return;
            var tg = EnsureMutableTransform(image);
            if (tg == null || tg.Children.Count < 2) return;

            var scale = (ScaleTransform)tg.Children[0];
            var translate = (TranslateTransform)tg.Children[1];

            scale.ScaleX = 1;
            scale.ScaleY = 1;
            translate.X = 0;
            translate.Y = 0;
            e.Handled = true;
        }
    }
}

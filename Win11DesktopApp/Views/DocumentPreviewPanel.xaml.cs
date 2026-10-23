using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Win11DesktopApp.Views
{
    public partial class DocumentPreviewPanel : UserControl
    {
        private Point _panStart;
        private double _panStartX;
        private double _panStartY;
        private bool _isPanning;
        private double _zoom = 1.0;

        public DocumentPreviewPanel()
        {
            InitializeComponent();
        }

        private void DocImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.18 : 1.0 / 1.18;
            var pivot = e.GetPosition(DocImage);
            ApplyZoom(_zoom * factor, pivot);
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
            => ApplyZoom(_zoom * 1.25, new Point(DocImage.ActualWidth / 2, DocImage.ActualHeight / 2));

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
            => ApplyZoom(_zoom / 1.25, new Point(DocImage.ActualWidth / 2, DocImage.ActualHeight / 2));

        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

        private void ResetZoom()
        {
            _zoom = 1.0;
            ZoomScale.ScaleX = 1.0;
            ZoomScale.ScaleY = 1.0;
            ZoomTranslate.X = 0;
            ZoomTranslate.Y = 0;
            ZoomResetBtn.Content = "100%";
            StageGrid.Cursor = Cursors.Arrow;
        }

        private void ApplyZoom(double targetZoom, Point pivot)
        {
            double next = Math.Clamp(targetZoom, 1.0, 5.0);
            if (Math.Abs(next - 1.0) < 0.03)
            {
                ResetZoom();
                return;
            }

            double ratio = next / _zoom;
            _zoom = next;
            ZoomScale.ScaleX = _zoom;
            ZoomScale.ScaleY = _zoom;
            ZoomTranslate.X = ZoomTranslate.X * ratio + pivot.X * (1 - ratio);
            ZoomTranslate.Y = ZoomTranslate.Y * ratio + pivot.Y * (1 - ratio);
            ClampPan();
            ZoomResetBtn.Content = $"{Math.Round(_zoom * 100)}%";
            StageGrid.Cursor = Cursors.SizeAll;
        }

        private void ClampPan()
        {
            double w = DocImage.ActualWidth > 0 ? DocImage.ActualWidth : StageGrid.ActualWidth;
            double h = DocImage.ActualHeight > 0 ? DocImage.ActualHeight : StageGrid.ActualHeight;
            if (w <= 0 || h <= 0)
                return;

            double minX = w * (1.0 - _zoom);
            double minY = h * (1.0 - _zoom);
            ZoomTranslate.X = Math.Clamp(ZoomTranslate.X, minX, 0);
            ZoomTranslate.Y = Math.Clamp(ZoomTranslate.Y, minY, 0);
        }

        private void Stage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src && InsideButton(src))
                return;

            if (e.ClickCount == 2)
            {
                ResetZoom();
                e.Handled = true;
                return;
            }

            if (_zoom <= 1.01)
                return;

            _isPanning = true;
            _panStart = e.GetPosition(StageGrid);
            _panStartX = ZoomTranslate.X;
            _panStartY = ZoomTranslate.Y;
            StageGrid.CaptureMouse();
            e.Handled = true;
        }

        private void Stage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning)
                return;

            var pos = e.GetPosition(StageGrid);
            ZoomTranslate.X = _panStartX + (pos.X - _panStart.X);
            ZoomTranslate.Y = _panStartY + (pos.Y - _panStart.Y);
            ClampPan();
            e.Handled = true;
        }

        private void Stage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isPanning)
                return;

            _isPanning = false;
            StageGrid.ReleaseMouseCapture();
            e.Handled = true;
        }

        private static bool InsideButton(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase)
                    return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }

            return false;
        }
    }
}

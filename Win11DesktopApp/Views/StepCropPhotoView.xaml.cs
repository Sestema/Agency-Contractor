using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public class CropTargetIconConverter : IValueConverter
    {
        private static readonly Geometry Photo = Geometry.Parse(
            "M7 3L8.2 4.6H11.8L13 3H15.5C16.6 3 17.5 3.9 17.5 5V15C17.5 16.1 16.6 17 15.5 17H4.5C3.4 17 2.5 16.1 2.5 15V5C2.5 3.9 3.4 3 4.5 3H7ZM10 6.8A3.2 3.2 0 1 0 10 13.2A3.2 3.2 0 1 0 10 6.8Z");
        private static readonly Geometry IdCard = Geometry.Parse(
            "M2 4.5C2 3.67 2.67 3 3.5 3H16.5C17.33 3 18 3.67 18 4.5V15.5C18 16.33 17.33 17 16.5 17H3.5C2.67 17 2 16.33 2 15.5V4.5ZM3.7 5V15H16.3V5H3.7ZM6.8 6.8A1.6 1.6 0 1 1 6.8 10A1.6 1.6 0 1 1 6.8 6.8ZM4.7 12.8C5 11.7 5.8 11.1 6.8 11.1C7.8 11.1 8.6 11.7 8.9 12.8H4.7ZM10.4 7.3H15V8.5H10.4V7.3ZM10.4 10H15V11.2H10.4V10ZM10.4 12.6H13.5V13.8H10.4V12.6Z");
        private static readonly Geometry Visa = Geometry.Parse(
            "M10 2A8 8 0 1 0 10 18A8 8 0 1 0 10 2ZM3.9 9.3H6.8C6.9 7.3 7.4 5.5 8.2 4.2A6.3 6.3 0 0 0 3.9 9.3ZM6.8 10.7H3.9A6.3 6.3 0 0 0 8.2 15.8C7.4 14.5 6.9 12.7 6.8 10.7ZM8.3 9.3H11.7C11.6 7.5 10.9 5.8 10 4.5C9.1 5.8 8.4 7.5 8.3 9.3ZM11.7 10.7H8.3C8.4 12.5 9.1 14.2 10 15.5C10.9 14.2 11.6 12.5 11.7 10.7ZM13.2 9.3H16.1A6.3 6.3 0 0 0 11.8 4.2C12.6 5.5 13.1 7.3 13.2 9.3ZM11.8 15.8A6.3 6.3 0 0 0 16.1 10.7H13.2C13.1 12.7 12.6 14.5 11.8 15.8Z");
        private static readonly Geometry Insurance = Geometry.Parse(
            "M10 1.8L16.5 4.4V9.4C16.5 13.7 13.7 17.3 10 18.5C6.3 17.3 3.5 13.7 3.5 9.4V4.4L10 1.8ZM9.2 6.2V8.9H6.5V10.5H9.2V13.2H10.8V10.5H13.5V8.9H10.8V6.2H9.2Z");
        private static readonly Geometry Permit = Geometry.Parse(
            "M7 2.5C7 1.67 7.67 1 8.5 1H11.5C12.33 1 13 1.67 13 2.5V4H16.5C17.33 4 18 4.67 18 5.5V16.5C18 17.33 17.33 18 16.5 18H3.5C2.67 18 2 17.33 2 16.5V5.5C2 4.67 2.67 4 3.5 4H7V2.5ZM8.5 2.5V4H11.5V2.5H8.5ZM3.7 8.5V16.3H16.3V8.5H12V10.2H8V8.5H3.7Z");
        private static readonly Geometry Passport = Geometry.Parse(
            "M4 2.5C4 1.67 4.67 1 5.5 1H14.5C15.33 1 16 1.67 16 2.5V17.5C16 18.33 15.33 19 14.5 19H5.5C4.67 19 4 18.33 4 17.5V2.5ZM6 3V17H14V3H6ZM10 5.4A2.2 2.2 0 1 1 10 9.8A2.2 2.2 0 1 1 10 5.4ZM7.3 12.6C7.7 11.4 8.75 10.7 10 10.7C11.25 10.7 12.3 11.4 12.7 12.6H7.3ZM7.2 14.4H12.8V15.5H7.2V14.4Z");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var label = value?.ToString() ?? string.Empty;
            if (Is(label, "CropPhoto")) return Photo;
            if (Is(label, "CropIdCard1") || Is(label, "CropIdCard2") || Is(label, "CropVisaPage2")) return IdCard;
            if (Is(label, "CropVisa")) return Visa;
            if (Is(label, "CropInsurance")) return Insurance;
            if (Is(label, "CropPermit")) return Permit;
            return Passport;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static bool Is(string label, string resourceKey)
        {
            var text = Application.Current?.TryFindResource(resourceKey) as string;
            return !string.IsNullOrEmpty(text)
                && string.Equals(label, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class CropAspectEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double current)
                return false;
            if (!double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
                return false;
            return Math.Abs(current - expected) < 0.02;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true
                && double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var expected))
                return expected;
            return Binding.DoNothing;
        }
    }

    public partial class StepCropPhotoView : UserControl
    {
        private Point _startPoint;
        private Point _panStart;
        private double _panStartX;
        private double _panStartY;
        private Point _moveRectStart;
        private double _moveRectStartLeft;
        private double _moveRectStartTop;
        private bool _isDragging;
        private bool _isMovingRect;
        private bool _isPanning;
        private double _zoom = 1.0;

        public StepCropPhotoView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            CropCanvas.MouseLeftButtonDown += OnMouseDown;
            CropCanvas.MouseMove += OnMouseMove;
            CropCanvas.MouseLeftButtonUp += OnMouseUp;
            CropCanvas.PreviewMouseWheel += OnCanvasMouseWheel;
        }

        private bool IsPhotoMode => DataContext is AddEmployeeWizardViewModel vm && vm.IsCropPhotoMode;

        private void OnCropSidePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer viewer || viewer.ScrollableHeight <= 0)
                return;

            viewer.ScrollToVerticalOffset(viewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is AddEmployeeWizardViewModel oldVm)
                oldVm.CropSourceChanged -= OnCropSourceChanged;

            if (e.NewValue is AddEmployeeWizardViewModel newVm)
                newVm.CropSourceChanged += OnCropSourceChanged;

            UpdateCanvasCursor();
        }

        private void OnCropSourceChanged()
        {
            ClearSelection();
            ResetZoom();
            UpdateCanvasCursor();
        }

        private void ClearSelection()
        {
            CropRect.Width = 0;
            CropRect.Height = 0;
            Canvas.SetLeft(CropRect, 0);
            Canvas.SetTop(CropRect, 0);
        }

        private void UpdateCanvasCursor()
        {
            if (IsPhotoMode)
                CropCanvas.Cursor = Cursors.Cross;
            else
                CropCanvas.Cursor = _zoom > 1.01 ? Cursors.SizeAll : Cursors.Arrow;
        }

        private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pivot = e.GetPosition(CropCanvas);
            var factor = e.Delta > 0 ? 1.18 : 1.0 / 1.18;
            ApplyZoom(_zoom * factor, pivot);
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
            => ApplyZoom(_zoom * 1.25, new Point(CropCanvas.Width / 2, CropCanvas.Height / 2));

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
            => ApplyZoom(_zoom / 1.25, new Point(CropCanvas.Width / 2, CropCanvas.Height / 2));

        private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

        private void ResetZoom()
        {
            _zoom = 1.0;
            ZoomScale.ScaleX = 1.0;
            ZoomScale.ScaleY = 1.0;
            ZoomTranslate.X = 0;
            ZoomTranslate.Y = 0;
            ZoomResetBtn.Content = "100%";
            UpdateCanvasCursor();
        }

        private void ApplyZoom(double targetZoom, Point pivot)
        {
            var next = Math.Clamp(targetZoom, 1.0, 4.0);
            if (Math.Abs(next - 1.0) < 0.03)
            {
                ResetZoom();
                if (CropRect.Width > 0 && CropRect.Height > 0)
                    UpdateScaledCropRect(Canvas.GetLeft(CropRect), Canvas.GetTop(CropRect), CropRect.Width, CropRect.Height);
                return;
            }

            var worldX = (pivot.X - ZoomTranslate.X) / _zoom;
            var worldY = (pivot.Y - ZoomTranslate.Y) / _zoom;

            _zoom = next;
            ZoomScale.ScaleX = _zoom;
            ZoomScale.ScaleY = _zoom;
            ZoomTranslate.X = pivot.X - worldX * _zoom;
            ZoomTranslate.Y = pivot.Y - worldY * _zoom;
            ClampPan();
            ZoomResetBtn.Content = $"{Math.Round(_zoom * 100)}%";
            UpdateCanvasCursor();

            if (CropRect.Width > 0 && CropRect.Height > 0)
                UpdateScaledCropRect(Canvas.GetLeft(CropRect), Canvas.GetTop(CropRect), CropRect.Width, CropRect.Height);
        }

        private void ClampPan()
        {
            var minX = CropCanvas.Width * (1.0 - _zoom);
            var minY = CropCanvas.Height * (1.0 - _zoom);
            ZoomTranslate.X = Math.Clamp(ZoomTranslate.X, minX, 0);
            ZoomTranslate.Y = Math.Clamp(ZoomTranslate.Y, minY, 0);
        }

        private bool IsInsideCropRect(Point pt)
        {
            if (CropRect.Width <= 4 || CropRect.Height <= 4)
                return false;

            var left = Canvas.GetLeft(CropRect);
            var top = Canvas.GetTop(CropRect);
            if (double.IsNaN(left) || double.IsNaN(top))
                return false;

            return pt.X >= left && pt.X <= left + CropRect.Width
                && pt.Y >= top && pt.Y <= top + CropRect.Height;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsPhotoMode)
            {
                if (_zoom <= 1.01)
                    return;
                _panStart = e.GetPosition(CropCanvas);
                _panStartX = ZoomTranslate.X;
                _panStartY = ZoomTranslate.Y;
                _isPanning = true;
                CropCanvas.CaptureMouse();
                return;
            }

            var clickPos = e.GetPosition(CropCanvas);
            if (IsInsideCropRect(clickPos))
            {
                _moveRectStart = clickPos;
                _moveRectStartLeft = Canvas.GetLeft(CropRect);
                _moveRectStartTop = Canvas.GetTop(CropRect);
                _isMovingRect = true;
                CropCanvas.Cursor = Cursors.SizeAll;
                CropCanvas.CaptureMouse();
                return;
            }

            _startPoint = clickPos;
            _isDragging = true;
            Canvas.SetLeft(CropRect, _startPoint.X);
            Canvas.SetTop(CropRect, _startPoint.Y);
            CropRect.Width = 0;
            CropRect.Height = 0;
            CropCanvas.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                var current = e.GetPosition(CropCanvas);
                ZoomTranslate.X = _panStartX + (current.X - _panStart.X);
                ZoomTranslate.Y = _panStartY + (current.Y - _panStart.Y);
                ClampPan();
                return;
            }

            if (_isMovingRect && IsPhotoMode)
            {
                var current = e.GetPosition(CropCanvas);
                var w = CropRect.Width;
                var h = CropRect.Height;
                var nextLeft = Math.Clamp(_moveRectStartLeft + (current.X - _moveRectStart.X), 0, Math.Max(0, CropCanvas.Width - w));
                var nextTop = Math.Clamp(_moveRectStartTop + (current.Y - _moveRectStart.Y), 0, Math.Max(0, CropCanvas.Height - h));

                Canvas.SetLeft(CropRect, nextLeft);
                Canvas.SetTop(CropRect, nextTop);
                UpdateScaledCropRect(nextLeft, nextTop, w, h);
                return;
            }

            if (!_isDragging)
            {
                if (IsPhotoMode)
                {
                    var hover = e.GetPosition(CropCanvas);
                    CropCanvas.Cursor = IsInsideCropRect(hover) ? Cursors.SizeAll : Cursors.Cross;
                }
                return;
            }

            if (!IsPhotoMode) return;
            var pos = e.GetPosition(CropCanvas);

            var x = Math.Min(pos.X, _startPoint.X);
            var y = Math.Min(pos.Y, _startPoint.Y);
            var w2 = Math.Abs(pos.X - _startPoint.X);
            var h2 = Math.Abs(pos.Y - _startPoint.Y);

            if (DataContext is AddEmployeeWizardViewModel vm && vm.KeepAspectRatio && vm.CropAspectRatio > 0)
            {
                var ratio = vm.CropAspectRatio;
                var newH = w2 / ratio;
                if (newH > CropCanvas.Height) newH = CropCanvas.Height;
                h2 = newH;
                w2 = h2 * ratio;
            }

            w2 = Math.Max(0, Math.Min(w2, CropCanvas.Width - x));
            h2 = Math.Max(0, Math.Min(h2, CropCanvas.Height - y));

            Canvas.SetLeft(CropRect, x);
            Canvas.SetTop(CropRect, y);
            CropRect.Width = w2;
            CropRect.Height = h2;

            UpdateScaledCropRect(x, y, w2, h2);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _isMovingRect = false;
            _isPanning = false;
            CropCanvas.ReleaseMouseCapture();
            if (IsPhotoMode)
            {
                var pos = e.GetPosition(CropCanvas);
                CropCanvas.Cursor = IsInsideCropRect(pos) ? Cursors.SizeAll : Cursors.Cross;
            }
        }

        private void UpdateScaledCropRect(double canvasX, double canvasY, double canvasW, double canvasH)
        {
            if (DataContext is not AddEmployeeWizardViewModel vm) return;

            var worldX = (canvasX - ZoomTranslate.X) / _zoom;
            var worldY = (canvasY - ZoomTranslate.Y) / _zoom;
            var worldW = canvasW / _zoom;
            var worldH = canvasH / _zoom;

            var source = CropImage.Source as BitmapSource;
            if (source == null || source.PixelWidth == 0 || source.PixelHeight == 0)
            {
                vm.CropRect = new Int32Rect((int)worldX, (int)worldY, Math.Max(1, (int)worldW), Math.Max(1, (int)worldH));
                return;
            }

            int realW = source.PixelWidth;
            int realH = source.PixelHeight;
            double displayW = CropCanvas.Width;
            double displayH = CropCanvas.Height;

            double imageAspect = (double)realW / realH;
            double canvasAspect = displayW / displayH;

            double renderedW, renderedH, offsetX, offsetY;

            if (imageAspect > canvasAspect)
            {
                renderedW = displayW;
                renderedH = displayW / imageAspect;
                offsetX = 0;
                offsetY = (displayH - renderedH) / 2.0;
            }
            else
            {
                renderedH = displayH;
                renderedW = displayH * imageAspect;
                offsetX = (displayW - renderedW) / 2.0;
                offsetY = 0;
            }

            double relX = (worldX - offsetX) / renderedW;
            double relY = (worldY - offsetY) / renderedH;
            double relW = worldW / renderedW;
            double relH = worldH / renderedH;

            relX = Math.Clamp(relX, 0, 1);
            relY = Math.Clamp(relY, 0, 1);
            relW = Math.Clamp(relW, 0, 1 - relX);
            relH = Math.Clamp(relH, 0, 1 - relY);

            int pixelX = (int)(relX * realW);
            int pixelY = (int)(relY * realH);
            int pixelW = Math.Max(1, (int)(relW * realW));
            int pixelH = Math.Max(1, (int)(relH * realH));

            if (pixelX + pixelW > realW) pixelW = realW - pixelX;
            if (pixelY + pixelH > realH) pixelH = realH - pixelY;

            vm.CropRect = new Int32Rect(pixelX, pixelY, Math.Max(1, pixelW), Math.Max(1, pixelH));
        }
    }
}

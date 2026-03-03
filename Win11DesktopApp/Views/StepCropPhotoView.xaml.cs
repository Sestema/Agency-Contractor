using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class StepCropPhotoView : UserControl
    {
        private Point _startPoint;
        private bool _isDragging;

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
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is AddEmployeeWizardViewModel oldVm)
                oldVm.CropSourceChanged -= OnCropSourceChanged;

            if (e.NewValue is AddEmployeeWizardViewModel newVm)
                newVm.CropSourceChanged += OnCropSourceChanged;
        }

        private void OnCropSourceChanged()
        {
            CropRect.Width = 0;
            CropRect.Height = 0;
            Canvas.SetLeft(CropRect, 0);
            Canvas.SetTop(CropRect, 0);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(CropCanvas);
            _isDragging = true;
            Canvas.SetLeft(CropRect, _startPoint.X);
            Canvas.SetTop(CropRect, _startPoint.Y);
            CropRect.Width = 0;
            CropRect.Height = 0;
            CropCanvas.CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(CropCanvas);

            var x = Math.Min(pos.X, _startPoint.X);
            var y = Math.Min(pos.Y, _startPoint.Y);
            var w = Math.Abs(pos.X - _startPoint.X);
            var h = Math.Abs(pos.Y - _startPoint.Y);

            if (DataContext is AddEmployeeWizardViewModel vm && vm.KeepAspectRatio && vm.CropAspectRatio > 0)
            {
                var ratio = vm.CropAspectRatio;
                var newH = w / ratio;
                if (newH > CropCanvas.Height) newH = CropCanvas.Height;
                h = newH;
                w = h * ratio;
            }

            w = Math.Max(0, Math.Min(w, CropCanvas.Width - x));
            h = Math.Max(0, Math.Min(h, CropCanvas.Height - y));

            Canvas.SetLeft(CropRect, x);
            Canvas.SetTop(CropRect, y);
            CropRect.Width = w;
            CropRect.Height = h;

            UpdateScaledCropRect(x, y, w, h);
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            CropCanvas.ReleaseMouseCapture();
        }

        private void UpdateScaledCropRect(double canvasX, double canvasY, double canvasW, double canvasH)
        {
            if (!(DataContext is AddEmployeeWizardViewModel vm)) return;

            var source = CropImage.Source as BitmapSource;
            if (source == null || source.PixelWidth == 0 || source.PixelHeight == 0)
            {
                vm.CropRect = new Int32Rect((int)canvasX, (int)canvasY, Math.Max(1, (int)canvasW), Math.Max(1, (int)canvasH));
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

            double relX = (canvasX - offsetX) / renderedW;
            double relY = (canvasY - offsetY) / renderedH;
            double relW = canvasW / renderedW;
            double relH = canvasH / renderedH;

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

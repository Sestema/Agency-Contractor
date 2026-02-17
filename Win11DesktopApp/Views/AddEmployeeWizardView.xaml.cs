using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class AddEmployeeWizardView : UserControl
    {
        private Point _startPoint;
        private bool _isDragging;

        public AddEmployeeWizardView()
        {
            InitializeComponent();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            CropCanvas.MouseLeftButtonDown += OnMouseDown;
            CropCanvas.MouseMove += OnMouseMove;
            CropCanvas.MouseLeftButtonUp += OnMouseUp;
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
            Canvas.SetLeft(CropRect, x);
            Canvas.SetTop(CropRect, y);
            CropRect.Width = w;
            CropRect.Height = h;

            if (DataContext is AddEmployeeWizardViewModel vm)
            {
                vm.CropRect = new Int32Rect((int)x, (int)y, Math.Max(1, (int)w), Math.Max(1, (int)h));
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            CropCanvas.ReleaseMouseCapture();
        }
    }
}

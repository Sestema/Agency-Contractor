using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class PdfEditorView : UserControl
    {
        private TagEntry? _pendingTag;
        private bool _isPlacingMode;

        // Drag state
        private bool _isDragging;
        private PdfPlacementViewModel? _dragPlacement;
        private Border? _dragElement;
        private Point _dragStartMouse;
        private double _dragStartLeft;
        private double _dragStartTop;

        public PdfEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateCanvasSize();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is PdfEditorViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.RequestPlaceTagOnClick = null;
                oldVm.RequestRenderOverlays -= RenderTagOverlays;
            }

            if (e.NewValue is PdfEditorViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                vm.RequestPlaceTagOnClick = tag =>
                {
                    _pendingTag = tag;
                    _isPlacingMode = true;
                    PdfCanvas.Cursor = Cursors.Cross;
                };
                vm.RequestRenderOverlays += RenderTagOverlays;
                UpdateCanvasSize();
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PdfEditorViewModel.CurrentPageImage) ||
                e.PropertyName == nameof(PdfEditorViewModel.CurrentPagePlacements))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateCanvasSize();
                    RenderTagOverlays();
                });
            }
        }

        private void UpdateCanvasSize()
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            var img = vm.CurrentPageImage;
            if (img == null) return;

            PdfCanvas.Width = img.PixelWidth;
            PdfCanvas.Height = img.PixelHeight;
            PdfImage.Width = img.PixelWidth;
            PdfImage.Height = img.PixelHeight;

            RenderTagOverlays();
        }

        private void PdfCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) return;

            if (!_isPlacingMode || _pendingTag == null) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            var pos = e.GetPosition(PdfCanvas);
            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;

            if (canvasW <= 0 || canvasH <= 0) return;

            double xPercent = pos.X / canvasW;
            double yPercent = pos.Y / canvasH;

            vm.PlaceTagAtPosition(_pendingTag, xPercent, yPercent);

            _isPlacingMode = false;
            _pendingTag = null;
            PdfCanvas.Cursor = Cursors.Arrow;

            RenderTagOverlays();
        }

        private void RenderTagOverlays()
        {
            if (DataContext is not PdfEditorViewModel vm) return;

            // Remove old overlays (keep the PdfImage)
            var toRemove = PdfCanvas.Children.OfType<UIElement>()
                .Where(c => c != PdfImage).ToList();
            foreach (var el in toRemove)
                PdfCanvas.Children.Remove(el);

            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            foreach (var placement in vm.CurrentPagePlacements)
            {
                double x = placement.X * canvasW;
                double y = placement.Y * canvasH;

                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(220, 227, 242, 253)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 2, 5, 2),
                    Cursor = Cursors.SizeAll,
                    Tag = placement
                };

                var stack = new StackPanel { Orientation = Orientation.Horizontal };

                var icon = new TextBlock
                {
                    Text = "\uE819",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0)
                };

                var text = new TextBlock
                {
                    Text = $"${{{placement.Tag}}}",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)),
                    FontFamily = new FontFamily("Consolas"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                stack.Children.Add(icon);
                stack.Children.Add(text);
                border.Child = stack;

                border.MouseLeftButtonDown += TagBorder_MouseLeftButtonDown;
                border.MouseMove += TagBorder_MouseMove;
                border.MouseLeftButtonUp += TagBorder_MouseLeftButtonUp;

                Canvas.SetLeft(border, x);
                Canvas.SetTop(border, y);
                PdfCanvas.Children.Add(border);

                // Small position dot
                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    Fill = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.5,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, x - 4);
                Canvas.SetTop(dot, y - 4);
                PdfCanvas.Children.Add(dot);
            }
        }

        private void TagBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not PdfPlacementViewModel placement) return;

            _isDragging = true;
            _dragPlacement = placement;
            _dragElement = border;
            _dragStartMouse = e.GetPosition(PdfCanvas);
            _dragStartLeft = Canvas.GetLeft(border);
            _dragStartTop = Canvas.GetTop(border);

            border.BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
            border.Background = new SolidColorBrush(Color.FromArgb(230, 255, 243, 224));
            border.Opacity = 0.9;

            border.CaptureMouse();
            e.Handled = true;
        }

        private void TagBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _dragElement == null || _dragPlacement == null) return;

            var currentPos = e.GetPosition(PdfCanvas);
            var deltaX = currentPos.X - _dragStartMouse.X;
            var deltaY = currentPos.Y - _dragStartMouse.Y;

            var newLeft = _dragStartLeft + deltaX;
            var newTop = _dragStartTop + deltaY;

            // Clamp to canvas
            newLeft = System.Math.Clamp(newLeft, 0, PdfCanvas.Width - 20);
            newTop = System.Math.Clamp(newTop, 0, PdfCanvas.Height - 10);

            Canvas.SetLeft(_dragElement, newLeft);
            Canvas.SetTop(_dragElement, newTop);

            e.Handled = true;
        }

        private void TagBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging || _dragElement == null || _dragPlacement == null) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            _dragElement.ReleaseMouseCapture();

            var finalLeft = Canvas.GetLeft(_dragElement);
            var finalTop = Canvas.GetTop(_dragElement);

            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;

            if (canvasW > 0 && canvasH > 0)
            {
                double newXPercent = finalLeft / canvasW;
                double newYPercent = finalTop / canvasH;
                vm.UpdatePlacementPosition(_dragPlacement, newXPercent, newYPercent);
            }

            _isDragging = false;
            _dragPlacement = null;
            _dragElement = null;

            RenderTagOverlays();
            e.Handled = true;
        }
    }
}

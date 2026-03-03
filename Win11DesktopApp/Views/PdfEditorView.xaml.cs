using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private AITemplateOverlayWindow? _aiOverlay;

        private bool _isDragging;
        private PdfPlacementViewModel? _dragPlacement;
        private Border? _dragElement;
        private Point _dragStartMouse;
        private double _dragStartLeft;
        private double _dragStartTop;

        private const double AlignSnapThreshold = 4.0;

        public PdfEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Focusable = true;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateCanvasSize();
            Focus();
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
                SubscribePlacementChanges();
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
                    SubscribePlacementChanges();
                });
            }
        }

        private void SubscribePlacementChanges()
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            foreach (var p in vm.AllPlacements)
            {
                p.PropertyChanged -= Placement_PropertyChanged;
                p.PropertyChanged += Placement_PropertyChanged;
            }
        }

        private void Placement_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PdfPlacementViewModel.FontSize)
                or nameof(PdfPlacementViewModel.FontFamily)
                or nameof(PdfPlacementViewModel.MaxWidth))
            {
                Dispatcher.InvokeAsync(RenderTagOverlays);
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

            if (!_isPlacingMode || _pendingTag == null)
            {
                if (DataContext is PdfEditorViewModel vm2)
                    vm2.SelectedPlacement = null;
                return;
            }

            if (DataContext is not PdfEditorViewModel vm) return;

            var pos = e.GetPosition(PdfCanvas);
            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            double xPercent = pos.X / canvasW;
            double yPercent = pos.Y / canvasH;

            if (vm.SnapToGrid)
            {
                xPercent = vm.SnapXPercent(xPercent);
                yPercent = vm.SnapYPercent(yPercent);
            }

            vm.PlaceTagAtPosition(_pendingTag, xPercent, yPercent);

            _isPlacingMode = false;
            _pendingTag = null;
            PdfCanvas.Cursor = Cursors.Arrow;

            RenderTagOverlays();
            SubscribePlacementChanges();
            Focus();
        }

        private void PdfCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.ZoomLevel += e.Delta > 0 ? 0.1 : -0.1;
            e.Handled = true;
        }

        #region Grid Drawing

        private void DrawGridLines(PdfEditorViewModel vm, double canvasW, double canvasH)
        {
            if (!vm.ShowGrid || vm.GridSpacingPt <= 0 || vm.PdfPageHeight <= 0) return;

            double pxPerPt = canvasH / vm.PdfPageHeight;
            double spacingPx = vm.GridSpacingPt * pxPerPt;
            if (spacingPx < 3) return;

            var pen = new SolidColorBrush(Color.FromArgb(40, 0, 120, 215));

            for (double y = spacingPx; y < canvasH; y += spacingPx)
            {
                var line = new Line
                {
                    X1 = 0, X2 = canvasW,
                    Y1 = y, Y2 = y,
                    Stroke = pen,
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false
                };
                PdfCanvas.Children.Add(line);
            }

            double spacingPxX = vm.GridSpacingPt * (canvasW / vm.PdfPageWidth);
            for (double x = spacingPxX; x < canvasW; x += spacingPxX)
            {
                var line = new Line
                {
                    X1 = x, X2 = x,
                    Y1 = 0, Y2 = canvasH,
                    Stroke = pen,
                    StrokeThickness = 0.5,
                    IsHitTestVisible = false
                };
                PdfCanvas.Children.Add(line);
            }
        }

        #endregion

        #region Tag Overlays

        private void RenderTagOverlays()
        {
            if (DataContext is not PdfEditorViewModel vm) return;

            var toRemove = PdfCanvas.Children.OfType<UIElement>()
                .Where(c => c != PdfImage).ToList();
            foreach (var el in toRemove)
                PdfCanvas.Children.Remove(el);

            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            DrawGridLines(vm, canvasW, canvasH);

            var accentBrush = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192));
            var bgBrush = new SolidColorBrush(Color.FromArgb(200, 227, 242, 253));
            var selectedBorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
            var selectedBg = new SolidColorBrush(Color.FromArgb(220, 255, 243, 224));

            foreach (var placement in vm.CurrentPagePlacements)
            {
                double x = placement.X * canvasW;
                double y = placement.Y * canvasH;

                double scaledFontSize = Math.Max(8, placement.FontSize * (canvasH / vm.PdfPageHeight) * 0.75);
                bool isSelected = placement.IsSelected;

                var border = new Border
                {
                    Background = isSelected ? selectedBg : bgBrush,
                    BorderBrush = isSelected ? selectedBorderBrush : accentBrush,
                    BorderThickness = new Thickness(isSelected ? 2 : 1.5),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 1, 4, 1),
                    Cursor = Cursors.SizeAll,
                    Tag = placement
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var deleteBtn = new Button
                {
                    Content = "\uE711",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = Math.Max(8, scaledFontSize * 0.6),
                    Width = Math.Max(13, scaledFontSize * 0.85),
                    Height = Math.Max(13, scaledFontSize * 0.85),
                    Background = new SolidColorBrush(Color.FromArgb(200, 220, 40, 40)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 3, 0)
                };
                deleteBtn.Tag = placement;
                deleteBtn.Click += DeleteBtn_Click;
                Grid.SetColumn(deleteBtn, 0);
                grid.Children.Add(deleteBtn);

                var textStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var text = new TextBlock
                {
                    Text = $"${{{placement.Tag}}}",
                    FontSize = scaledFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isSelected ? new SolidColorBrush(Color.FromArgb(255, 230, 81, 0)) : accentBrush,
                    FontFamily = new FontFamily(placement.FontFamily ?? "Consolas"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var sizeInfo = new TextBlock
                {
                    Text = $" {placement.FontSize}pt",
                    FontSize = Math.Max(7, scaledFontSize * 0.55),
                    Foreground = new SolidColorBrush(Color.FromArgb(150, 100, 100, 100)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(3, 0, 0, 0)
                };

                textStack.Children.Add(text);
                textStack.Children.Add(sizeInfo);
                Grid.SetColumn(textStack, 1);
                grid.Children.Add(textStack);

                border.Child = grid;

                if (placement.MaxWidth > 0)
                {
                    double scaledMaxW = placement.MaxWidth * (canvasW / vm.PdfPageWidth);
                    border.MaxWidth = scaledMaxW + 20;
                }

                border.MouseLeftButtonDown += TagBorder_MouseLeftButtonDown;
                border.MouseMove += TagBorder_MouseMove;
                border.MouseLeftButtonUp += TagBorder_MouseLeftButtonUp;

                Canvas.SetLeft(border, x);
                Canvas.SetTop(border, y);
                PdfCanvas.Children.Add(border);

                var dot = new Ellipse
                {
                    Width = 6, Height = 6,
                    Fill = isSelected ? selectedBorderBrush : accentBrush,
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(dot, x - 3);
                Canvas.SetTop(dot, y - 3);
                PdfCanvas.Children.Add(dot);
            }
        }

        private void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (btn.Tag is not PdfPlacementViewModel placement) return;
            if (DataContext is not PdfEditorViewModel vm) return;
            vm.RemovePlacementCommand.Execute(placement);
            e.Handled = true;
        }

        #endregion

        #region Drag & Drop + Selection

        private void TagBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not PdfPlacementViewModel placement) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectedPlacement = placement;
            vm.UpdateCoordinateText(placement);

            _isDragging = true;
            _dragPlacement = placement;
            _dragElement = border;
            _dragStartMouse = e.GetPosition(PdfCanvas);
            _dragStartLeft = Canvas.GetLeft(border);
            _dragStartTop = Canvas.GetTop(border);

            border.CaptureMouse();
            e.Handled = true;
            Focus();
        }

        private void TagBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || _dragElement == null || _dragPlacement == null) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            var currentPos = e.GetPosition(PdfCanvas);
            var deltaX = currentPos.X - _dragStartMouse.X;
            var deltaY = currentPos.Y - _dragStartMouse.Y;

            var newLeft = _dragStartLeft + deltaX;
            var newTop = _dragStartTop + deltaY;

            newLeft = Math.Clamp(newLeft, 0, PdfCanvas.Width - 10);
            newTop = Math.Clamp(newTop, 0, PdfCanvas.Height - 10);

            // Alignment guides: snap to other tags' Y positions
            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            double? alignY = null;

            foreach (var other in vm.CurrentPagePlacements)
            {
                if (other == _dragPlacement) continue;
                double otherY = other.Y * canvasH;
                if (Math.Abs(newTop - otherY) < AlignSnapThreshold)
                {
                    newTop = otherY;
                    alignY = otherY;
                    break;
                }
            }

            ShowAlignmentGuide(alignY, canvasW);

            Canvas.SetLeft(_dragElement, newLeft);
            Canvas.SetTop(_dragElement, newTop);

            // Update coordinate display in real time
            if (canvasW > 0 && canvasH > 0)
            {
                double tmpXPt = Math.Round(newLeft / canvasW * vm.PdfPageWidth, 1);
                double tmpYPt = Math.Round(newTop / canvasH * vm.PdfPageHeight, 1);
                vm.CoordinateText = $"X: {tmpXPt}pt  Y: {tmpYPt}pt";
            }

            e.Handled = true;
        }

        private Line? _alignGuide;

        private void ShowAlignmentGuide(double? y, double canvasW)
        {
            if (_alignGuide != null)
            {
                PdfCanvas.Children.Remove(_alignGuide);
                _alignGuide = null;
            }
            if (y == null) return;

            _alignGuide = new Line
            {
                X1 = 0, X2 = canvasW,
                Y1 = y.Value, Y2 = y.Value,
                Stroke = new SolidColorBrush(Color.FromArgb(180, 255, 152, 0)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            PdfCanvas.Children.Add(_alignGuide);
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

                if (vm.SnapToGrid)
                {
                    newYPercent = vm.SnapYPercent(newYPercent);
                    newXPercent = vm.SnapXPercent(newXPercent);
                }

                vm.UpdatePlacementPosition(_dragPlacement, newXPercent, newYPercent);
                vm.UpdateCoordinateText(_dragPlacement);
            }

            ShowAlignmentGuide(null, 0);

            _isDragging = false;
            _dragPlacement = null;
            _dragElement = null;

            RenderTagOverlays();
            e.Handled = true;
        }

        private void PlacedTag_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not PdfPlacementViewModel placement) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectedPlacement = placement;
            vm.UpdateCoordinateText(placement);

            if (placement.Page != vm.CurrentPageIndex)
                vm.CurrentPageIndex = placement.Page;

            e.Handled = true;
            Focus();
        }

        #endregion

        #region AI Overlay

        private void AIOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_aiOverlay == null || !_aiOverlay.IsLoaded)
            {
                _aiOverlay = new AITemplateOverlayWindow();
                _aiOverlay.Owner = Window.GetWindow(this);
                _aiOverlay.SetContentProviders(GetPdfContent, GetTagCatalogText);
            }

            if (_aiOverlay.IsVisible)
                _aiOverlay.Hide();
            else
                _aiOverlay.Show();
        }

        private string? GetPdfContent()
        {
            if (DataContext is not PdfEditorViewModel vm) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"PDF template: {vm.Title}");
            sb.AppendLine($"Pages: {vm.PageCount}");
            sb.AppendLine($"Page size: {vm.PdfPageWidth:F0}x{vm.PdfPageHeight:F0} pt");
            sb.AppendLine();
            sb.AppendLine("Placed tags:");
            foreach (var p in vm.AllPlacements)
            {
                sb.AppendLine($"  ${{{p.Tag}}} — page {p.Page + 1}, X={Math.Round(p.X * vm.PdfPageWidth, 1)}pt Y={Math.Round(p.Y * vm.PdfPageHeight, 1)}pt, font={p.FontFamily} {p.FontSize}pt");
            }
            return sb.ToString();
        }

        private string? GetTagCatalogText()
        {
            if (DataContext is not PdfEditorViewModel vm) return null;
            var sb = new StringBuilder();
            foreach (var group in vm.TagGroups)
            {
                sb.AppendLine($"[{group.GroupName}]");
                foreach (var tag in group.Tags)
                    sb.AppendLine($"  ${{{tag.Tag}}} — {tag.Description}");
            }
            return sb.ToString();
        }

        #endregion

        #region Arrow Key Nudge

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            if (vm.SelectedPlacement == null) return;

            double stepPx = 1.0;
            if (Keyboard.Modifiers == ModifierKeys.Shift)
                stepPx = 5.0;

            double dxPercent = 0, dyPercent = 0;
            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            switch (e.Key)
            {
                case Key.Up:    dyPercent = -stepPx / canvasH; break;
                case Key.Down:  dyPercent = stepPx / canvasH; break;
                case Key.Left:  dxPercent = -stepPx / canvasW; break;
                case Key.Right: dxPercent = stepPx / canvasW; break;
                case Key.Escape:
                    vm.SelectedPlacement = null;
                    vm.ClearCoordinateText();
                    RenderTagOverlays();
                    e.Handled = true;
                    return;
                case Key.Delete:
                    var sel = vm.SelectedPlacement;
                    vm.RemovePlacementCommand.Execute(sel);
                    e.Handled = true;
                    return;
                default: return;
            }

            vm.NudgeSelected(dxPercent, dyPercent);
            e.Handled = true;
        }

        #endregion
    }
}

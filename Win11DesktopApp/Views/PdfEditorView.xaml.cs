using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private bool _isPlacingInlineTextRow;
        private bool _isPlacingField;

        private bool _isDragging;
        private PdfPlacementViewModel? _dragPlacement;
        private FrameworkElement? _dragElement;
        private Point _dragStartMouse;
        private double _dragStartLeft;
        private double _dragStartTop;
        private bool _isApplyingSavedLayout;

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
            ApplySavedLayout();
            UpdateCanvasSize();
            Focus();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is PdfEditorViewModel oldVm)
            {
                oldVm.PropertyChanged -= Vm_PropertyChanged;
                oldVm.RequestPlaceTagOnClick = null;
                oldVm.RequestPlaceInlineTextRowOnClick = null;
                oldVm.RequestPlaceFieldOnClick = null;
                oldVm.RequestFocusInlineTemplateEditor = null;
                oldVm.RequestFocusFormFieldEditor = null;
                oldVm.RequestRenderOverlays -= RenderTagOverlays;
                UnsubscribeFormFieldChanges(oldVm);
            }

            if (e.NewValue is PdfEditorViewModel vm)
            {
                vm.PropertyChanged += Vm_PropertyChanged;
                vm.RequestPlaceTagOnClick = tag =>
                {
                    _pendingTag = tag;
                    _isPlacingMode = true;
                    _isPlacingInlineTextRow = false;
                    _isPlacingField = false;
                    PdfCanvas.Cursor = Cursors.Cross;
                };
                vm.RequestPlaceInlineTextRowOnClick = () =>
                {
                    _pendingTag = null;
                    _isPlacingMode = false;
                    _isPlacingInlineTextRow = true;
                    _isPlacingField = false;
                    PdfCanvas.Cursor = Cursors.Cross;
                };
                vm.RequestPlaceFieldOnClick = () =>
                {
                    _pendingTag = null;
                    _isPlacingMode = false;
                    _isPlacingInlineTextRow = false;
                    _isPlacingField = true;
                    PdfCanvas.Cursor = Cursors.Cross;
                };
                vm.RequestFocusInlineTemplateEditor = caretIndex =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        InlineTemplateTextBox.Focus();
                        InlineTemplateTextBox.CaretIndex = Math.Clamp(caretIndex, 0, InlineTemplateTextBox.Text?.Length ?? 0);
                        InlineTemplateTextBox.SelectionLength = 0;
                    });
                };
                vm.RequestFocusFormFieldEditor = (binding, caretIndex) =>
                {
                    Dispatcher.InvokeAsync(() => FocusFormFieldTextBox(binding, caretIndex));
                };
                vm.RequestRenderOverlays += RenderTagOverlays;
                UpdateCanvasSize();
                SubscribePlacementChanges();
                SubscribeFormFieldChanges(vm);
            }
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PdfEditorViewModel.CurrentPageImage) ||
                e.PropertyName == nameof(PdfEditorViewModel.CurrentPagePlacements) ||
                e.PropertyName == nameof(PdfEditorViewModel.PdfMode) ||
                e.PropertyName == nameof(PdfEditorViewModel.SelectedFormFieldBinding) ||
                e.PropertyName == nameof(PdfEditorViewModel.FormFieldBindings))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    UpdateCanvasSize();
                    RenderTagOverlays();
                    SubscribePlacementChanges();
                    if (DataContext is PdfEditorViewModel vm)
                        SubscribeFormFieldChanges(vm);
                });
            }

            if (e.PropertyName == nameof(PdfEditorViewModel.IsAIPdfSuggestionsOpen)
                || e.PropertyName == nameof(PdfEditorViewModel.PdfMode))
            {
                Dispatcher.InvokeAsync(UpdateAiPanelLayoutState);
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
                or nameof(PdfPlacementViewModel.MaxWidth)
                or nameof(PdfPlacementViewModel.BoxHeight)
                or nameof(PdfPlacementViewModel.TextAlign)
                or nameof(PdfPlacementViewModel.TemplateText))
            {
                Dispatcher.InvokeAsync(RenderTagOverlays);
            }
        }

        private void SubscribeFormFieldChanges(PdfEditorViewModel vm)
        {
            foreach (var field in vm.FormFieldBindings)
            {
                field.PropertyChanged -= FormField_PropertyChanged;
                field.PropertyChanged += FormField_PropertyChanged;
            }
        }

        private void UnsubscribeFormFieldChanges(PdfEditorViewModel vm)
        {
            foreach (var field in vm.FormFieldBindings)
                field.PropertyChanged -= FormField_PropertyChanged;
        }

        private void FormField_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PdfFormFieldBindingViewModel.TemplateText)
                or nameof(PdfFormFieldBindingViewModel.DisplayText)
                or nameof(PdfFormFieldBindingViewModel.IsSelected))
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

            if ((!_isPlacingMode || _pendingTag == null) && !_isPlacingInlineTextRow && !_isPlacingField)
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

            if (_isPlacingInlineTextRow)
                vm.PlaceInlineTextRowAtPosition(xPercent, yPercent);
            else if (_isPlacingField)
                vm.PlaceFieldAtPosition(xPercent, yPercent);
            else if (_pendingTag != null)
                vm.PlaceTagAtPosition(_pendingTag, xPercent, yPercent);

            _isPlacingMode = false;
            _isPlacingInlineTextRow = false;
            _isPlacingField = false;
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

        private double GetOverlayPixelsPerDip()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
                return source.CompositionTarget.TransformToDevice.M11;
            return 1.0;
        }

        private static (double width, double height, double baseline) MeasureOverlayText(
            string text,
            string fontFamily,
            double fontSizePx,
            double maxTextWidthPx,
            double pixelsPerDip,
            bool allowWrap)
        {
            var sample = string.IsNullOrWhiteSpace(text) ? " " : text;
            var typeface = new Typeface(
                new FontFamily(fontFamily),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

            var formatted = new FormattedText(
                sample,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSizePx,
                Brushes.Black,
                pixelsPerDip);

            if (maxTextWidthPx > 0)
                formatted.MaxTextWidth = maxTextWidthPx;

            if (!allowWrap)
                formatted.MaxLineCount = 1;

            return (Math.Ceiling(formatted.WidthIncludingTrailingWhitespace),
                Math.Ceiling(formatted.Height),
                formatted.Baseline);
        }

        // Returns the font's glyph-cell metrics in pixels (ascent and full cell height = ascent+descent),
        // matching the metrics PdfSharp uses when it draws the generated text. This lets the editor box
        // hug the font tightly and sit exactly where the generated text will land.
        private static (double ascentPx, double cellHeightPx) GetFontCellMetrics(string fontFamily, double emPx)
        {
            try
            {
                var typeface = new Typeface(
                    new FontFamily(fontFamily),
                    FontStyles.Normal,
                    FontWeights.Normal,
                    FontStretches.Normal);

                if (typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    var ascent = glyphTypeface.Baseline * emPx;
                    var cell = glyphTypeface.Height * emPx;
                    if (ascent > 0 && cell > 0)
                        return (ascent, cell);
                }
            }
            catch
            {
                // Fall through to approximate metrics below.
            }

            // Typical Latin font fallback (ascent ~0.9 em, cell ~1.15 em).
            return (emPx * 0.9, emPx * 1.15);
        }

        private void RenderTagOverlays()
        {
            if (DataContext is not PdfEditorViewModel vm) return;

            var toRemove = PdfCanvas.Children.OfType<UIElement>()
                .Where(c => c != PdfImage).ToList();
            foreach (var el in toRemove)
                PdfCanvas.Children.Remove(el);

            if (vm.IsFormMode)
            {
                RenderSelectedFormFieldOverlay(vm);
                return;
            }

            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0) return;

            DrawGridLines(vm, canvasW, canvasH);

            var accentBrush = new SolidColorBrush(Color.FromArgb(255, 21, 101, 192));
            var bgBrush = new SolidColorBrush(Color.FromArgb(200, 227, 242, 253));
            var selectedBorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0));
            var selectedBg = new SolidColorBrush(Color.FromArgb(220, 255, 243, 224));

            var pixelsPerDip = GetOverlayPixelsPerDip();
            var pageScaleY = canvasH / vm.PdfPageHeight;
            var pageScaleX = canvasW / vm.PdfPageWidth;

            foreach (var placement in vm.CurrentPagePlacements)
            {
                double x = placement.X * canvasW;
                double y = placement.Y * canvasH;

                var pdfFontSize = placement.FontSize > 0 ? placement.FontSize : 10;
                var fontFamily = string.IsNullOrWhiteSpace(placement.FontFamily) ? "Arial" : placement.FontFamily;
                double scaledFontSize = Math.Max(6, pdfFontSize * pageScaleY);
                bool isSelected = placement.IsSelected;
                bool isField = placement.IsField;
                bool allowWrap = isField || placement.IsInlineText;
                double scaledMaxW = placement.MaxWidth > 0
                    ? placement.MaxWidth * pageScaleX
                    : 0;

                var (textWidth, textHeight, textBaseline) = MeasureOverlayText(
                    placement.OverlayText,
                    fontFamily,
                    scaledFontSize,
                    scaledMaxW,
                    pixelsPerDip,
                    allowWrap);

                // WYSIWYG box: hug the font cell (ascent+descent) and anchor it at the placement origin,
                // so the box mirrors exactly where and how tall the generated PDF text will be.
                var (ascentPx, cellHeightPx) = GetFontCellMetrics(fontFamily, scaledFontSize);
                var deleteSize = Math.Max(12, scaledFontSize * 0.85);
                var contentWidth = Math.Max(8, textWidth);
                // Single-line tags hug the glyph cell; wrapped fields grow to the measured text height.
                var contentHeight = (allowWrap && scaledMaxW > 0)
                    ? Math.Max(cellHeightPx, textHeight)
                    : cellHeightPx;
                // Shift the preview text up by the WPF line-box leading so its baseline lands at the same
                // spot PdfSharp uses when generating (top-of-cell + ascent).
                var textTopOffset = ascentPx - textBaseline;

                var textBlock = new TextBlock
                {
                    Text = placement.OverlayText,
                    FontSize = scaledFontSize,
                    FontWeight = FontWeights.Normal,
                    Foreground = isSelected
                        ? new SolidColorBrush(Color.FromArgb(255, 230, 81, 0))
                        : accentBrush,
                    FontFamily = new FontFamily(fontFamily),
                    TextTrimming = allowWrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                    TextWrapping = allowWrap && scaledMaxW > 0 ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MaxWidth = scaledMaxW > 0 ? Math.Max(8, scaledMaxW) : double.PositiveInfinity,
                    TextAlignment = string.Equals(placement.TextAlign, "center", StringComparison.OrdinalIgnoreCase)
                        ? TextAlignment.Center
                        : string.Equals(placement.TextAlign, "right", StringComparison.OrdinalIgnoreCase)
                            ? TextAlignment.Right
                            : TextAlignment.Left
                };

                var chrome = new Border
                {
                    Width = contentWidth,
                    Height = contentHeight,
                    Background = isSelected ? selectedBg : bgBrush,
                    BorderBrush = isSelected ? selectedBorderBrush : accentBrush,
                    BorderThickness = new Thickness(isSelected ? 2 : 1),
                    CornerRadius = new CornerRadius(2),
                    IsHitTestVisible = false
                };

                var host = new Canvas
                {
                    Tag = placement,
                    Cursor = Cursors.SizeAll,
                    Background = Brushes.Transparent,
                    Width = contentWidth,
                    Height = contentHeight
                };

                var deleteBtn = new Button
                {
                    Content = "\uE711",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = Math.Max(7, scaledFontSize * 0.55),
                    Width = deleteSize,
                    Height = deleteSize,
                    Background = new SolidColorBrush(Color.FromArgb(200, 220, 40, 40)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Cursor = Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                deleteBtn.Tag = placement;
                deleteBtn.Click += DeleteBtn_Click;

                Canvas.SetLeft(chrome, 0);
                Canvas.SetTop(chrome, 0);
                Canvas.SetLeft(textBlock, 0);
                Canvas.SetTop(textBlock, textTopOffset);
                Canvas.SetLeft(deleteBtn, -deleteSize - 3);
                Canvas.SetTop(deleteBtn, Math.Max(0, (contentHeight - deleteSize) / 2));

                host.Children.Add(chrome);
                host.Children.Add(textBlock);
                host.Children.Add(deleteBtn);

                host.MouseLeftButtonDown += TagBorder_MouseLeftButtonDown;
                host.MouseMove += TagBorder_MouseMove;
                host.MouseLeftButtonUp += TagBorder_MouseLeftButtonUp;

                Canvas.SetLeft(host, x);
                Canvas.SetTop(host, y);
                PdfCanvas.Children.Add(host);

                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
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

        private void RenderSelectedFormFieldOverlay(PdfEditorViewModel vm)
        {
            var canvasW = PdfCanvas.Width;
            var canvasH = PdfCanvas.Height;
            if (canvasW <= 0 || canvasH <= 0 || vm.PdfPageWidth <= 0 || vm.PdfPageHeight <= 0)
                return;

            var scaleX = canvasW / vm.PdfPageWidth;
            var scaleY = canvasH / vm.PdfPageHeight;
            var currentPageFields = vm.FormFieldBindings
                .Where(f => f.HasBounds && f.Page == vm.CurrentPageIndex)
                .ToList();

            foreach (var field in currentPageFields)
            {
                var isSelected = ReferenceEquals(field, vm.SelectedFormFieldBinding);
                var left = field.X * scaleX;
                var width = Math.Max(8, field.Width * scaleX);
                var height = Math.Max(8, field.Height * scaleY);
                var top = canvasH - ((field.Y + field.Height) * scaleY);

                var highlight = new Rectangle
                {
                    Width = width,
                    Height = height,
                    RadiusX = 3,
                    RadiusY = 3,
                    Fill = isSelected
                        ? new SolidColorBrush(Color.FromArgb(70, 255, 193, 7))
                        : new SolidColorBrush(Color.FromArgb(28, 33, 150, 243)),
                    Stroke = isSelected
                        ? new SolidColorBrush(Color.FromArgb(255, 255, 152, 0))
                        : new SolidColorBrush(Color.FromArgb(220, 33, 150, 243)),
                    StrokeThickness = isSelected ? 2.5 : 1.5,
                    Cursor = Cursors.Hand,
                    Tag = field
                };
                highlight.MouseLeftButtonDown += FormFieldOverlay_MouseLeftButtonDown;
                Canvas.SetLeft(highlight, left);
                Canvas.SetTop(highlight, top);
                PdfCanvas.Children.Add(highlight);

                var displayText = field.DisplayText;
                if (!string.IsNullOrWhiteSpace(displayText))
                {
                    var text = new TextBlock
                    {
                        Text = displayText,
                        FontSize = Math.Max(8, Math.Min(height * 0.55, 12)),
                        FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                        Foreground = isSelected
                            ? new SolidColorBrush(Color.FromArgb(255, 140, 74, 0))
                            : new SolidColorBrush(Color.FromArgb(255, 21, 101, 192)),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Width = Math.Max(10, width - 8),
                        Height = Math.Max(10, height - 4),
                        Padding = new Thickness(2, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(text, left + 4);
                    Canvas.SetTop(text, top + Math.Max(1, (height - text.FontSize - 2) / 2));
                    PdfCanvas.Children.Add(text);
                }

                if (!isSelected)
                    continue;

                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(230, 255, 243, 224)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 255, 152, 0)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = field.FieldName,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 74, 0))
                    }
                };
                Canvas.SetLeft(label, Math.Max(0, left));
                Canvas.SetTop(label, Math.Max(0, top - 26));
                PdfCanvas.Children.Add(label);
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
            if (sender is not FrameworkElement border) return;
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
            if (sender is not FrameworkElement border) return;
            if (border.Tag is not PdfPlacementViewModel placement) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectedPlacement = placement;
            vm.UpdateCoordinateText(placement);

            if (placement.Page != vm.CurrentPageIndex)
                vm.CurrentPageIndex = placement.Page;

            e.Handled = true;
            Focus();
        }

        private void FormField_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not PdfFormFieldBindingViewModel binding) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectFormField(binding);
            e.Handled = true;
            Focus();
        }

        private void FormFieldOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Shape shape) return;
            if (shape.Tag is not PdfFormFieldBindingViewModel binding) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectFormField(binding);
            FormFieldListBox?.ScrollIntoView(binding);
            e.Handled = true;
            Focus();
        }

        private void FormFieldListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            if (listBox.SelectedItem is not PdfFormFieldBindingViewModel binding) return;

            listBox.ScrollIntoView(binding);
        }

        #endregion

        private void InlineTemplateTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            vm.InlineTemplateCaretIndex = InlineTemplateTextBox.CaretIndex;
        }

        private void InlineTemplateTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            vm.IsInlineTemplateEditorFocused = true;
            vm.InlineTemplateCaretIndex = InlineTemplateTextBox.CaretIndex;
        }

        private void InlineTemplateTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            vm.IsInlineTemplateEditorFocused = false;
            vm.InlineTemplateCaretIndex = InlineTemplateTextBox.CaretIndex;
        }

        private void FormFieldTemplateTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not PdfFormFieldBindingViewModel binding) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.SelectFormField(binding);
            vm.UpdateFormFieldEditorState(binding, textBox.CaretIndex, true);
        }

        private void FormFieldTemplateTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not PdfFormFieldBindingViewModel binding) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.UpdateFormFieldEditorState(binding, textBox.CaretIndex, textBox.IsKeyboardFocused);
        }

        private void FormFieldTemplateTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            if (textBox.DataContext is not PdfFormFieldBindingViewModel binding) return;
            if (DataContext is not PdfEditorViewModel vm) return;

            vm.UpdateFormFieldEditorState(binding, textBox.CaretIndex, false);
        }

        private void FocusFormFieldTextBox(PdfFormFieldBindingViewModel binding, int caretIndex)
        {
            FormFieldListBox?.ScrollIntoView(binding);
            FormFieldListBox?.UpdateLayout();

            if (FormFieldListBox?.ItemContainerGenerator.ContainerFromItem(binding) is not ListBoxItem item)
                return;

            var textBox = FindDescendant<TextBox>(item);
            if (textBox == null)
                return;

            textBox.Focus();
            textBox.CaretIndex = Math.Clamp(caretIndex, 0, textBox.Text?.Length ?? 0);
            textBox.SelectionLength = 0;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    return match;

                var nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        private void ApplySavedLayout()
        {
            if (DataContext is not PdfEditorViewModel vm)
            {
                UpdateAiPanelLayoutState();
                return;
            }

            var settings = vm.AppSettingsService.Settings;

            _isApplyingSavedLayout = true;
            try
            {
                RightSidebarColumn.Width = new GridLength(Math.Max(240, settings.PdfEditorSidebarWidth));
                FieldsPanelRow.Height = new GridLength(Math.Max(180, settings.PdfEditorFieldsPanelHeight));
                AiPanelRow.Height = new GridLength(Math.Max(160, settings.PdfEditorAiPanelHeight));
            }
            finally
            {
                _isApplyingSavedLayout = false;
            }

            UpdateAiPanelLayoutState();
        }

        private void UpdateAiPanelLayoutState()
        {
            if (DataContext is not PdfEditorViewModel vm)
                return;

            var isVisible = vm.IsAIPdfSuggestionsVisible;
            AiPanelSplitterRow.Height = isVisible ? GridLength.Auto : new GridLength(0);
            if (isVisible)
            {
                if (AiPanelRow.Height.Value <= 0)
                    AiPanelRow.Height = new GridLength(Math.Max(160, vm.AppSettingsService.Settings.PdfEditorAiPanelHeight));
            }
            else
            {
                AiPanelRow.Height = new GridLength(0);
            }
        }

        private void SavePdfEditorLayout()
        {
            if (_isApplyingSavedLayout)
                return;

            if (DataContext is not PdfEditorViewModel vm)
                return;

            var settings = vm.AppSettingsService.Settings;

            if (RightSidebarColumn.ActualWidth > 0)
                settings.PdfEditorSidebarWidth = Math.Max(240, RightSidebarColumn.ActualWidth);

            if (FieldsPanelRow.ActualHeight > 0)
                settings.PdfEditorFieldsPanelHeight = Math.Max(180, FieldsPanelRow.ActualHeight);

            if (AiPanelRow.ActualHeight > 0)
                settings.PdfEditorAiPanelHeight = Math.Max(160, AiPanelRow.ActualHeight);

            vm.AppSettingsService.SaveSettings();
        }

        private void AiPanelGridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SavePdfEditorLayout();
        }

        private void FieldsPanelGridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SavePdfEditorLayout();
        }

        private void SidebarGridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            SavePdfEditorLayout();
        }

        #region Arrow Key Nudge

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not PdfEditorViewModel vm) return;
            if (!vm.IsOverlayMode) return;
            if (vm.SelectedPlacement == null) return;
            if (InlineTemplateTextBox.IsKeyboardFocusWithin || vm.IsInlineTemplateEditorFocused) return;

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

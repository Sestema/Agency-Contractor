using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public class CardDragAdorner : Adorner
    {
        private readonly VisualBrush _visualBrush;
        private readonly Size _cardSize;
        private Point _offset;

        public CardDragAdorner(UIElement adornedElement, UIElement cardVisual, Point startOffset)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            _cardSize = new Size(cardVisual.RenderSize.Width, cardVisual.RenderSize.Height);
            _offset = startOffset;
            _visualBrush = new VisualBrush(cardVisual)
            {
                Opacity = 0.82,
                Stretch = Stretch.None
            };
        }

        public void UpdatePosition(Point pos)
        {
            _offset = pos;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var rect = new Rect(
                _offset.X - _cardSize.Width / 2,
                _offset.Y - _cardSize.Height / 2,
                _cardSize.Width, _cardSize.Height);
            dc.PushOpacity(0.82);
            dc.DrawRectangle(_visualBrush, null, rect);
            dc.Pop();
        }
    }

    public partial class MainPage : UserControl
    {
        private Point _dragStartPoint;
        private bool _isDragActive;
        private MenuCardItem? _draggedItem;
        private Border? _draggedBorder;
        private CardDragAdorner? _adorner;
        private AdornerLayer? _adornerLayer;
        private int _currentDropIndex = -1;
        private int _dragSourceIndex = -1;
        private const double DragThreshold = 10;

        public MainPage()
        {
            InitializeComponent();
        }

        private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border)
            {
                _dragStartPoint = e.GetPosition(this);
                _isDragActive = false;
                _draggedBorder = border;
            }
        }

        private void Card_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedBorder == null) return;

            var pos = e.GetPosition(this);
            var diff = pos - _dragStartPoint;
            if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
                return;

            if (_isDragActive) return;

            if (_draggedBorder.DataContext is MenuCardItem item && DataContext is MainViewModel vm)
            {
                _isDragActive = true;
                _draggedItem = item;
                _dragSourceIndex = vm.MenuCards.IndexOf(item);
                _currentDropIndex = _dragSourceIndex;

                _draggedBorder.Opacity = 0.2;

                _adornerLayer = AdornerLayer.GetAdornerLayer(this);
                if (_adornerLayer != null)
                {
                    var cardVisual = _draggedBorder.Child ?? _draggedBorder;
                    _adorner = new CardDragAdorner(this, cardVisual, pos);
                    _adornerLayer.Add(_adorner);
                }

                DragDrop.DoDragDrop(_draggedBorder, new DataObject("MenuCard", item), DragDropEffects.Move);

                CleanupDrag();
            }
        }

        private void Card_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragActive)
            {
                _draggedBorder = null;
                _draggedItem = null;
            }
        }

        private void Card_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (_adorner != null)
            {
                var pos = GetMousePositionRelativeToThis();
                _adorner.UpdatePosition(pos);
            }
            e.UseDefaultCursors = true;
            e.Handled = true;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private Point GetMousePositionRelativeToThis()
        {
            GetCursorPos(out var pt);
            return PointFromScreen(new Point(pt.X, pt.Y));
        }

        private void Grid_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("MenuCard") || _draggedItem == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;

            var panel = FindUniformGrid(CardsGrid);
            if (panel == null || DataContext is not MainViewModel vm)
            {
                e.Handled = true;
                return;
            }

            var pos = e.GetPosition(panel);
            int targetIndex = GetDropIndexFromPosition(pos, panel, vm.MenuCards.Count);

            if (targetIndex >= 0 && targetIndex != _currentDropIndex)
            {
                _currentDropIndex = targetIndex;
                AnimateCardShifts(vm, targetIndex);
            }

            e.Handled = true;
        }

        private int GetDropIndexFromPosition(Point pos, UniformGrid grid, int itemCount)
        {
            if (itemCount == 0) return 0;

            int numCols = (int)Math.Ceiling((double)itemCount / grid.Rows);
            if (numCols == 0) return 0;

            double cellW = grid.ActualWidth / numCols;
            double cellH = grid.ActualHeight / grid.Rows;
            if (cellW <= 0 || cellH <= 0) return 0;

            int col = (int)(pos.X / cellW);
            int row = (int)(pos.Y / cellH);

            col = Math.Max(0, Math.Min(col, numCols - 1));
            row = Math.Max(0, Math.Min(row, grid.Rows - 1));

            int index = row * numCols + col;
            return Math.Max(0, Math.Min(index, itemCount - 1));
        }

        private void AnimateCardShifts(MainViewModel vm, int targetIndex)
        {
            if (_dragSourceIndex < 0) return;

            var panel = FindUniformGrid(CardsGrid);
            if (panel == null) return;

            int count = vm.MenuCards.Count;
            int numCols = (int)Math.Ceiling((double)count / panel.Rows);
            if (numCols == 0) return;

            double cellW = panel.ActualWidth / numCols;
            double cellH = panel.ActualHeight / panel.Rows;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);

            for (int i = 0; i < count; i++)
            {
                var container = CardsGrid.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;

                var border = VisualTreeHelper.GetChildrenCount(container) > 0
                    ? VisualTreeHelper.GetChild(container, 0) as Border
                    : null;
                if (border == null) continue;

                double shiftX = 0, shiftY = 0;

                if (i == _dragSourceIndex)
                {
                    int curCol = i % numCols, curRow = i / numCols;
                    int tgtCol = targetIndex % numCols, tgtRow = targetIndex / numCols;
                    shiftX = (tgtCol - curCol) * cellW;
                    shiftY = (tgtRow - curRow) * cellH;
                }
                else if (_dragSourceIndex < targetIndex && i > _dragSourceIndex && i <= targetIndex)
                {
                    int curCol = i % numCols, curRow = i / numCols;
                    int newCol = (i - 1) % numCols, newRow = (i - 1) / numCols;
                    shiftX = (newCol - curCol) * cellW;
                    shiftY = (newRow - curRow) * cellH;
                }
                else if (_dragSourceIndex > targetIndex && i >= targetIndex && i < _dragSourceIndex)
                {
                    int curCol = i % numCols, curRow = i / numCols;
                    int newCol = (i + 1) % numCols, newRow = (i + 1) / numCols;
                    shiftX = (newCol - curCol) * cellW;
                    shiftY = (newRow - curRow) * cellH;
                }

                var tt = border.RenderTransform as TranslateTransform;
                if (tt == null)
                {
                    tt = new TranslateTransform(0, 0);
                    border.RenderTransform = tt;
                }

                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(shiftX, dur) { EasingFunction = ease });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shiftY, dur) { EasingFunction = ease });
            }
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("MenuCard") || _draggedItem == null)
            {
                e.Handled = true;
                return;
            }

            if (DataContext is MainViewModel vm && _currentDropIndex >= 0)
            {
                int fromIndex = vm.MenuCards.IndexOf(_draggedItem);
                if (fromIndex >= 0 && fromIndex != _currentDropIndex)
                {
                    ResetAllShifts();
                    vm.MoveCard(fromIndex, _currentDropIndex);
                }
            }

            e.Handled = true;
        }

        private void CleanupDrag()
        {
            if (_adorner != null && _adornerLayer != null)
            {
                _adornerLayer.Remove(_adorner);
                _adorner = null;
                _adornerLayer = null;
            }

            if (_draggedBorder != null)
                _draggedBorder.Opacity = 1.0;

            ResetAllShifts();

            _isDragActive = false;
            _draggedItem = null;
            _draggedBorder = null;
            _currentDropIndex = -1;
            _dragSourceIndex = -1;
        }

        private void ResetAllShifts()
        {
            if (DataContext is not MainViewModel vm) return;

            var dur = TimeSpan.FromMilliseconds(150);
            for (int i = 0; i < vm.MenuCards.Count; i++)
            {
                var container = CardsGrid.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                if (container == null) continue;
                var border = VisualTreeHelper.GetChildrenCount(container) > 0
                    ? VisualTreeHelper.GetChild(container, 0) as Border
                    : null;
                if (border?.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur));
                    tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur));
                }
            }
        }

        private static UniformGrid? FindUniformGrid(DependencyObject parent)
        {
            if (parent is UniformGrid ug) return ug;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var result = FindUniformGrid(VisualTreeHelper.GetChild(parent, i));
                if (result != null) return result;
            }
            return null;
        }

        private void CompanyItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is EmployerCompany company)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.EditCompanyCommand.Execute(company);
                    e.Handled = true;
                }
            }
        }

        private void CompanyList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            CompanyScrollViewer.ScrollToVerticalOffset(CompanyScrollViewer.VerticalOffset - e.Delta / 3.0);
        }
    }
}

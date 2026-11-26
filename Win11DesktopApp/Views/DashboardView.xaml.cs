using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class DashboardView : UserControl
    {
        private int _dragSourceSlot = -1;

        public DashboardView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm)
            {
                var ratio = vm.ColumnRatio;
                if (ratio > 0.1 && ratio < 10)
                {
                    LeftCol.Width = new GridLength(1, GridUnitType.Star);
                    RightCol.Width = new GridLength(ratio, GridUnitType.Star);
                }

                var rowRatio = vm.RowRatio;
                if (rowRatio > 0.05 && rowRatio < 20)
                {
                    RightTopRow.Height = new GridLength(rowRatio, GridUnitType.Star);
                    RightBottomRow.Height = new GridLength(1.0, GridUnitType.Star);
                }

                var leftBottom = vm.LeftBottomRatio;
                LeftTopRow.Height = new GridLength(1 - leftBottom, GridUnitType.Star);
                LeftBottomRow.Height = new GridLength(leftBottom, GridUnitType.Star);
            }
        }

        private void LeftRowSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is not DashboardViewModel vm)
                return;
            var top = LeftTopRow.ActualHeight;
            var bottom = LeftBottomRow.ActualHeight;
            if (top + bottom <= 1)
                return;
            vm.LeftBottomRatio = bottom / (top + bottom);
            vm.SaveLayout();
            LeftTopRow.Height = new GridLength(1 - vm.LeftBottomRatio, GridUnitType.Star);
            LeftBottomRow.Height = new GridLength(vm.LeftBottomRatio, GridUnitType.Star);
        }

        private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBlock grip) return;

            var slot = FindParentSlotBorder(grip);
            if (slot == null || slot.Tag is not string tagStr || !int.TryParse(tagStr, out int slotIndex)) return;

            _dragSourceSlot = slotIndex;
            var data = new DataObject("SlotIndex", slotIndex);
            slot.Opacity = 0.5;

            DragDrop.DoDragDrop(slot, data, DragDropEffects.Move);

            slot.Opacity = 1.0;
            _dragSourceSlot = -1;
        }

        // ===== Charts widget: adaptive columns, reordering the chart blocks =====

        private const string ChartBlockFormat = "SalaryChartBlock";

        private void SalaryChartsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer scroll)
                LayoutChartBlocks(scroll);
        }

        private void SalaryChartsItems_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject items && FindAncestor<ScrollViewer>(items) is { } scroll)
                Dispatcher.BeginInvoke(new Action(() => LayoutChartBlocks(scroll)), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Size at which the four blocks look as designed; a smaller widget shows the same row scaled down.
        private const double ChartRowDesignWidth = 1000;
        private const double ChartRowDesignHeight = 200;

        /// <summary>
        /// Always 4 blocks in one row. When the widget is narrower or lower than the design size,
        /// the whole row is scaled down as is (same look, just smaller); otherwise it fills the widget.
        /// </summary>
        private static void LayoutChartBlocks(ScrollViewer scroll)
        {
            var grid = FindDescendant<System.Windows.Controls.Primitives.UniformGrid>(scroll);
            var items = FindDescendant<ItemsControl>(scroll);
            var width = scroll.ActualWidth - 1;
            var height = scroll.ActualHeight - 1;
            if (grid == null || items == null || width <= 0 || height <= 0)
                return;

            var scale = Math.Min(1.0, Math.Min(width / ChartRowDesignWidth, height / ChartRowDesignHeight));
            scale = Math.Max(scale, 0.3);
            grid.Columns = 4;
            grid.Height = double.NaN;
            grid.VerticalAlignment = VerticalAlignment.Stretch;
            items.Width = width / scale;
            items.Height = height / scale;
            items.LayoutTransform = scale < 0.999 ? new ScaleTransform(scale, scale) : Transform.Identity;
        }

        private void ChartGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement grip || FindAncestor<Border>(grip, border => border.Tag is string) is not { Tag: string key } block)
                return;

            e.Handled = true;
            block.Opacity = 0.5;
            try
            {
                DragDrop.DoDragDrop(block, new DataObject(ChartBlockFormat, key), DragDropEffects.Move);
            }
            finally
            {
                block.Opacity = 1.0;
            }
        }

        private void ChartBlock_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border && e.Data.GetDataPresent(ChartBlockFormat))
            {
                if (!Equals(e.Data.GetData(ChartBlockFormat), border.Tag))
                {
                    border.BorderBrush = FindBrush("AccentBrush") ?? Brushes.DodgerBlue;
                    border.BorderThickness = new Thickness(2);
                }
                e.Handled = true;
            }
        }

        private void ChartBlock_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(ChartBlockFormat))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void ChartBlock_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border && e.Data.GetDataPresent(ChartBlockFormat))
            {
                border.BorderBrush = FindBrush("CardBorderBrush") ?? Brushes.LightGray;
                border.BorderThickness = new Thickness(1);
                e.Handled = true;
            }
        }

        private void ChartBlock_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border border || !e.Data.GetDataPresent(ChartBlockFormat))
                return;

            border.BorderBrush = FindBrush("CardBorderBrush") ?? Brushes.LightGray;
            border.BorderThickness = new Thickness(1);
            if (DataContext is DashboardViewModel vm)
                vm.MoveSalaryChartBlock(e.Data.GetData(ChartBlockFormat) as string, border.Tag as string);
            e.Handled = true;
        }

        private static T? FindAncestor<T>(DependencyObject child, Func<T, bool>? match = null) where T : DependencyObject
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is T typed && (match == null || match(typed)))
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;
                if (FindDescendant<T>(child) is { } found)
                    return found;
            }
            return null;
        }

        private void Slot_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border && e.Data.GetDataPresent("SlotIndex"))
            {
                int source = (int)e.Data.GetData("SlotIndex");
                if (int.TryParse(border.Tag?.ToString(), out int target) && source != target)
                {
                    border.BorderBrush = FindBrush("AccentBrush") ?? Brushes.DodgerBlue;
                    border.BorderThickness = new Thickness(2);
                }
            }
            e.Handled = true;
        }

        private void Slot_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = FindBrush("CardBorderBrush") ?? Brushes.LightGray;
                border.BorderThickness = new Thickness(1);
            }
            e.Handled = true;
        }

        private void Slot_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderBrush = FindBrush("CardBorderBrush") ?? Brushes.LightGray;
                border.BorderThickness = new Thickness(1);

                if (e.Data.GetDataPresent("SlotIndex") &&
                    int.TryParse(border.Tag?.ToString(), out int targetSlot))
                {
                    int sourceSlot = (int)e.Data.GetData("SlotIndex");
                    if (sourceSlot != targetSlot && DataContext is DashboardViewModel vm)
                        vm.SwapSlots(sourceSlot, targetSlot);
                }
            }
            e.Handled = true;
        }

        private void ColumnSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm && LeftCol.ActualWidth > 1 && RightCol.ActualWidth > 1)
            {
                var ratio = RightCol.ActualWidth / LeftCol.ActualWidth;
                if (ratio > 0.1 && ratio < 10)
                {
                    vm.ColumnRatio = ratio;
                    vm.SaveLayout();
                }
            }
        }

        private void RowSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm && RightBottomRow.ActualHeight > 1 && RightTopRow.ActualHeight > 1)
            {
                var ratio = RightTopRow.ActualHeight / RightBottomRow.ActualHeight;
                if (ratio > 0.05 && ratio < 20)
                {
                    vm.RowRatio = ratio;
                    vm.SaveLayout();
                }
            }
        }

        private Border? FindParentSlotBorder(DependencyObject child)
        {
            var current = VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is Border b && b.Tag is string tag &&
                    int.TryParse(tag, out _) && b.AllowDrop)
                    return b;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private Brush? FindBrush(string key)
        {
            return TryFindResource(key) as Brush;
        }
    }
}

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
            }
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

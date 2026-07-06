using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class EmployeesView : UserControl
    {
        private const string EmployeeCardMoreMenuTag = "EmployeeCardMoreMenu";
        private Popup? _openEmployeeCardPopup;
        private bool _suppressNextTileCardOpen;

        public EmployeesView()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += EmployeeView_PreviewMouseLeftButtonDown;
            PreviewKeyDown += EmployeesView_PreviewKeyDown;
        }

        private void EmployeesView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
            {
                EmployeeSearchBox.Focus();
                EmployeeSearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid dg && dg.SelectedItem is EmployeeModels.EmployeeSummary emp
                && DataContext is EmployeesViewModel vm && vm.OpenEmployeeCommand.CanExecute(emp))
            {
                vm.OpenEmployeeCommand.Execute(emp);
            }
        }

        private void TilesItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is EmployeesViewModel vm && e.NewSize.Width > 0)
                vm.TilesAvailableWidth = e.NewSize.Width;
        }

        private void EmployeesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
                return;

            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void EmployeeSelectionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is EmployeesViewModel vm)
                vm.UpdateSelectedCount();

            e.Handled = true;
        }

        private void EmployeeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_openEmployeeCardPopup?.IsOpen != true)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (IsInsideOpenPopup(source) || IsInsideTaggedElement(source, EmployeeCardMoreMenuTag))
                return;

            CloseEmployeeCardPopup(_openEmployeeCardPopup);
            _suppressNextTileCardOpen = true;
        }

        private void EmployeeTileCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_suppressNextTileCardOpen)
            {
                _suppressNextTileCardOpen = false;
                e.Handled = true;
                return;
            }

            if (sender is not FrameworkElement element)
                return;

            if (IsInsideTaggedElement(e.OriginalSource as DependencyObject, EmployeeCardMoreMenuTag)
                || FindParent<CheckBox>(e.OriginalSource as DependencyObject) != null
                || _openEmployeeCardPopup?.IsOpen == true)
                return;

            if (element.DataContext is EmployeeModels.EmployeeSummary emp
                && DataContext is EmployeesViewModel vm
                && vm.OpenEmployeeCommand.CanExecute(emp))
            {
                vm.OpenEmployeeCommand.Execute(emp);
                e.Handled = true;
            }
        }

        private void EmployeeCardMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                ToggleEmployeeCardPopup(btn);

            e.Handled = true;
        }

        private void EmployeeCardOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            ExecuteEmployeeCardAction(vm => vm.OpenEmployeeFolderCommand);
            e.Handled = true;
        }

        private void EmployeeDocumentButton_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is EmployeeModels.EmployeeSummary employee
                && element.Tag is string documentType
                && DataContext is EmployeesViewModel vm
                && vm.OpenEmployeeDocumentCommand.CanExecute(Tuple.Create(employee, documentType)))
            {
                vm.OpenEmployeeDocumentCommand.Execute(Tuple.Create(employee, documentType));
            }

            e.Handled = true;
        }

        private void EmployeeCardDelete_Click(object sender, RoutedEventArgs e)
        {
            ExecuteEmployeeCardAction(vm => vm.DeleteEmployeeCommand);
            e.Handled = true;
        }

        private void ExecuteEmployeeCardAction(System.Func<EmployeesViewModel, ICommand> commandSelector)
        {
            if (DataContext is not EmployeesViewModel vm)
                return;

            var employee = _openEmployeeCardPopup?.DataContext as EmployeeModels.EmployeeSummary
                ?? (_openEmployeeCardPopup?.PlacementTarget as FrameworkElement)?.DataContext as EmployeeModels.EmployeeSummary;

            if (employee == null)
                return;

            var command = commandSelector(vm);
            if (command.CanExecute(employee))
                command.Execute(employee);

            CloseEmployeeCardPopup(_openEmployeeCardPopup);
        }

        private void ToggleEmployeeCardPopup(Button btn)
        {
            var popup = FindSiblingPopup(btn);
            if (popup == null)
                return;

            if (_openEmployeeCardPopup != null && !ReferenceEquals(_openEmployeeCardPopup, popup))
                _openEmployeeCardPopup.IsOpen = false;

            popup.PlacementTarget = btn;
            popup.DataContext = btn.DataContext;
            popup.IsOpen = !popup.IsOpen;
            _openEmployeeCardPopup = popup.IsOpen ? popup : null;
        }

        private void CloseEmployeeCardPopup(DependencyObject? source)
        {
            var popup = source as Popup ?? (source != null ? FindParent<Popup>(source) : null) ?? _openEmployeeCardPopup;
            if (popup == null)
                return;

            popup.IsOpen = false;
            if (ReferenceEquals(_openEmployeeCardPopup, popup))
                _openEmployeeCardPopup = null;
        }

        private bool IsInsideOpenPopup(DependencyObject? source)
        {
            if (_openEmployeeCardPopup?.Child is not DependencyObject popupRoot || source == null)
                return false;

            while (source != null)
            {
                if (ReferenceEquals(source, popupRoot))
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static bool IsInsideTaggedElement(DependencyObject? source, string tag)
        {
            while (source != null)
            {
                if (source is FrameworkElement fe && Equals(fe.Tag, tag))
                    return true;

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        private static Popup? FindSiblingPopup(Button button)
        {
            if (button.Parent is not Panel panel)
                return null;

            foreach (var child in panel.Children)
            {
                if (child is Popup popup)
                    return popup;
            }

            return null;
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match)
                    return match;

                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }
    }
}

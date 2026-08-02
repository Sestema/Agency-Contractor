using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class EmployeesView : UserControl
    {
        private const string EmployeeCardMoreMenuTag = "EmployeeCardMoreMenu";
        private Popup? _openEmployeeCardPopup;
        private bool _suppressNextTileCardOpen;
        private readonly DispatcherTimer _companyDropdownCloseTimer;

        public EmployeesView()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += EmployeeView_PreviewMouseLeftButtonDown;
            PreviewKeyDown += EmployeesView_PreviewKeyDown;

            // Grace period so the dropdown survives the mouse crossing the small
            // gap between the company pill and the popup card below it.
            _companyDropdownCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _companyDropdownCloseTimer.Tick += (_, _) =>
            {
                _companyDropdownCloseTimer.Stop();
                if (DataContext is EmployeesViewModel vm)
                    vm.IsCompanyDropdownOpen = false;
            };
        }

        private void CompanyPill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _companyDropdownCloseTimer.Stop();
            if (DataContext is EmployeesViewModel vm && vm.ToggleCompanyDropdownCommand.CanExecute(null))
                vm.ToggleCompanyDropdownCommand.Execute(null);

            e.Handled = true;
        }

        private void CompanyDropdown_MouseEnter(object sender, MouseEventArgs e)
        {
            _companyDropdownCloseTimer.Stop();
        }

        private void CompanyDropdown_MouseLeave(object sender, MouseEventArgs e)
        {
            if (DataContext is EmployeesViewModel vm && vm.IsCompanyDropdownOpen)
            {
                _companyDropdownCloseTimer.Stop();
                _companyDropdownCloseTimer.Start();
            }
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

        private void IconsItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is EmployeesViewModel vm && e.NewSize.Width > 0)
                vm.IconsAvailableWidth = e.NewSize.Width;
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

        private void EmployeeCardContextOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            ExecuteEmployeeCardContextAction(sender, vm => vm.OpenEmployeeFolderCommand);
        }

        private void EmployeeCardContextDelete_Click(object sender, RoutedEventArgs e)
        {
            ExecuteEmployeeCardContextAction(sender, vm => vm.DeleteEmployeeCommand);
        }

        private void ExecuteEmployeeCardContextAction(object sender, System.Func<EmployeesViewModel, ICommand> commandSelector)
        {
            if (sender is not MenuItem menuItem
                || menuItem.DataContext is not EmployeeModels.EmployeeSummary employee
                || DataContext is not EmployeesViewModel vm)
                return;

            var command = commandSelector(vm);
            if (command.CanExecute(employee))
                command.Execute(employee);
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

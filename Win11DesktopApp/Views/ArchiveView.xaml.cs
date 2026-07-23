using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ArchiveView : UserControl
    {
        private const string ArchiveCardMoreMenuTag = "EmployeeCardMoreMenu";
        private Popup? _openCardPopup;
        private bool _suppressNextTileCardOpen;

        public ArchiveView()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += ArchiveView_PreviewMouseLeftButtonDown;
        }

        private void TilesItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is ArchiveViewModel vm && e.NewSize.Width > 0)
                vm.TilesAvailableWidth = e.NewSize.Width;
        }

        private void IconsItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is ArchiveViewModel vm && e.NewSize.Width > 0)
                vm.IconsAvailableWidth = e.NewSize.Width;
        }

        private void ArchiveView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_openCardPopup?.IsOpen != true)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (IsInsideOpenPopup(source) || IsInsideTaggedElement(source, ArchiveCardMoreMenuTag))
                return;

            CloseCardPopup(_openCardPopup);
            _suppressNextTileCardOpen = true;
        }

        private void ArchiveTileCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_suppressNextTileCardOpen)
            {
                _suppressNextTileCardOpen = false;
                e.Handled = true;
                return;
            }

            if (sender is not FrameworkElement element)
                return;

            if (IsInsideTaggedElement(e.OriginalSource as DependencyObject, ArchiveCardMoreMenuTag)
                || FindParent<Button>(e.OriginalSource as DependencyObject) != null
                || _openCardPopup?.IsOpen == true)
                return;

            if (element.DataContext is ArchivedEmployeeSummary emp
                && DataContext is ArchiveViewModel vm
                && vm.ViewEmployeeCommand.CanExecute(emp))
            {
                vm.ViewEmployeeCommand.Execute(emp);
                e.Handled = true;
            }
        }

        private void ArchiveCardMoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
                ToggleCardPopup(btn);

            e.Handled = true;
        }

        private void ArchiveCardViewProfile_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCardAction(vm => vm.ViewEmployeeCommand);
            e.Handled = true;
        }

        private void ArchiveCardOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCardAction(vm => vm.OpenEmployeeFolderCommand);
            e.Handled = true;
        }

        private void ExecuteCardAction(System.Func<ArchiveViewModel, ICommand> commandSelector)
        {
            if (DataContext is not ArchiveViewModel vm)
                return;

            var employee = _openCardPopup?.DataContext as ArchivedEmployeeSummary
                ?? (_openCardPopup?.PlacementTarget as FrameworkElement)?.DataContext as ArchivedEmployeeSummary;

            if (employee == null)
                return;

            var command = commandSelector(vm);
            if (command.CanExecute(employee))
                command.Execute(employee);

            CloseCardPopup(_openCardPopup);
        }

        private void ToggleCardPopup(Button btn)
        {
            var popup = FindSiblingPopup(btn);
            if (popup == null)
                return;

            if (_openCardPopup != null && !ReferenceEquals(_openCardPopup, popup))
                _openCardPopup.IsOpen = false;

            popup.PlacementTarget = btn;
            popup.DataContext = btn.DataContext;
            popup.IsOpen = !popup.IsOpen;
            _openCardPopup = popup.IsOpen ? popup : null;
        }

        private void CloseCardPopup(DependencyObject? source)
        {
            var popup = source as Popup ?? (source != null ? FindParent<Popup>(source) : null) ?? _openCardPopup;
            if (popup == null)
                return;

            popup.IsOpen = false;
            if (ReferenceEquals(_openCardPopup, popup))
                _openCardPopup = null;
        }

        private bool IsInsideOpenPopup(DependencyObject? source)
        {
            if (_openCardPopup?.Child is not DependencyObject popupRoot || source == null)
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

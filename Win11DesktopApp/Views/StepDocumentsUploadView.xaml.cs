using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class StepDocumentsUploadView : UserControl
    {
        public StepDocumentsUploadView() => InitializeComponent();

        private void OnDocDrop(object sender, DragEventArgs e, string docType)
        {
            ResetDropVisual(sender);
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 0) return;
            if (DataContext is AddEmployeeWizardViewModel vm)
                vm.UploadDocumentFromPath(files[0], docType);
            e.Handled = true;
        }

        private void OnDocDragEnter(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            if (sender is Border border)
            {
                var brush = Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush;
                if (brush != null) border.BorderBrush = brush;
            }
            e.Handled = true;
        }

        private void OnDocDragLeave(object sender, DragEventArgs e)
        {
            ResetDropVisual(sender);
            e.Handled = true;
        }

        private void ResetDropVisual(object sender)
        {
            if (sender is Border border)
            {
                var brush = Application.Current?.TryFindResource("CardBorderBrush") as System.Windows.Media.Brush;
                if (brush != null) border.BorderBrush = brush;
            }
        }

        private void PassportDrop(object s, DragEventArgs e) => OnDocDrop(s, e, "passport");
        private void PassportPage2Drop(object s, DragEventArgs e) => OnDocDrop(s, e, "passport_page2");
        private void VisaDrop(object s, DragEventArgs e) => OnDocDrop(s, e, "visa");
        private void VisaPage2Drop(object s, DragEventArgs e) => OnDocDrop(s, e, "visa_page2");
        private void InsuranceDrop(object s, DragEventArgs e) => OnDocDrop(s, e, "insurance");
        private void WorkPermitDrop(object s, DragEventArgs e) => OnDocDrop(s, e, "work_permit");

        private void RowClick(object sender, MouseButtonEventArgs e, System.Action<AddEmployeeWizardViewModel> enable)
        {
            if (e.OriginalSource is DependencyObject source && InsideToggleOrButton(source))
                return;
            if (DataContext is AddEmployeeWizardViewModel vm)
                enable(vm);
        }

        private static bool InsideToggleOrButton(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is System.Windows.Controls.Primitives.ButtonBase)
                    return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }

            return false;
        }

        private void PassportPage2Row_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
            => RowClick(s, e, vm => { if (!vm.HasPassportPage2) vm.HasPassportPage2 = true; });

        private void VisaRow_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
            => RowClick(s, e, vm => { if (!vm.HasVisa) vm.HasVisa = true; });

        private void VisaPage2Row_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
            => RowClick(s, e, vm => { if (!vm.HasVisaPage2) vm.HasVisaPage2 = true; });

        private void InsuranceRow_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
            => RowClick(s, e, vm => { if (!vm.HasInsurance) vm.HasInsurance = true; });

        private void WorkPermitRow_MouseLeftButtonUp(object s, MouseButtonEventArgs e)
            => RowClick(s, e, vm => { if (!vm.HasWorkPermit) vm.HasWorkPermit = true; });

        private void InnerScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Win11DesktopApp.Views
{
    public partial class StepInsuranceDataView : UserControl
    {
        public StepInsuranceDataView() => InitializeComponent();

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

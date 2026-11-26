using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class VacanciesView : UserControl
    {
        public VacanciesView()
        {
            InitializeComponent();
        }

        private void Digits_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void ContactButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VacanciesViewModel vm)
                vm.IsContactOpen = true;
        }

        private void ContactCancel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is VacanciesViewModel vm)
                vm.IsContactOpen = false;
        }

        private void ContactOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is VacanciesViewModel vm)
                vm.IsContactOpen = false;
        }
    }
}

using System.Windows;

namespace AdminPanel
{
    public partial class AdminLoginWindow : Window
    {
        public string Password => PasswordInput.Password;

        public AdminLoginWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => PasswordInput.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

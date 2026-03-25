using System.Windows;

namespace Win11DesktopApp.Views
{
    public partial class ConfirmPasswordWindow : Window
    {
        public string EnteredPassword { get; private set; } = string.Empty;
        public bool IsConfirmed { get; private set; }

        public ConfirmPasswordWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => PasswordBox.Focus();
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var password = PasswordBox.Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(password))
            {
                var title = TryL("ConfirmPasswordTitle") ?? "Confirm password";
                var message = TryL("ProfileErrPasswordRequired") ?? "Enter password.";
                MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                PasswordBox.Focus();
                return;
            }

            EnteredPassword = password;
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }

        private static string? TryL(string key)
        {
            try
            {
                return Application.Current.FindResource(key) as string;
            }
            catch
            {
                return null;
            }
        }
    }
}

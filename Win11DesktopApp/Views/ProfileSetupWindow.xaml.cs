using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class ProfileSetupWindow : Window
    {
        private readonly ProfileSetupViewModel _viewModel;

        public bool IsProfileCreated { get; private set; }

        public ProfileSetupWindow(ProfileSetupViewModel viewModel)
        {
            InitializeComponent();

            _viewModel = viewModel;
            _viewModel.RequestClose += OnRequestClose;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            DataContext = _viewModel;
            SyncPasswordBoxes();
        }

        private void OnRequestClose(bool success)
        {
            IsProfileCreated = success;
            DialogResult = success;
            Close();
        }

        private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.Password = ((PasswordBox)sender).Password;
        }

        private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.ConfirmPassword = ((PasswordBox)sender).Password;
        }

        private void LatinNameTextBox_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = sender is not TextBox textBox || !IsLatinNameInputAllowed(textBox, e.Text);
        }

        private void LatinNameTextBox_OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                e.CancelCommand();
                return;
            }

            var pastedText = e.SourceDataObject.GetData(DataFormats.UnicodeText) as string;
            if (string.IsNullOrEmpty(pastedText) || !IsLatinNameInputAllowed(textBox, pastedText))
                e.CancelCommand();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProfileSetupViewModel.Password)
                || e.PropertyName == nameof(ProfileSetupViewModel.ConfirmPassword)
                || e.PropertyName == nameof(ProfileSetupViewModel.ShowPasswords))
            {
                SyncPasswordBoxes();
            }
        }

        private void SyncPasswordBoxes()
        {
            if (PasswordBox.Password != _viewModel.Password)
                PasswordBox.Password = _viewModel.Password ?? string.Empty;

            if (ConfirmPasswordBox.Password != _viewModel.ConfirmPassword)
                ConfirmPasswordBox.Password = _viewModel.ConfirmPassword ?? string.Empty;
        }

        private static bool IsLatinNameInputAllowed(TextBox textBox, string input)
        {
            var currentText = textBox.Text ?? string.Empty;
            var selectionStart = textBox.SelectionStart;
            var selectionLength = textBox.SelectionLength;
            var nextText = currentText.Remove(selectionStart, selectionLength).Insert(selectionStart, input);

            foreach (var ch in nextText)
            {
                if (ch is < 'A' or > 'z' || (ch > 'Z' && ch < 'a'))
                    return false;
            }

            return true;
        }
    }
}

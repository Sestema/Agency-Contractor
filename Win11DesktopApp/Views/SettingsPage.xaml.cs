using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class SettingsPage : UserControl
    {
        private SettingsViewModel? _subscribedViewModel;
        private bool _isSyncingGeminiApiKeyPasswordBox;

        public SettingsPage()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_subscribedViewModel != null)
                _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

            _subscribedViewModel = e.NewValue as SettingsViewModel;
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
                SyncGeminiApiKeyPasswordBox();
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (ViewModel == null)
                return;

            if (e.PropertyName == nameof(SettingsViewModel.ProfileCurrentPassword)
                && string.IsNullOrEmpty(ViewModel.ProfileCurrentPassword))
            {
                ProfileCurrentPasswordBox.Password = string.Empty;
            }

            if (e.PropertyName == nameof(SettingsViewModel.ProfileNewPassword)
                && string.IsNullOrEmpty(ViewModel.ProfileNewPassword))
            {
                ProfileNewPasswordBox.Password = string.Empty;
            }

            if (e.PropertyName == nameof(SettingsViewModel.ProfileConfirmPassword)
                && string.IsNullOrEmpty(ViewModel.ProfileConfirmPassword))
            {
                ProfileConfirmPasswordBox.Password = string.Empty;
            }

            if (e.PropertyName == nameof(SettingsViewModel.GeminiApiKeyDraft)
                || e.PropertyName == nameof(SettingsViewModel.IsEditingGeminiApiKey))
            {
                SyncGeminiApiKeyPasswordBox();
            }
        }

        private void ProfileCurrentPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.ProfileCurrentPassword = ((PasswordBox)sender).Password;
        }

        private void ProfileNewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.ProfileNewPassword = ((PasswordBox)sender).Password;
        }

        private void ProfileConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
                ViewModel.ProfileConfirmPassword = ((PasswordBox)sender).Password;
        }

        private void GeminiApiKeyMaskedBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null)
                return;

            ViewModel.BeginGeminiApiKeyEdit();
            SyncGeminiApiKeyPasswordBox();
            GeminiApiKeyPasswordBox.Focus();
            e.Handled = true;
        }

        private void GeminiApiKeyPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || _isSyncingGeminiApiKeyPasswordBox)
                return;

            ViewModel.GeminiApiKeyDraft = ((PasswordBox)sender).Password;
        }

        private void GeminiApiKeyPasswordBox_OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
                return;

            if (string.IsNullOrWhiteSpace(ViewModel.GeminiApiKeyDraft))
                ViewModel.CancelGeminiApiKeyEdit();
            else
                ViewModel.CommitGeminiApiKeyEdit();
        }

        private void GeminiApiKeyPasswordBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && (e.Key == Key.C || e.Key == Key.X))
            {
                e.Handled = true;
                return;
            }

            if (ViewModel == null)
                return;

            if (e.Key == Key.Escape)
            {
                ViewModel.CancelGeminiApiKeyEdit();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (string.IsNullOrWhiteSpace(ViewModel.GeminiApiKeyDraft))
                    ViewModel.CancelGeminiApiKeyEdit();
                else
                    ViewModel.CommitGeminiApiKeyEdit();

                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
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

        private void SyncGeminiApiKeyPasswordBox()
        {
            _isSyncingGeminiApiKeyPasswordBox = true;
            GeminiApiKeyPasswordBox.Password = ViewModel?.GeminiApiKeyDraft ?? string.Empty;
            _isSyncingGeminiApiKeyPasswordBox = false;
        }
    }
}

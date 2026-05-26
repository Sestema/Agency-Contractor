using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views;

public partial class UnifiedLoginWindow : Window
{
    private readonly UnifiedLoginViewModel _viewModel;

    public UnifiedLoginKind LoginKind { get; private set; } = UnifiedLoginKind.Failed;
    public ClientProfileRecord? AuthenticatedProfile { get; private set; }
    public AppSettingsService.BusinessUserSetting? AuthenticatedMember { get; private set; }

    public UnifiedLoginWindow(UnifiedLoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        SyncPasswordBox();
    }

    private void OnRequestClose(UnifiedLoginAttemptResult result)
    {
        if (!result.Success)
        {
            DialogResult = false;
            Close();
            return;
        }

        LoginKind = result.Kind;
        AuthenticatedProfile = result.OwnerProfile;
        AuthenticatedMember = result.MemberUser;
        DialogResult = true;
        Close();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Password = ((PasswordBox)sender).Password;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnifiedLoginViewModel.Password)
            || e.PropertyName == nameof(UnifiedLoginViewModel.ShowPassword))
        {
            SyncPasswordBox();
        }
    }

    private void SyncPasswordBox()
    {
        if (PasswordBox.Password != _viewModel.Password)
            PasswordBox.Password = _viewModel.Password ?? string.Empty;
    }
}

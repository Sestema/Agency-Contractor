using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views;

public partial class BusinessUserLoginWindow : Window
{
    private readonly BusinessUserLoginWindowViewModel _viewModel;

    public AppSettingsService.BusinessUserSetting? LoggedInUser { get; private set; }

    public BusinessUserLoginWindow(BusinessUserLoginWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        DataContext = _viewModel;
        SyncPasswordBox();
    }

    private void OnRequestClose(bool success, AppSettingsService.BusinessUserSetting? user)
    {
        LoggedInUser = user;
        DialogResult = success;
        Close();
    }

    private void PasswordBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        _viewModel.Password = ((PasswordBox)sender).Password;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BusinessUserLoginWindowViewModel.Password)
            || e.PropertyName == nameof(BusinessUserLoginWindowViewModel.ShowPassword))
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

using System.Windows;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views;

public partial class StartupRoleSelectionWindow : Window
{
    private readonly StartupRoleSelectionViewModel _viewModel;

    public StartupRoleChoice? SelectedRole { get; private set; }

    public StartupRoleSelectionWindow(StartupRoleSelectionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
    }

    private void OnRequestClose(StartupRoleChoice? role)
    {
        SelectedRole = role;
        DialogResult = role.HasValue;
        Close();
    }
}

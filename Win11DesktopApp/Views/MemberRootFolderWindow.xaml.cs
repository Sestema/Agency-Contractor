using System.Windows;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views;

public partial class MemberRootFolderWindow : Window
{
    private readonly MemberRootFolderViewModel _viewModel;

    public string? SelectedRootFolderPath { get; private set; }

    public MemberRootFolderWindow(MemberRootFolderViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.RequestClose += OnRequestClose;
        DataContext = _viewModel;
    }

    private void OnRequestClose(bool success, string? path)
    {
        SelectedRootFolderPath = path;
        DialogResult = success;
        Close();
    }
}

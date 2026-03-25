using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Invoices.Models;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public sealed class InvoicesTrashViewModel : ViewModelBase
{
    private readonly InvoiceStorageService _storageService;
    private readonly Action? _onChanged;
    private ObservableCollection<InvoiceTrashSummary> _items = new();

    public InvoicesTrashViewModel(InvoiceStorageService storageService, Action? onChanged = null)
    {
        _storageService = storageService;
        _onChanged = onChanged;

        RefreshCommand = new RelayCommand(_ => LoadItems());
        RestoreCommand = new RelayCommand(RestoreDocument, static parameter => parameter is InvoiceTrashSummary);
        DeleteForeverCommand = new RelayCommand(DeleteForever, static parameter => parameter is InvoiceTrashSummary);

        LoadItems();
    }

    public ICommand RefreshCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand DeleteForeverCommand { get; }

    public ObservableCollection<InvoiceTrashSummary> Items
    {
        get => _items;
        set => SetProperty(ref _items, value);
    }

    public bool HasItems => Items.Count > 0;
    public int ItemCount => Items.Count;

    public void Refresh()
        => LoadItems();

    private void LoadItems()
    {
        Items = new ObservableCollection<InvoiceTrashSummary>(_storageService.GetTrashSummaries());
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ItemCount));
    }

    private void RestoreDocument(object? parameter)
    {
        if (parameter is not InvoiceTrashSummary item)
            return;

        if (!PolicyService.EnsureWriteAllowed("Повернути документ Faktury з кошика"))
            return;

        var title = Res("InvoicesTitle");
        var message = string.Format(Res("InvoicesTrashRestoreConfirm"), item.Number);
        if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (_storageService.RestoreFromTrash(item.Id))
        {
            ToastService.Instance.Success(string.Format(Res("InvoicesTrashRestoreSuccess"), item.Number));
            LoadItems();
            _onChanged?.Invoke();
        }
        else
        {
            ToastService.Instance.Warning(Res("InvoicesTrashRestoreFailed"));
        }
    }

    private void DeleteForever(object? parameter)
    {
        if (parameter is not InvoiceTrashSummary item)
            return;

        if (!PolicyService.EnsureWriteAllowed("Видалити документ Faktury з кошика назавжди"))
            return;

        var title = Res("InvoicesTitle");
        var message = string.Format(Res("InvoicesTrashDeleteForeverConfirm"), item.Number);
        if (MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (_storageService.DeleteTrashEntryForever(item.Id))
        {
            ToastService.Instance.Success(string.Format(Res("InvoicesTrashDeleteForeverSuccess"), item.Number));
            LoadItems();
            _onChanged?.Invoke();
        }
        else
        {
            ToastService.Instance.Warning(Res("InvoicesTrashDeleteForeverFailed"));
        }
    }
}

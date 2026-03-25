using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Invoices.Views;

public partial class InvoicePdfPreviewWindow : Window
{
    private readonly string _pdfPath;

    public InvoicePdfPreviewWindow(string pdfPath)
    {
        _pdfPath = pdfPath;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_pdfPath))
            {
                ShowOverlay(Res("InvoicesPreviewUnavailable"), Res("MsgOpenFileError", "File not found."));
                return;
            }

            TxtSubtitle.Text = Path.GetFileName(_pdfPath);
            TxtOverlayMessage.Text = Path.GetFileName(_pdfPath);

            await PdfWebView.EnsureCoreWebView2Async();
            PdfWebView.NavigationCompleted += PdfWebView_NavigationCompleted;
            PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            PdfWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PdfWebView.CoreWebView2.Navigate(new Uri(_pdfPath).AbsoluteUri);
        }
        catch (Exception ex)
        {
            ShowOverlay(Res("InvoicesPreviewUnavailable"), string.Format(Res("MsgOpenFileError"), ex.Message));
        }
    }

    private void PdfWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            PreviewMessageOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        ShowOverlay(Res("InvoicesPreviewUnavailable"), e.WebErrorStatus.ToString());
    }

    private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = Path.GetFileName(_pdfPath),
                AddExtension = true,
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true)
                return;

            File.Copy(_pdfPath, dialog.FileName, overwrite: true);
            ToastService.Instance.Success(Res("InvoicesPreviewSavedAs"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(string.Format(Res("MsgOpenFileError"), ex.Message), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        DocumentGenerationService.OpenFile(_pdfPath);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ShowOverlay(string title, string message)
    {
        PreviewMessageOverlay.Visibility = Visibility.Visible;
        TxtOverlayTitle.Text = title;
        TxtOverlayMessage.Text = message;
    }

    private static string Res(string key, string? fallback = null)
        => Application.Current?.TryFindResource(key) as string ?? fallback ?? key;
}

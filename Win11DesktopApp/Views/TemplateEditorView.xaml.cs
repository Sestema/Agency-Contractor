using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TemplateEditorView : UserControl
    {
        private TemplateEditorViewModel? _vm;
        private DocxEditorWebHost? _host;
        private bool _documentReady;
        private bool _initSent;

        public TemplateEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static string Res(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.RequestInsertTag -= InsertTagAtCaret;
                _vm.RequestReloadDocument -= ReloadFromDisk;
                _vm.RequestGetDocxBytes = null;
            }

            _vm = DataContext as TemplateEditorViewModel;
            if (_vm == null)
                return;

            _vm.RequestInsertTag += InsertTagAtCaret;
            _vm.RequestReloadDocument += ReloadFromDisk;
            _vm.RequestGetDocxBytes = GetDocxBytesAsync;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm == null)
                    return;

                _host?.Dispose();
                _host = new DocxEditorWebHost();
                _host.Ready += OnEditorReady;
                _host.DocumentLoaded += OnDocumentLoaded;
                _host.DocumentLoadFailed += OnDocumentLoadFailed;
                _host.DocumentChanged += OnDocumentChanged;
                _host.SaveRequested += OnSaveRequested;
                await _host.InitializeAsync(EditorWebView);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("TemplateEditorView.OnLoaded", ex);
                _vm?.HandleTemplateOpenFailure(ex.Message);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_host != null)
            {
                _host.Ready -= OnEditorReady;
                _host.DocumentLoaded -= OnDocumentLoaded;
                _host.DocumentLoadFailed -= OnDocumentLoadFailed;
                _host.DocumentChanged -= OnDocumentChanged;
                _host.SaveRequested -= OnSaveRequested;
                _host.Dispose();
                _host = null;
            }

            _documentReady = false;
            _initSent = false;
        }

        private void OnEditorReady()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_vm == null || _host == null || _initSent)
                    return;

                _initSent = true;
                _host.SendInit(_vm.EditorLocale, _vm.HighlightTagNames);
                LoadCurrentDocument();
            }));
        }

        private void OnDocumentLoaded()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _documentReady = true;
                _vm?.NotifyEditorLoaded();
            }));
        }

        private void OnDocumentLoadFailed(string message)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _vm?.HandleTemplateOpenFailure(string.IsNullOrWhiteSpace(message)
                    ? Res("EditorDocxLoadFailed")
                    : message);
            }));
        }

        private void OnDocumentChanged()
        {
            Dispatcher.BeginInvoke(new Action(() => _vm?.MarkDirty()));
        }

        private void OnSaveRequested()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_vm?.SaveCommand.CanExecute(null) == true)
                    _vm.SaveCommand.Execute(null);
            }));
        }

        private void LoadCurrentDocument()
        {
            if (_vm == null || _host == null)
                return;

            try
            {
                var bytes = ResolveDocumentBytes();
                if (bytes == null || bytes.Length == 0)
                {
                    LoggingService.LogWarning("TemplateEditorView.LoadCurrentDocument", $"No document bytes for \"{_vm.OriginalTemplatePath}\"");
                    _vm.HandleTemplateOpenFailure();
                    return;
                }

                LoggingService.LogInfo("TemplateEditorView.LoadCurrentDocument", $"Opening \"{_vm.OriginalTemplatePath}\" ({bytes.Length} bytes)");
                _documentReady = false;
                _host.LoadDocument(bytes);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("TemplateEditorView.LoadCurrentDocument", ex);
                _vm.HandleTemplateOpenFailure(ex.Message);
            }
        }

        private byte[]? ResolveDocumentBytes()
        {
            if (_vm == null)
                return null;

            var docxPath = _vm.OriginalTemplatePath;
            if (DocumentGenerationService.CanOpenAsTemplateDocx(docxPath))
                return SafeFileService.ReadAllBytes(docxPath);

            if (!string.IsNullOrWhiteSpace(docxPath) && File.Exists(docxPath))
            {
                try
                {
                    var expanded = RtfToNativeDocxService.TryExpandAltChunkDocx(docxPath);
                    if (expanded is { Length: > 0 })
                    {
                        LoggingService.LogInfo("TemplateEditorView.ResolveDocumentBytes", $"Expanded embedded RTF from \"{docxPath}\" into {expanded.Length} bytes.");
                        return expanded;
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("TemplateEditorView.ResolveDocumentBytes", ex.Message);
                }
            }

            var xamlPath = string.IsNullOrWhiteSpace(_vm.TemplateFolderPath)
                ? string.Empty
                : Path.Combine(_vm.TemplateFolderPath, "content.xamlpackage");
            if (File.Exists(xamlPath) || (!string.IsNullOrWhiteSpace(_vm.RtfFilePath) && File.Exists(_vm.RtfFilePath)))
            {
                var converted = RtfToNativeDocxService.ConvertLegacyEditorFiles(xamlPath, _vm.RtfFilePath);
                try
                {
                    if (!string.IsNullOrWhiteSpace(docxPath) && !File.Exists(docxPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(docxPath) ?? _vm.TemplateFolderPath);
                        SafeFileService.WriteBytesAtomic(docxPath, converted);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("TemplateEditorView.ResolveDocumentBytes", ex.Message);
                }

                return converted;
            }

            if (!string.IsNullOrWhiteSpace(docxPath) && File.Exists(docxPath))
                return SafeFileService.ReadAllBytes(docxPath);

            return null;
        }

        private void ReloadFromDisk()
        {
            if (_host == null || !_host.IsReady)
                return;
            LoadCurrentDocument();
        }

        private void InsertTagAtCaret(string tagText)
        {
            if (!_documentReady || _host == null)
                return;
            _host.InsertText(tagText);
            _vm?.MarkDirty();
        }

        private async System.Threading.Tasks.Task<byte[]?> GetDocxBytesAsync()
        {
            if (_host == null || !_documentReady)
                return null;
            return await _host.SaveAsync();
        }

    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TemplateEditorView : UserControl
    {
        private TemplateEditorViewModel? _vm;
        private bool _isLoaded;

        public TemplateEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old VM
            if (_vm != null)
            {
                _vm.RequestInsertTag -= InsertTagAtCaret;
                _vm.RequestGetRtfContent = null;
            }

            _vm = null;

            // Subscribe to new VM
            if (DataContext is TemplateEditorViewModel vm)
            {
                _vm = vm;
                _vm.RequestInsertTag += InsertTagAtCaret;
                _vm.RequestGetRtfContent = GetRtfContent;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            try
            {
                if (_vm != null && File.Exists(_vm.RtfFilePath))
                {
                    LoadRtfFromFile(_vm.RtfFilePath);
                }
                Editor.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TemplateEditorView.OnLoaded error: {ex.Message}");
            }
        }

        private void LoadRtfFromFile(string path)
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                range.Load(stream, DataFormats.Rtf);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadRtfFromFile error: {ex.Message}");
            }
        }

        private string? GetRtfContent()
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = new MemoryStream();
                range.Save(stream, DataFormats.Rtf);
                stream.Position = 0;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch
            {
                return null;
            }
        }

        private void InsertTagAtCaret(string tagText)
        {
            try
            {
                Editor.Focus();
                if (Editor.Selection != null && !Editor.Selection.IsEmpty)
                {
                    Editor.Selection.Text = tagText;
                }
                else
                {
                    Editor.CaretPosition.InsertTextInRun(tagText);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InsertTagAtCaret error: {ex.Message}");
            }
        }

        private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: skip during initialization
            if (!_isLoaded || Editor == null) return;

            try
            {
                if (FontSizeCombo.SelectedItem is ComboBoxItem item &&
                    double.TryParse(item.Content?.ToString(), out double size))
                {
                    if (Editor.Selection != null && !Editor.Selection.IsEmpty)
                    {
                        Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    }
                    Editor.Focus();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FontSizeCombo error: {ex.Message}");
            }
        }
    }
}

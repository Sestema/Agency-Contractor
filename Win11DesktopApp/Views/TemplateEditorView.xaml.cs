using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TemplateEditorView : UserControl
    {
        private TemplateEditorViewModel? _vm;
        private bool _isLoaded;
        private AITemplateOverlayWindow? _aiOverlay;
        private const double A4HeightPx = 1123.0;

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
                _vm.RequestReplaceTagsInDocument -= ReplaceTagsInDocument!;
                _vm.RequestGetRtfContent = null;
                _vm.RequestGetPlainText = null;
            }

            _vm = null;

            // Subscribe to new VM
            if (DataContext is TemplateEditorViewModel vm)
            {
                _vm = vm;
                _vm.RequestInsertTag += InsertTagAtCaret;
                _vm.RequestReplaceTagsInDocument += ReplaceTagsInDocument!;
                _vm.RequestGetRtfContent = GetRtfContent;
                _vm.RequestGetPlainText = GetPlainTextContent;
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
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorView.GetRtfContent", ex.Message);
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

        private string? GetPlainTextContent()
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                return range.Text;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorView.GetPlainTextContent", ex.Message);
                return null;
            }
        }

        private void ReplaceTagsInDocument(List<(string ContextBefore, string ReplaceWhat, string Tag)> replacements)
        {
            // We use a "tracking" plain text to compute which occurrence to replace.
            // After each replacement the tracking string is updated so the next search is accurate.
            var trackingText = GetPlainTextContent() ?? string.Empty;

            // Normalize dashes in tracking text to match what AI likely returned
            trackingText = NormalizeDashes(trackingText);

            foreach (var (contextBefore, replaceWhat, tag) in replacements)
            {
                var normalizedContext = NormalizeDashes(contextBefore);
                var normalizedReplace = NormalizeDashes(replaceWhat);

                if (string.IsNullOrEmpty(normalizedReplace)) continue;

                // Find context_before in the tracking text
                int contextIdx = string.IsNullOrEmpty(normalizedContext)
                    ? 0
                    : trackingText.IndexOf(normalizedContext, StringComparison.OrdinalIgnoreCase);

                if (contextIdx < 0) contextIdx = 0; // fallback: search from start

                // Find replace_what after context_before
                int searchFrom = contextIdx + (normalizedContext?.Length ?? 0);
                int targetIdx = trackingText.IndexOf(normalizedReplace, searchFrom, StringComparison.Ordinal);
                if (targetIdx < 0) continue;

                // Count how many times normalizedReplace appears from beginning up to targetIdx (inclusive)
                int occurrence = 0;
                int pos = 0;
                while (pos <= targetIdx)
                {
                    int found = trackingText.IndexOf(normalizedReplace, pos, StringComparison.Ordinal);
                    if (found < 0 || found > targetIdx) break;
                    occurrence++;
                    pos = found + Math.Max(1, normalizedReplace.Length);
                }

                if (occurrence == 0) continue;

                // Replace the N-th occurrence in the actual RichTextBox (searching with original chars)
                ReplaceNthOccurrence(replaceWhat, tag, occurrence);

                // Update tracking text: replace this occurrence with the tag text (so future context searches work)
                int cnt = 0;
                int tpos = 0;
                while (tpos < trackingText.Length)
                {
                    int found = trackingText.IndexOf(normalizedReplace, tpos, StringComparison.Ordinal);
                    if (found < 0) break;
                    cnt++;
                    if (cnt == occurrence)
                    {
                        trackingText = trackingText.Substring(0, found) + tag
                                     + trackingText.Substring(found + normalizedReplace.Length);
                        break;
                    }
                    tpos = found + Math.Max(1, normalizedReplace.Length);
                }
            }
        }

        // Normalize em-dash, en-dash and figure dash to a common em-dash for reliable searching
        private static string NormalizeDashes(string text)
        {
            return text
                .Replace('\u2013', '\u2014') // en-dash → em-dash
                .Replace('\u2012', '\u2014') // figure dash → em-dash
                .Replace('\u2015', '\u2014'); // horizontal bar → em-dash
        }

        private void ReplaceNthOccurrence(string original, string tagText, int targetOccurrence)
        {
            if (string.IsNullOrEmpty(original)) return;

            // Try exact match first; if nothing found, try normalized
            bool TryReplace(string searchText)
            {
                var runs = new List<Run>();
                CollectAllRuns(Editor.Document, runs);

                int count = 0;
                foreach (var run in runs)
                {
                    int startIdx = 0;
                    while (true)
                    {
                        int idx = run.Text.IndexOf(searchText, startIdx, StringComparison.Ordinal);
                        if (idx < 0) break;

                        count++;
                        if (count == targetOccurrence)
                        {
                            string before = run.Text.Substring(0, idx);
                            string after = run.Text.Substring(idx + searchText.Length);

                            var tagRun = new Run(tagText)
                            {
                                FontFamily = run.FontFamily,
                                FontSize = run.FontSize,
                                FontWeight = run.FontWeight,
                                FontStyle = run.FontStyle,
                                Foreground = run.Foreground
                            };

                            if (run.Parent is Paragraph para)
                            {
                                run.Text = before;
                                para.Inlines.InsertAfter(run, tagRun);
                                if (!string.IsNullOrEmpty(after))
                                    para.Inlines.InsertAfter(tagRun, CreateMatchingRun(after, run));
                            }
                            else if (run.Parent is Span span)
                            {
                                run.Text = before;
                                span.Inlines.InsertAfter(run, tagRun);
                                if (!string.IsNullOrEmpty(after))
                                    span.Inlines.InsertAfter(tagRun, CreateMatchingRun(after, run));
                            }
                            return true;
                        }

                        startIdx = idx + Math.Max(1, searchText.Length);
                    }
                }
                return false;
            }

            // First try exact, then try normalized variant
            if (!TryReplace(original))
                TryReplace(NormalizeDashes(original));
        }

        private static Run CreateMatchingRun(string text, Run source) => new Run(text)
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
            Foreground = source.Foreground
        };

        private static void CollectAllRuns(FlowDocument doc, List<Run> runs)
        {
            foreach (var block in doc.Blocks)
                CollectBlockRuns(block, runs);
        }

        private static void CollectBlockRuns(Block block, List<Run> runs)
        {
            if (block is Paragraph para)
            {
                foreach (var inline in para.Inlines)
                    CollectInlineRuns(inline, runs);
            }
            else if (block is Section section)
            {
                foreach (var b in section.Blocks)
                    CollectBlockRuns(b, runs);
            }
            else if (block is System.Windows.Documents.List list)
            {
                foreach (ListItem item in list.ListItems)
                    foreach (var b in item.Blocks)
                        CollectBlockRuns(b, runs);
            }
        }

        private static void CollectInlineRuns(Inline inline, List<Run> runs)
        {
            if (inline is Run run)
                runs.Add(run);
            else if (inline is Span span)
                foreach (var i in span.Inlines)
                    CollectInlineRuns(i, runs);
        }

        private void AIOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_aiOverlay == null || !_aiOverlay.IsLoaded)
            {
                _aiOverlay = new AITemplateOverlayWindow();
                _aiOverlay.Owner = Window.GetWindow(this);
                _aiOverlay.SetContentProviders(GetPlainTextContent, GetTagCatalogText);
            }

            if (_aiOverlay.IsVisible)
                _aiOverlay.Hide();
            else
                _aiOverlay.Show();
        }

        private string? GetTagCatalogText()
        {
            if (_vm == null) return null;
            var sb = new StringBuilder();
            foreach (var group in _vm.TagGroups)
            {
                sb.AppendLine($"[{group.GroupName}]");
                foreach (var tag in group.Tags)
                    sb.AppendLine($"  ${{{tag.Tag}}} — {tag.Description}");
            }
            return sb.ToString();
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

        private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageBreakLines();
        }

        private void UpdatePageBreakLines()
        {
            if (PageBreakCanvas == null || Editor == null) return;

            PageBreakCanvas.Children.Clear();

            double totalHeight = Editor.ActualHeight;
            if (totalHeight <= A4HeightPx) return;

            int pageBreaks = (int)(totalHeight / A4HeightPx);
            double width = Editor.ActualWidth;

            for (int i = 1; i <= pageBreaks; i++)
            {
                double y = i * A4HeightPx;

                var line = new Line
                {
                    X1 = 8,
                    X2 = width - 8,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 6, 3 },
                    SnapsToDevicePixels = true
                };
                PageBreakCanvas.Children.Add(line);

                var label = new Border
                {
                    Background = Brushes.White,
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(8, 1, 8, 1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = $"── {i + 1} ──",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                        FontFamily = new FontFamily("Segoe UI"),
                    }
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, (width - label.DesiredSize.Width) / 2);
                Canvas.SetTop(label, y - label.DesiredSize.Height / 2);
                PageBreakCanvas.Children.Add(label);
            }
        }
    }
}

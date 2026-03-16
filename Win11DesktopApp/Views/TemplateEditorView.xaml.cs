using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TemplateEditorView : UserControl
    {
        private TemplateEditorViewModel? _vm;
        private bool _isLoaded;
        private bool _suppressEditorEvents;
        private AITemplateOverlayWindow? _aiOverlay;
        private const double DefaultPageHeightPx = 1123.0;
        private const double DefaultParagraphSpacingPx = 8.0;

        public TemplateEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private static string Res(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        private static string ResF(string key, params object[] args)
        {
            var format = Res(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old VM
            if (_vm != null)
            {
                _vm.RequestInsertTag -= InsertTagAtCaret;
                _vm.RequestApplyStarterTemplate -= ApplyStarterTemplate;
                _vm.RequestReplaceTagsInDocument -= ReplaceTagsInDocument!;
                _vm.RequestGetRtfContent = null;
                _vm.RequestGetXamlPackageContent = null;
                _vm.RequestGetPlainText = null;
                _vm.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _vm = null;

            // Subscribe to new VM
            if (DataContext is TemplateEditorViewModel vm)
            {
                _vm = vm;
                _vm.RequestInsertTag += InsertTagAtCaret;
                _vm.RequestApplyStarterTemplate += ApplyStarterTemplate;
                _vm.RequestReplaceTagsInDocument += ReplaceTagsInDocument!;
                _vm.RequestGetRtfContent = GetRtfContent;
                _vm.RequestGetXamlPackageContent = GetXamlPackageContent;
                _vm.RequestGetPlainText = GetPlainTextContent;
                _vm.PropertyChanged += ViewModel_PropertyChanged;
                LoadPreviewRtf(vm.SelectedStarterTemplateRtf);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _suppressEditorEvents = true;

                if (_vm == null)
                {
                    return;
                }

                if (File.Exists(_vm.NativeDocumentPath))
                {
                    LoadXamlPackageFromFile(_vm.NativeDocumentPath);
                }
                else if (File.Exists(_vm.RtfFilePath))
                {
                    LoadRtfFromFile(_vm.RtfFilePath);
                }
                else if (_vm != null && !string.IsNullOrWhiteSpace(_vm.OriginalTemplatePath) && File.Exists(_vm.OriginalTemplatePath))
                {
                    // Allow opening the editor for templates that exist but have not been resaved
                    // into content.xamlpackage/content.rtf yet.
                }
                else
                {
                    _vm?.HandleTemplateOpenFailure();
                    return;
                }

                ApplyCurrentPageLayout();
                UpdatePageBreakLines();
                UpdateRuler();
                SyncParagraphFormattingControls();
                UpdateToolbarButtonStates();
                LoadPreviewRtf(_vm?.SelectedStarterTemplateRtf);
                Editor.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TemplateEditorView.OnLoaded error: {ex.Message}");
                _vm?.HandleTemplateOpenFailure(ex.Message);
            }
            finally
            {
                _suppressEditorEvents = false;
                _isLoaded = true;
                _vm?.NotifyEditorLoaded();
            }
        }

        private void LoadRtfFromFile(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                LoadRtfIntoRichTextBox(Editor, stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadRtfFromFile error: {ex.Message}");
                _vm?.HandleTemplateOpenFailure(ex.Message);
            }
        }

        private void LoadXamlPackageFromFile(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                LoadXamlPackageIntoRichTextBox(Editor, stream);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadXamlPackageFromFile error: {ex.Message}");
                _vm?.HandleTemplateOpenFailure(ex.Message);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TemplateEditorViewModel.SelectedStarterTemplateRtf))
                LoadPreviewRtf(_vm?.SelectedStarterTemplateRtf);

            if (e.PropertyName is nameof(TemplateEditorViewModel.PagePreviewWidth)
                or nameof(TemplateEditorViewModel.PagePreviewHeight)
                or nameof(TemplateEditorViewModel.PagePadding)
                or nameof(TemplateEditorViewModel.SelectedPageSize)
                or nameof(TemplateEditorViewModel.SelectedPageOrientation)
                or nameof(TemplateEditorViewModel.SelectedPageMargin))
            {
                ApplyCurrentPageLayout();
                UpdatePageBreakLines();
                UpdateRuler();
                UpdateLayoutMenuState();
            }
        }

        private void LoadPreviewRtf(string? rtfContent)
        {
            if (SamplesPreviewBox == null)
                return;

            if (string.IsNullOrWhiteSpace(rtfContent))
            {
                SamplesPreviewBox.Document = new FlowDocument(new Paragraph(new Run()));
                return;
            }

            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtfContent));
                LoadRtfIntoRichTextBox(SamplesPreviewBox, stream);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorView.LoadPreviewRtf", ex.Message);
                SamplesPreviewBox.Document = new FlowDocument(new Paragraph(new Run()));
            }
        }

        private void ApplyStarterTemplate(string rtfContent)
        {
            if (string.IsNullOrWhiteSpace(rtfContent))
                return;

            _suppressEditorEvents = true;
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtfContent));
                LoadRtfIntoRichTextBox(Editor, stream);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorView.ApplyStarterTemplate", ex.Message);
                return;
            }
            finally
            {
                _suppressEditorEvents = false;
            }

            _vm?.MarkDirty();
            ApplyCurrentPageLayout();
            UpdatePageBreakLines();
            UpdateRuler();
            SyncParagraphFormattingControls();
            Editor.Focus();
        }

        private void ApplyCurrentPageLayout()
        {
            if (Editor?.Document == null || _vm == null)
                return;

            var document = Editor.Document;
            document.PageWidth = _vm.PagePreviewWidth;
            document.MinPageWidth = _vm.PagePreviewWidth;
            document.MaxPageWidth = _vm.PagePreviewWidth;
            document.PageHeight = _vm.PagePreviewHeight;
            document.MinPageHeight = _vm.PagePreviewHeight;
            document.MaxPageHeight = _vm.PagePreviewHeight;
            document.PagePadding = _vm.PagePadding;
            document.ColumnWidth = double.PositiveInfinity;
        }

        private static void LoadRtfIntoRichTextBox(RichTextBox richTextBox, Stream stream)
        {
            richTextBox.Document = new FlowDocument();
            var range = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
            range.Load(stream, DataFormats.Rtf);
        }

        private static void LoadXamlPackageIntoRichTextBox(RichTextBox richTextBox, Stream stream)
        {
            richTextBox.Document = new FlowDocument();
            var range = new TextRange(richTextBox.Document.ContentStart, richTextBox.Document.ContentEnd);
            range.Load(stream, DataFormats.XamlPackage);
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

        private byte[]? GetXamlPackageContent()
        {
            try
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var stream = new MemoryStream();
                range.Save(stream, DataFormats.XamlPackage);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateEditorView.GetXamlPackageContent", ex.Message);
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

                _vm?.MarkDirty();
                UpdatePageBreakLines();
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

            _vm?.MarkDirty();
            UpdatePageBreakLines();
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
            if (!_isLoaded || _suppressEditorEvents || Editor == null) return;

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

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            _vm?.MarkDirty();
            UpdatePageBreakLines();
            UpdateToolbarButtonStates();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            SyncParagraphFormattingControls();
            UpdateToolbarButtonStates();
        }

        private void SpacingMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateSpacingMenuState();

            if (SpacingMenuButton.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = SpacingMenuButton;
            menu.IsOpen = true;
        }

        private void PageOrientationMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateLayoutMenuState();

            if (PageOrientationMenuButton.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = PageOrientationMenuButton;
            menu.IsOpen = true;
        }

        private void PageSizeMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateLayoutMenuState();

            if (PageSizeMenuButton.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = PageSizeMenuButton;
            menu.IsOpen = true;
        }

        private void PageMarginsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateLayoutMenuState();

            if (PageMarginsMenuButton.ContextMenu is not ContextMenu menu)
                return;

            menu.PlacementTarget = PageMarginsMenuButton;
            menu.IsOpen = true;
        }

        private void PageOrientationMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not MenuItem item)
                return;

            var key = item.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            _vm.SelectedPageOrientation = _vm.AvailablePageOrientations.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? _vm.SelectedPageOrientation;
            UpdateLayoutMenuState();
        }

        private void PageSizeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not MenuItem item)
                return;

            var key = item.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            _vm.SelectedPageSize = _vm.AvailablePageSizes.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? _vm.SelectedPageSize;
            UpdateLayoutMenuState();
        }

        private void PageMarginsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (_vm == null || sender is not MenuItem item)
                return;

            var key = item.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                return;

            _vm.SelectedPageMargin = _vm.AvailablePageMargins.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase))
                ?? _vm.SelectedPageMargin;
            UpdateLayoutMenuState();
        }

        private void LineSpacingMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || !TryGetMenuItemValue(item, out var multiplier))
                return;

            ApplyLineSpacing(multiplier);
        }

        private void QuickSpacingBeforeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var hasSpacing = GetSelectedParagraphs().Any(paragraph => HasSpacing(paragraph.Margin.Top));
            SetParagraphSpacingBefore(hasSpacing ? 0 : DefaultParagraphSpacingPx);
        }

        private void QuickSpacingAfterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var hasSpacing = GetSelectedParagraphs().Any(paragraph => HasSpacing(paragraph.Margin.Bottom));
            SetParagraphSpacingAfter(hasSpacing ? 0 : DefaultParagraphSpacingPx);
        }

        private void SpacingBeforeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || !TryGetMenuItemValue(item, out var spacing))
                return;

            SetParagraphSpacingBefore(spacing);
        }

        private void SpacingAfterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || !TryGetMenuItemValue(item, out var spacing))
                return;

            SetParagraphSpacingAfter(spacing);
        }

        private void FirstLineIndentMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || !TryGetMenuItemValue(item, out var indent))
                return;

            SetFirstLineIndent(indent);
        }

        private void LeftIndentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            if (!TryGetSelectedComboValue(LeftIndentCombo, out var indent))
                return;

            ApplyParagraphFormatting(paragraph =>
            {
                paragraph.TextIndent = Math.Min(paragraph.TextIndent, indent);
                paragraph.Margin = new Thickness(indent, paragraph.Margin.Top, paragraph.Margin.Right, paragraph.Margin.Bottom);
            });
        }

        private void RightIndentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            if (!TryGetSelectedComboValue(RightIndentCombo, out var indent))
                return;

            ApplyParagraphFormatting(paragraph =>
            {
                paragraph.Margin = new Thickness(paragraph.Margin.Left, paragraph.Margin.Top, indent, paragraph.Margin.Bottom);
            });
        }

        private void BulletListButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleList(TextMarkerStyle.Disc);
        }

        private void NumberedListButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleList(TextMarkerStyle.Decimal);
        }

        private void Editor_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePageBreakLines();
            UpdateRuler();
        }

        private void ApplyParagraphFormatting(Action<Paragraph> apply)
        {
            var paragraphs = GetSelectedParagraphs().ToList();
            if (paragraphs.Count == 0)
                return;

            _suppressEditorEvents = true;

            try
            {
                foreach (var paragraph in paragraphs)
                    apply(paragraph);
            }
            finally
            {
                _suppressEditorEvents = false;
            }

            _vm?.MarkDirty();
            UpdatePageBreakLines();
            UpdateRuler();
            SyncParagraphFormattingControls();
            Editor.Focus();
        }

        private IEnumerable<Paragraph> GetSelectedParagraphs()
        {
            var paragraphs = new HashSet<Paragraph>();
            var start = Editor.Selection?.Start;
            var end = Editor.Selection?.End;

            if (start == null || end == null)
                return paragraphs;

            if (start.Paragraph != null)
                paragraphs.Add(start.Paragraph);

            var pointer = start;
            while (pointer != null && pointer.CompareTo(end) <= 0)
            {
                if (pointer.Paragraph != null)
                    paragraphs.Add(pointer.Paragraph);

                var next = pointer.GetNextContextPosition(LogicalDirection.Forward);
                if (next == null || next.CompareTo(pointer) == 0)
                    break;

                pointer = next;
            }

            if (end.Paragraph != null)
                paragraphs.Add(end.Paragraph);

            return paragraphs;
        }

        private static bool TryGetSelectedComboValue(ComboBox comboBox, out double value)
        {
            value = 0;

            if (comboBox.SelectedItem is ComboBoxItem item)
                return double.TryParse(item.Tag?.ToString() ?? item.Content?.ToString(), out value);

            return false;
        }

        private static bool TryGetMenuItemValue(MenuItem item, out double value)
        {
            value = 0;
            return double.TryParse(item.Tag?.ToString() ?? item.Header?.ToString(), out value);
        }

        private static bool HasSpacing(double value)
        {
            return Math.Abs(value) > 0.1;
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.05;
        }

        private static void SetCheckedMenuItem(MenuItem? item, bool isChecked)
        {
            if (item != null)
                item.IsChecked = isChecked;
        }

        private void SyncParagraphFormattingControls()
        {
            var paragraph = Editor.Selection?.Start?.Paragraph;
            if (paragraph == null)
                return;

            _suppressEditorEvents = true;

            try
            {
                SyncComboSelection(LeftIndentCombo, paragraph.Margin.Left);
                SyncComboSelection(RightIndentCombo, paragraph.Margin.Right);
                UpdateSpacingMenuState();
            }
            finally
            {
                _suppressEditorEvents = false;
            }
        }

        private void UpdateToolbarButtonStates()
        {
            if (Editor == null)
                return;

            var selection = Editor.Selection;
            var paragraph = selection?.Start?.Paragraph;

            var isBold = IsSelectionPropertyActive(TextElement.FontWeightProperty, FontWeights.Bold);
            var isItalic = IsSelectionPropertyActive(TextElement.FontStyleProperty, FontStyles.Italic);
            var isUnderline = IsUnderlineActive();

            ApplyToolbarButtonState(BoldButton, isBold);
            ApplyToolbarButtonState(ItalicButton, isItalic);
            ApplyToolbarButtonState(UnderlineButton, isUnderline);

            var alignment = paragraph?.TextAlignment ?? TextAlignment.Left;
            ApplyToolbarButtonState(AlignLeftButton, alignment == TextAlignment.Left);
            ApplyToolbarButtonState(AlignCenterButton, alignment == TextAlignment.Center);
            ApplyToolbarButtonState(AlignRightButton, alignment == TextAlignment.Right);

            var list = GetCurrentParentList(paragraph);
            ApplyToolbarButtonState(BulletListButton, list?.MarkerStyle == TextMarkerStyle.Disc);
            ApplyToolbarButtonState(NumberedListButton, list?.MarkerStyle == TextMarkerStyle.Decimal);
        }

        private bool IsSelectionPropertyActive(DependencyProperty property, object expectedValue)
        {
            var value = Editor.Selection?.GetPropertyValue(property);
            if (value == null || value == DependencyProperty.UnsetValue)
                return false;

            return Equals(value, expectedValue);
        }

        private bool IsUnderlineActive()
        {
            var value = Editor.Selection?.GetPropertyValue(Inline.TextDecorationsProperty);
            if (value == null || value == DependencyProperty.UnsetValue)
                return false;

            if (value is TextDecorationCollection decorations)
                return decorations.Count > 0;

            return false;
        }

        private static System.Windows.Documents.List? GetCurrentParentList(Paragraph? paragraph)
        {
            if (paragraph?.Parent is ListItem listItem && listItem.Parent is System.Windows.Documents.List list)
                return list;

            return null;
        }

        private void ApplyToolbarButtonState(Button? button, bool isActive)
        {
            if (button == null)
                return;

            var accentBrush = Application.Current?.TryFindResource("AccentBrush") as Brush;
            var borderBrush = Application.Current?.TryFindResource("CardBorderBrush") as Brush;
            var backgroundBrush = Application.Current?.TryFindResource("CardBackgroundBrush") as Brush;
            var foregroundBrush = Application.Current?.TryFindResource("ForegroundBrush") as Brush;

            if (isActive)
            {
                button.BorderBrush = accentBrush ?? button.BorderBrush;
                button.Background = CreateAccentBackground(accentBrush);
            }
            else
            {
                button.BorderBrush = borderBrush ?? button.BorderBrush;
                button.Background = backgroundBrush ?? button.Background;
            }

            ApplyButtonContentForeground(button, isActive && accentBrush != null ? accentBrush : foregroundBrush);
        }

        private static Brush CreateAccentBackground(Brush? accentBrush)
        {
            if (accentBrush is SolidColorBrush solid)
            {
                var color = solid.Color;
                return new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
            }

            return new SolidColorBrush(Color.FromArgb(20, 0, 120, 215));
        }

        private static void ApplyButtonContentForeground(DependencyObject parent, Brush? brush)
        {
            if (brush == null)
                return;

            if (parent is TextBlock textBlock)
            {
                textBlock.Foreground = brush;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                ApplyButtonContentForeground(child, brush);
            }
        }

        private void ApplyLineSpacing(double multiplier)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            ApplyParagraphFormatting(paragraph =>
            {
                var fontSize = paragraph.FontSize > 0 ? paragraph.FontSize : Editor.FontSize;
                paragraph.LineHeight = Math.Round(fontSize * multiplier, 1);
            });
        }

        private void SetParagraphSpacingBefore(double spacing)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            ApplyParagraphFormatting(paragraph =>
            {
                var margin = paragraph.Margin;
                paragraph.Margin = new Thickness(margin.Left, spacing, margin.Right, margin.Bottom);
            });
        }

        private void SetParagraphSpacingAfter(double spacing)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            ApplyParagraphFormatting(paragraph =>
            {
                var margin = paragraph.Margin;
                paragraph.Margin = new Thickness(margin.Left, margin.Top, margin.Right, spacing);
            });
        }

        private void SetFirstLineIndent(double indent)
        {
            if (!_isLoaded || _suppressEditorEvents)
                return;

            ApplyParagraphFormatting(paragraph => paragraph.TextIndent = indent);
        }

        private void UpdateSpacingMenuState()
        {
            var paragraph = Editor.Selection?.Start?.Paragraph;
            if (paragraph == null)
                return;

            var selectedParagraphs = GetSelectedParagraphs().ToList();
            if (selectedParagraphs.Count == 0)
                selectedParagraphs.Add(paragraph);

            var hasBeforeSpacing = selectedParagraphs.Any(item => HasSpacing(item.Margin.Top));
            var hasAfterSpacing = selectedParagraphs.Any(item => HasSpacing(item.Margin.Bottom));

            if (QuickSpacingBeforeMenuItem != null)
                QuickSpacingBeforeMenuItem.Header = Res(hasBeforeSpacing ? "EditorRemoveSpaceBeforeParagraph" : "EditorAddSpaceBeforeParagraph");

            if (QuickSpacingAfterMenuItem != null)
                QuickSpacingAfterMenuItem.Header = Res(hasAfterSpacing ? "EditorRemoveSpaceAfterParagraph" : "EditorAddSpaceAfterParagraph");

            var lineSpacing = GetLineSpacingValue(paragraph);
            SetCheckedMenuItem(LineSpacing100MenuItem, AreClose(lineSpacing, 1.0));
            SetCheckedMenuItem(LineSpacing115MenuItem, AreClose(lineSpacing, 1.15));
            SetCheckedMenuItem(LineSpacing150MenuItem, AreClose(lineSpacing, 1.5));
            SetCheckedMenuItem(LineSpacing200MenuItem, AreClose(lineSpacing, 2.0));
            SetCheckedMenuItem(LineSpacing250MenuItem, AreClose(lineSpacing, 2.5));
            SetCheckedMenuItem(LineSpacing300MenuItem, AreClose(lineSpacing, 3.0));
        }

        private void UpdateLayoutMenuState()
        {
            if (_vm == null)
                return;

            SetCheckedMenuItem(A4PageSizeMenuItem, string.Equals(_vm.SelectedPageSize?.Key, "a4", StringComparison.OrdinalIgnoreCase));
            SetCheckedMenuItem(LetterPageSizeMenuItem, string.Equals(_vm.SelectedPageSize?.Key, "letter", StringComparison.OrdinalIgnoreCase));

            SetCheckedMenuItem(PortraitOrientationMenuItem, string.Equals(_vm.SelectedPageOrientation?.Key, "portrait", StringComparison.OrdinalIgnoreCase));
            SetCheckedMenuItem(LandscapeOrientationMenuItem, string.Equals(_vm.SelectedPageOrientation?.Key, "landscape", StringComparison.OrdinalIgnoreCase));

            SetCheckedMenuItem(NormalMarginsMenuItem, string.Equals(_vm.SelectedPageMargin?.Key, "normal", StringComparison.OrdinalIgnoreCase));
            SetCheckedMenuItem(NarrowMarginsMenuItem, string.Equals(_vm.SelectedPageMargin?.Key, "narrow", StringComparison.OrdinalIgnoreCase));
            SetCheckedMenuItem(WideMarginsMenuItem, string.Equals(_vm.SelectedPageMargin?.Key, "wide", StringComparison.OrdinalIgnoreCase));
        }

        private void ToggleList(TextMarkerStyle style)
        {
            var paragraphs = GetSelectedParagraphs().ToList();
            if (paragraphs.Count == 0)
                return;

            _suppressEditorEvents = true;
            try
            {
                foreach (var paragraph in paragraphs)
                {
                    if (paragraph.Parent is ListItem existingItem && existingItem.Parent is System.Windows.Documents.List existingList)
                    {
                        if (existingList.MarkerStyle == style)
                        {
                            ConvertListItemToParagraph(existingItem, existingList);
                        }
                        else
                        {
                            existingList.MarkerStyle = style;
                        }

                        continue;
                    }

                    ConvertParagraphToListItem(paragraph, style);
                }
            }
            finally
            {
                _suppressEditorEvents = false;
            }

            _vm?.MarkDirty();
            UpdatePageBreakLines();
            UpdateRuler();
            Editor.Focus();
        }

        private static void ConvertParagraphToListItem(Paragraph paragraph, TextMarkerStyle style)
        {
            var parentCollection = paragraph.Parent switch
            {
                FlowDocument document => document.Blocks,
                Section section => section.Blocks,
                ListItem listItem => listItem.Blocks,
                _ => null
            };

            if (parentCollection == null)
                return;

            var list = new System.Windows.Documents.List
            {
                MarkerStyle = style,
                Margin = paragraph.Margin
            };

            var newParagraph = CloneParagraph(paragraph);
            var item = new ListItem(newParagraph);
            list.ListItems.Add(item);

            parentCollection.InsertBefore(paragraph, list);
            parentCollection.Remove(paragraph);
        }

        private static void ConvertListItemToParagraph(ListItem listItem, System.Windows.Documents.List list)
        {
            var parentBlocks = list.Parent switch
            {
                FlowDocument document => document.Blocks,
                Section section => section.Blocks,
                ListItem parentListItem => parentListItem.Blocks,
                _ => null
            };
            if (parentBlocks == null)
                return;

            var paragraph = listItem.Blocks.OfType<Paragraph>().FirstOrDefault();
            if (paragraph == null)
                return;

            var newParagraph = CloneParagraph(paragraph);
            newParagraph.Margin = list.Margin;

            parentBlocks.InsertBefore(list, newParagraph);
            list.ListItems.Remove(listItem);
            if (list.ListItems.Count == 0)
                parentBlocks.Remove(list);
        }

        private static Paragraph CloneParagraph(Paragraph source)
        {
            var xaml = XamlWriter.Save(source);
            return (Paragraph)XamlReader.Parse(xaml);
        }

        private static double GetLineSpacingValue(Paragraph paragraph)
        {
            var fontSize = paragraph.FontSize > 0 ? paragraph.FontSize : 12;
            if (paragraph.LineHeight <= 0 || double.IsNaN(paragraph.LineHeight))
                return 1.0;

            return Math.Round(paragraph.LineHeight / fontSize, 2);
        }

        private static void SyncComboSelection(ComboBox comboBox, double actualValue)
        {
            ComboBoxItem? nearestItem = null;
            var bestDelta = double.MaxValue;

            foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (!double.TryParse(item.Tag?.ToString() ?? item.Content?.ToString(), out var itemValue))
                    continue;

                var delta = Math.Abs(itemValue - actualValue);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    nearestItem = item;
                }
            }

            if (nearestItem != null)
                comboBox.SelectedItem = nearestItem;
        }

        private void UpdatePageBreakLines()
        {
            if (PageBreakCanvas == null || Editor == null) return;

            PageBreakCanvas.Children.Clear();

            double totalHeight = Editor.ActualHeight;
            var pageHeight = _vm?.PagePreviewHeight ?? DefaultPageHeightPx;
            if (totalHeight <= pageHeight) return;

            int pageBreaks = (int)(totalHeight / pageHeight);
            double width = Editor.ActualWidth;
            var leftMargin = _vm?.PagePadding.Left ?? 0;
            var rightMargin = Math.Max(leftMargin, width - (_vm?.PagePadding.Right ?? 0));

            for (int i = 1; i <= pageBreaks; i++)
            {
                double y = i * pageHeight;

                var topLine = new Line
                {
                    X1 = 8,
                    X2 = width - 8,
                    Y1 = y - 4,
                    Y2 = y - 4,
                    Stroke = new SolidColorBrush(Color.FromRgb(176, 176, 176)),
                    StrokeThickness = 1.5,
                    SnapsToDevicePixels = true
                };
                PageBreakCanvas.Children.Add(topLine);

                var bottomLine = new Line
                {
                    X1 = 8,
                    X2 = width - 8,
                    Y1 = y + 4,
                    Y2 = y + 4,
                    Stroke = new SolidColorBrush(Color.FromRgb(208, 208, 208)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 8, 4 },
                    SnapsToDevicePixels = true
                };
                PageBreakCanvas.Children.Add(bottomLine);

                var leftMarker = new Line
                {
                    X1 = leftMargin,
                    X2 = leftMargin,
                    Y1 = y - 11,
                    Y2 = y + 11,
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    StrokeThickness = 1.5,
                    SnapsToDevicePixels = true
                };
                PageBreakCanvas.Children.Add(leftMarker);

                var rightMarker = new Line
                {
                    X1 = rightMargin,
                    X2 = rightMargin,
                    Y1 = y - 11,
                    Y2 = y + 11,
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                    StrokeThickness = 1.5,
                    SnapsToDevicePixels = true
                };
                PageBreakCanvas.Children.Add(rightMarker);

                var label = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 3, 10, 3),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                    BorderThickness = new Thickness(1),
                    Effect = new DropShadowEffect
                    {
                        BlurRadius = 6,
                        ShadowDepth = 1,
                        Opacity = 0.18,
                        Color = Colors.Black
                    },
                    Child = new TextBlock
                    {
                        Text = ResF("EditorPageBadgeFmt", i + 1),
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                        FontFamily = new FontFamily("Segoe UI"),
                    }
                };
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(label, Math.Max(12, width - label.DesiredSize.Width - 18));
                Canvas.SetTop(label, y - (label.DesiredSize.Height / 2));
                PageBreakCanvas.Children.Add(label);
            }
        }

        private void UpdateRuler()
        {
            if (RulerCanvas == null || PageBorder == null || _vm == null)
                return;

            RulerCanvas.Children.Clear();

            var width = Math.Max(0, PageBorder.ActualWidth);
            if (width <= 0)
                return;

            RulerCanvas.Width = width;

            var leftMargin = _vm.PagePadding.Left;
            var rightMargin = Math.Max(0, width - _vm.PagePadding.Right);

            var leftShade = new Rectangle
            {
                Width = Math.Max(0, leftMargin),
                Height = 27,
                Fill = new SolidColorBrush(Color.FromRgb(242, 242, 242))
            };
            RulerCanvas.Children.Add(leftShade);

            var rightShade = new Rectangle
            {
                Width = Math.Max(0, width - rightMargin),
                Height = 27,
                Fill = new SolidColorBrush(Color.FromRgb(242, 242, 242))
            };
            Canvas.SetLeft(rightShade, rightMargin);
            RulerCanvas.Children.Add(rightShade);

            var baseLine = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = 26,
                Y2 = 26,
                Stroke = new SolidColorBrush(Color.FromRgb(190, 190, 190)),
                StrokeThickness = 1
            };
            RulerCanvas.Children.Add(baseLine);

            for (double x = 0; x <= width; x += 24)
            {
                var isMajor = Math.Abs(x % 96) < 0.1;
                var tick = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = isMajor ? 8 : 16,
                    Y2 = 26,
                    Stroke = new SolidColorBrush(Color.FromRgb(110, 110, 110)),
                    StrokeThickness = 1
                };
                RulerCanvas.Children.Add(tick);

                if (isMajor && x > 0)
                {
                    var label = new TextBlock
                    {
                        Text = (x / 96d).ToString("0.#"),
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 110))
                    };
                    Canvas.SetLeft(label, x + 2);
                    Canvas.SetTop(label, 0);
                    RulerCanvas.Children.Add(label);
                }
            }
        }
    }
}

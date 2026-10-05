using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;
using WD = System.Windows.Documents;
using WpfImage = System.Windows.Controls.Image;

namespace Win11DesktopApp.Services
{
    /// <summary>Page geometry in twips (1/1440 inch).</summary>
    public readonly record struct DocxPageSetup(
        int WidthTwips,
        int HeightTwips,
        int MarginLeftTwips,
        int MarginTopTwips,
        int MarginRightTwips,
        int MarginBottomTwips)
    {
        public static DocxPageSetup A4Portrait => new(11906, 16838, 1440, 1440, 1440, 1440);

        public static DocxPageSetup FromPixels(double widthPx, double heightPx, Thickness marginPx)
        {
            static int Tw(double px) => (int)Math.Round(px * 15);
            return new DocxPageSetup(
                NormalizePaperTwips(Tw(widthPx)),
                NormalizePaperTwips(Tw(heightPx)),
                Tw(marginPx.Left), Tw(marginPx.Top), Tw(marginPx.Right), Tw(marginPx.Bottom));
        }

        private static int NormalizePaperTwips(int twips) => twips switch
        {
            >= 11895 and <= 11925 => 11906, // A4 short edge
            >= 16830 and <= 16860 => 16838, // A4 long edge
            _ => twips
        };
    }

    /// <summary>
    /// Converts legacy editor content (WPF FlowDocument from content.xamlpackage / content.rtf) into a native DOCX.
    /// Must run on an STA thread.
    /// </summary>
    public static class FlowDocumentDocxConverter
    {
        private const double DefaultFontSizePx = 16.0; // 12 pt
        private const string DefaultFontFamily = "Segoe UI";

        public static WD.FlowDocument? LoadLegacyDocument(string? xamlPackagePath, string? rtfPath)
        {
            if (!string.IsNullOrWhiteSpace(xamlPackagePath) && File.Exists(xamlPackagePath))
            {
                try
                {
                    using var stream = File.OpenRead(xamlPackagePath);
                    return LoadInto(stream, DataFormats.XamlPackage);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FlowDocumentDocxConverter.LoadXamlPackage", ex.Message);
                }
            }

            if (!string.IsNullOrWhiteSpace(rtfPath) && File.Exists(rtfPath))
            {
                try
                {
                    using var stream = File.OpenRead(rtfPath);
                    return LoadInto(stream, DataFormats.Rtf);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FlowDocumentDocxConverter.LoadRtf", ex.Message);
                }
            }

            return null;
        }

        public static WD.FlowDocument LoadRtf(string rtf)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rtf));
            return LoadInto(stream, DataFormats.Rtf);
        }

        public static WD.FlowDocument LoadRtf(Stream rtf) => LoadInto(rtf, DataFormats.Rtf);

        public static byte[] RtfToDocx(string rtf, DocxPageSetup page) => Convert(LoadRtf(rtf), page);

        private static WD.FlowDocument LoadInto(Stream stream, string format)
        {
            var document = new WD.FlowDocument
            {
                FontFamily = new FontFamily(DefaultFontFamily),
                FontSize = DefaultFontSizePx
            };
            var range = new WD.TextRange(document.ContentStart, document.ContentEnd);
            range.Load(stream, format);
            return document;
        }

        public static byte[] Convert(WD.FlowDocument document, DocxPageSetup page)
        {
            using var output = new MemoryStream();
            using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
            {
                var mainPart = package.AddMainDocumentPart();
                AddStyles(mainPart);

                var context = new ConversionContext(mainPart, page);
                var body = new W.Body();
                foreach (var block in document.Blocks)
                    context.AppendBlock(body, block, ListScope.None);

                if (!body.Elements<W.Paragraph>().Any() && !body.Elements<W.Table>().Any())
                    body.Append(new W.Paragraph());
                if (body.LastChild is W.Table)
                    body.Append(new W.Paragraph());

                body.Append(CreateSectionProperties(page));
                mainPart.Document = new W.Document(body);
                context.WriteNumbering();
                mainPart.Document.Save();
            }

            return output.ToArray();
        }

        private static void AddStyles(MainDocumentPart mainPart)
        {
            var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new W.Styles(
                new W.DocDefaults(
                    new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(
                        new W.RunFonts { Ascii = DefaultFontFamily, HighAnsi = DefaultFontFamily, ComplexScript = DefaultFontFamily, EastAsia = DefaultFontFamily },
                        new W.FontSize { Val = "24" },
                        new W.FontSizeComplexScript { Val = "24" },
                        new W.Languages { Val = "cs-CZ" })),
                    new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                        new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }))),
                new W.Style(
                    new W.StyleName { Val = "Normal" },
                    new W.PrimaryStyle())
                { Type = W.StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new W.Style(
                    new W.StyleName { Val = "Table Grid" },
                    new W.BasedOn { Val = "TableNormal" })
                { Type = W.StyleValues.Table, StyleId = "TableGrid" },
                new W.Style(
                    new W.StyleName { Val = "Normal Table" },
                    new W.UIPriority { Val = 99 },
                    new W.StyleTableProperties(
                        new W.TableIndentation { Width = 0, Type = W.TableWidthUnitValues.Dxa },
                        new W.TableCellMarginDefault(
                            new W.TopMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                            new W.TableCellLeftMargin { Width = 108, Type = W.TableWidthValues.Dxa },
                            new W.BottomMargin { Width = "0", Type = W.TableWidthUnitValues.Dxa },
                            new W.TableCellRightMargin { Width = 108, Type = W.TableWidthValues.Dxa })))
                { Type = W.StyleValues.Table, StyleId = "TableNormal", Default = true });
            stylesPart.Styles.Save();
        }

        private static W.SectionProperties CreateSectionProperties(DocxPageSetup page)
        {
            var size = new W.PageSize { Width = (UInt32Value)(uint)page.WidthTwips, Height = (UInt32Value)(uint)page.HeightTwips };
            if (page.WidthTwips > page.HeightTwips)
                size.Orient = W.PageOrientationValues.Landscape;

            return new W.SectionProperties(
                size,
                new W.PageMargin
                {
                    Top = page.MarginTopTwips,
                    Bottom = page.MarginBottomTwips,
                    Left = (UInt32Value)(uint)page.MarginLeftTwips,
                    Right = (UInt32Value)(uint)page.MarginRightTwips,
                    Header = 708,
                    Footer = 708,
                    Gutter = 0
                });
        }

        private sealed record ListScope(int Level, int NumId, double IndentTwips, bool IsFirstParagraphOfItem)
        {
            public static readonly ListScope None = new(-1, 0, 0, false);
            public bool IsList => Level >= 0;
        }

        private sealed class ConversionContext
        {
            private readonly MainDocumentPart _mainPart;
            private readonly int _contentWidthTwips;
            private readonly List<(int NumId, TextMarkerStyle Marker, int Start)> _lists = new();
            private uint _drawingId = 1;

            public ConversionContext(MainDocumentPart mainPart, DocxPageSetup page)
            {
                _mainPart = mainPart;
                _contentWidthTwips = Math.Max(1440, page.WidthTwips - page.MarginLeftTwips - page.MarginRightTwips);
            }

            public void AppendBlock(OpenXmlCompositeElement container, WD.Block block, ListScope scope, int availableWidthTwips = 0)
            {
                var width = availableWidthTwips > 0 ? availableWidthTwips : _contentWidthTwips;
                switch (block)
                {
                    case WD.Paragraph paragraph:
                        container.Append(ConvertParagraph(paragraph, scope, width));
                        break;
                    case WD.List list:
                        AppendList(container, list, scope, width);
                        break;
                    case WD.Table table:
                        container.Append(ConvertTable(table, width));
                        break;
                    case WD.Section section:
                        var first = true;
                        foreach (var child in section.Blocks)
                        {
                            AppendBlock(container, child, first ? scope : scope with { IsFirstParagraphOfItem = false }, width);
                            first = false;
                        }
                        break;
                    case WD.BlockUIContainer ui:
                        var imageParagraph = new W.Paragraph(CreateParagraphProperties(ui, ListScope.None, ui.FontSize));
                        var imageRun = TryCreateImageRun(ui.Child, width);
                        if (imageRun != null)
                            imageParagraph.Append(imageRun);
                        container.Append(imageParagraph);
                        break;
                }
            }

            private void AppendList(OpenXmlCompositeElement container, WD.List list, ListScope parent, int width)
            {
                var level = Math.Min(parent.Level + 1, 8);
                var numId = RegisterList(list);
                var listPadding = double.IsNaN(list.Padding.Left) ? 24 : list.Padding.Left;
                var listMargin = double.IsNaN(list.Margin.Left) ? 0 : list.Margin.Left;
                var indent = (parent.IsList ? parent.IndentTwips : 0) + (listPadding + listMargin) * 15;

                foreach (var item in list.ListItems)
                {
                    var first = true;
                    foreach (var child in item.Blocks)
                    {
                        AppendBlock(container, child, new ListScope(level, numId, indent, first && child is WD.Paragraph), width);
                        if (child is WD.Paragraph)
                            first = false;
                    }
                }
            }

            private int RegisterList(WD.List list)
            {
                var numId = _lists.Count + 1;
                _lists.Add((numId, list.MarkerStyle, Math.Max(1, list.StartIndex)));
                return numId;
            }

            public void WriteNumbering()
            {
                if (_lists.Count == 0)
                    return;

                var numbering = new W.Numbering();
                foreach (var (numId, marker, start) in _lists)
                {
                    var abstractNum = new W.AbstractNum { AbstractNumberId = numId };
                    abstractNum.Append(new W.MultiLevelType { Val = W.MultiLevelValues.HybridMultilevel });
                    for (var lvl = 0; lvl < 9; lvl++)
                        abstractNum.Append(CreateLevel(lvl, marker, start));
                    numbering.Append(abstractNum);
                }

                foreach (var (numId, _, _) in _lists)
                    numbering.Append(new W.NumberingInstance(new W.AbstractNumId { Val = numId }) { NumberID = numId });

                var part = _mainPart.AddNewPart<NumberingDefinitionsPart>();
                part.Numbering = numbering;
                part.Numbering.Save();
            }

            private static W.Level CreateLevel(int lvl, TextMarkerStyle marker, int start)
            {
                var (format, text, font) = marker switch
                {
                    TextMarkerStyle.Decimal => (W.NumberFormatValues.Decimal, $"%{lvl + 1}.", (string?)null),
                    TextMarkerStyle.LowerLatin => (W.NumberFormatValues.LowerLetter, $"%{lvl + 1}.", null),
                    TextMarkerStyle.UpperLatin => (W.NumberFormatValues.UpperLetter, $"%{lvl + 1}.", null),
                    TextMarkerStyle.LowerRoman => (W.NumberFormatValues.LowerRoman, $"%{lvl + 1}.", null),
                    TextMarkerStyle.UpperRoman => (W.NumberFormatValues.UpperRoman, $"%{lvl + 1}.", null),
                    TextMarkerStyle.Circle => (W.NumberFormatValues.Bullet, "\u25E6", "Segoe UI Symbol"),
                    TextMarkerStyle.Square => (W.NumberFormatValues.Bullet, "\u25AA", "Segoe UI Symbol"),
                    TextMarkerStyle.Box => (W.NumberFormatValues.Bullet, "\u25A1", "Segoe UI Symbol"),
                    TextMarkerStyle.None => (W.NumberFormatValues.None, "", null),
                    _ => (W.NumberFormatValues.Bullet, "\u2022", "Arial")
                };

                var level = new W.Level(
                    new W.StartNumberingValue { Val = start },
                    new W.NumberingFormat { Val = format },
                    new W.LevelText { Val = text },
                    new W.LevelJustification { Val = W.LevelJustificationValues.Left },
                    new W.PreviousParagraphProperties(new W.Indentation
                    {
                        Left = ((lvl + 1) * 360).ToString(CultureInfo.InvariantCulture),
                        Hanging = "360"
                    }))
                { LevelIndex = lvl };

                if (font != null)
                    level.Append(new W.NumberingSymbolRunProperties(new W.RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font }));

                return level;
            }

            private W.Paragraph ConvertParagraph(WD.Paragraph paragraph, ListScope scope, int width)
            {
                var result = new W.Paragraph(CreateParagraphProperties(paragraph, scope, paragraph.FontSize));
                foreach (var inline in paragraph.Inlines)
                    AppendInline(result, inline, width);
                return result;
            }

            private W.ParagraphProperties CreateParagraphProperties(WD.Block block, ListScope scope, double fontSizePx)
            {
                var props = new W.ParagraphProperties();

                if (block is WD.Paragraph p)
                {
                    if (p.KeepWithNext)
                        props.KeepNext = new W.KeepNext();
                    if (p.KeepTogether)
                        props.KeepLines = new W.KeepLines();
                }
                if (block.BreakPageBefore)
                    props.PageBreakBefore = new W.PageBreakBefore();

                if (scope.IsList && scope.IsFirstParagraphOfItem)
                {
                    props.NumberingProperties = new W.NumberingProperties(
                        new W.NumberingLevelReference { Val = scope.Level },
                        new W.NumberingId { Val = scope.NumId });
                }

                var borders = CreateParagraphBorders(block);
                if (borders != null)
                    props.ParagraphBorders = borders;

                var background = ToHex(block.Background);
                if (background != null)
                    props.Shading = new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = background };

                var margin = block.Margin;
                var autoMargin = Math.Max(0, fontSizePx);
                var spacing = new W.SpacingBetweenLines
                {
                    Before = Twips(double.IsNaN(margin.Top) ? autoMargin : margin.Top),
                    After = Twips(double.IsNaN(margin.Bottom) ? autoMargin : margin.Bottom)
                };
                if (!double.IsNaN(block.LineHeight) && block.LineHeight > 0)
                {
                    spacing.Line = Twips(block.LineHeight);
                    spacing.LineRule = block.LineStackingStrategy == LineStackingStrategy.BlockLineHeight
                        ? W.LineSpacingRuleValues.Exact
                        : W.LineSpacingRuleValues.AtLeast;
                }
                else
                {
                    spacing.Line = "240";
                    spacing.LineRule = W.LineSpacingRuleValues.Auto;
                }
                props.SpacingBetweenLines = spacing;

                var left = (double.IsNaN(margin.Left) ? 0 : margin.Left) * 15 + PaddingTwips(block.Padding.Left);
                var right = (double.IsNaN(margin.Right) ? 0 : margin.Right) * 15 + PaddingTwips(block.Padding.Right);
                var textIndent = block is WD.Paragraph para && !double.IsNaN(para.TextIndent) ? para.TextIndent * 15 : 0;
                var indentation = new W.Indentation();
                var hasIndent = false;

                if (scope.IsList)
                {
                    left += scope.IndentTwips;
                    if (scope.IsFirstParagraphOfItem)
                    {
                        indentation.Hanging = "360";
                        hasIndent = true;
                    }
                }
                else if (Math.Abs(textIndent) >= 1)
                {
                    if (textIndent > 0)
                        indentation.FirstLine = Round(textIndent);
                    else
                        indentation.Hanging = Round(-textIndent);
                    hasIndent = true;
                }

                if (left >= 1 || hasIndent)
                {
                    indentation.Left = Round(left);
                    hasIndent = true;
                }
                if (right >= 1)
                {
                    indentation.Right = Round(right);
                    hasIndent = true;
                }
                if (hasIndent)
                    props.Indentation = indentation;

                props.Justification = new W.Justification
                {
                    Val = block.TextAlignment switch
                    {
                        TextAlignment.Center => W.JustificationValues.Center,
                        TextAlignment.Right => W.JustificationValues.Right,
                        TextAlignment.Justify => W.JustificationValues.Both,
                        _ => W.JustificationValues.Left
                    }
                };

                var markProps = new W.ParagraphMarkRunProperties();
                var family = NormalizeFontFamily(block.FontFamily);
                markProps.Append(new W.RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family, EastAsia = family });
                var halfPoints = HalfPoints(fontSizePx);
                markProps.Append(new W.FontSize { Val = halfPoints });
                markProps.Append(new W.FontSizeComplexScript { Val = halfPoints });
                props.ParagraphMarkRunProperties = markProps;

                return props;
            }

            private static W.ParagraphBorders? CreateParagraphBorders(WD.Block block)
            {
                var t = block.BorderThickness;
                if (t.Left <= 0 && t.Top <= 0 && t.Right <= 0 && t.Bottom <= 0)
                    return null;

                var color = ToHex(block.BorderBrush) ?? "000000";
                var borders = new W.ParagraphBorders();
                if (t.Top > 0) borders.TopBorder = Border<W.TopBorder>(t.Top, color);
                if (t.Left > 0) borders.LeftBorder = Border<W.LeftBorder>(t.Left, color);
                if (t.Bottom > 0) borders.BottomBorder = Border<W.BottomBorder>(t.Bottom, color);
                if (t.Right > 0) borders.RightBorder = Border<W.RightBorder>(t.Right, color);
                return borders;
            }

            private void AppendInline(OpenXmlCompositeElement target, WD.Inline inline, int width)
            {
                switch (inline)
                {
                    case WD.Run run:
                        AppendTextRuns(target, run.Text, CreateRunProperties(run));
                        break;
                    case WD.LineBreak lineBreak:
                        target.Append(new W.Run(CreateRunProperties(lineBreak), new W.Break()));
                        break;
                    case WD.Hyperlink hyperlink when hyperlink.NavigateUri is { IsAbsoluteUri: true } uri:
                        var relationship = _mainPart.AddHyperlinkRelationship(uri, true);
                        var link = new W.Hyperlink { Id = relationship.Id, History = true };
                        foreach (var child in hyperlink.Inlines)
                            AppendInline(link, child, width);
                        target.Append(link);
                        break;
                    case WD.Span span:
                        foreach (var child in span.Inlines)
                            AppendInline(target, child, width);
                        break;
                    case WD.InlineUIContainer ui:
                        var imageRun = TryCreateImageRun(ui.Child, width);
                        if (imageRun != null)
                            target.Append(imageRun);
                        break;
                    case WD.AnchoredBlock anchored:
                        foreach (var block in anchored.Blocks)
                        {
                            if (block is WD.Paragraph nested)
                            {
                                foreach (var child in nested.Inlines)
                                    AppendInline(target, child, width);
                            }
                        }
                        break;
                }
            }

            private static void AppendTextRuns(OpenXmlCompositeElement target, string? text, W.RunProperties props)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                var run = new W.Run(props);
                var buffer = new StringBuilder();

                void Flush()
                {
                    if (buffer.Length == 0)
                        return;
                    run.Append(new W.Text(buffer.ToString()) { Space = SpaceProcessingModeValues.Preserve });
                    buffer.Clear();
                }

                for (var i = 0; i < text.Length; i++)
                {
                    var ch = text[i];
                    switch (ch)
                    {
                        case '\t':
                            Flush();
                            run.Append(new W.TabChar());
                            break;
                        case '\r':
                            if (i + 1 < text.Length && text[i + 1] == '\n')
                                i++;
                            Flush();
                            run.Append(new W.Break());
                            break;
                        case '\n':
                        case '\u2028':
                        case '\u000B':
                            Flush();
                            run.Append(new W.Break());
                            break;
                        default:
                            if (IsValidXmlChar(ch) || (char.IsSurrogate(ch)))
                                buffer.Append(ch);
                            break;
                    }
                }

                Flush();
                target.Append(run);
            }

            private static W.RunProperties CreateRunProperties(WD.TextElement element)
            {
                var props = new W.RunProperties();
                var family = NormalizeFontFamily(element.FontFamily);
                props.RunFonts = new W.RunFonts { Ascii = family, HighAnsi = family, ComplexScript = family, EastAsia = family };

                if (element.FontWeight.ToOpenTypeWeight() >= FontWeights.SemiBold.ToOpenTypeWeight())
                {
                    props.Bold = new W.Bold();
                    props.BoldComplexScript = new W.BoldComplexScript();
                }
                if (element.FontStyle == FontStyles.Italic || element.FontStyle == FontStyles.Oblique)
                {
                    props.Italic = new W.Italic();
                    props.ItalicComplexScript = new W.ItalicComplexScript();
                }

                var decorations = CollectDecorations(element);
                if (decorations.Any(d => d.Location == TextDecorationLocation.Strikethrough))
                    props.Strike = new W.Strike();

                var color = ToHex(element.Foreground);
                if (color != null && color != "000000")
                    props.Color = new W.Color { Val = color };

                var halfPoints = HalfPoints(element.FontSize);
                props.FontSize = new W.FontSize { Val = halfPoints };
                props.FontSizeComplexScript = new W.FontSizeComplexScript { Val = halfPoints };

                if (decorations.Any(d => d.Location == TextDecorationLocation.Underline))
                    props.Underline = new W.Underline { Val = W.UnderlineValues.Single };

                var background = FindBackground(element);
                if (background != null)
                    props.Shading = new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = background };

                var baseline = FindBaseline(element);
                if (baseline == BaselineAlignment.Superscript)
                    props.VerticalTextAlignment = new W.VerticalTextAlignment { Val = W.VerticalPositionValues.Superscript };
                else if (baseline == BaselineAlignment.Subscript)
                    props.VerticalTextAlignment = new W.VerticalTextAlignment { Val = W.VerticalPositionValues.Subscript };

                return props;
            }

            private static List<TextDecoration> CollectDecorations(WD.TextElement element)
            {
                var result = new List<TextDecoration>();
                for (DependencyObject? current = element; current is WD.TextElement te; current = te.Parent)
                {
                    var decorations = te switch
                    {
                        WD.Inline inline => inline.TextDecorations,
                        WD.Paragraph paragraph => paragraph.TextDecorations,
                        _ => null
                    };
                    if (decorations != null)
                        result.AddRange(decorations);
                    if (te is WD.Block)
                        break;
                }
                return result;
            }

            private static string? FindBackground(WD.TextElement element)
            {
                for (DependencyObject? current = element; current is WD.Inline inline; current = inline.Parent)
                {
                    var hex = ToHex(inline.Background);
                    if (hex != null)
                        return hex;
                }
                return null;
            }

            private static BaselineAlignment FindBaseline(WD.TextElement element)
            {
                for (DependencyObject? current = element; current is WD.Inline inline; current = inline.Parent)
                {
                    if (inline.BaselineAlignment is BaselineAlignment.Superscript or BaselineAlignment.Subscript)
                        return inline.BaselineAlignment;
                }
                return BaselineAlignment.Baseline;
            }

            private W.Table ConvertTable(WD.Table table, int availableWidth)
            {
                var rows = table.RowGroups.SelectMany(g => g.Rows).ToList();
                var gridCount = Math.Max(
                    table.Columns.Count,
                    rows.Count == 0 ? 1 : rows.Max(r => r.Cells.Sum(c => Math.Max(1, c.ColumnSpan))));
                gridCount = Math.Max(1, gridCount);
                var gridWidths = ComputeGridWidths(table, gridCount, availableWidth);

                var tableProps = new W.TableProperties(
                    new W.TableWidth { Width = gridWidths.Sum().ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa },
                    new W.TableLayout { Type = W.TableLayoutValues.Fixed });

                var outer = table.BorderThickness;
                if (outer.Left > 0 || outer.Top > 0 || outer.Right > 0 || outer.Bottom > 0)
                {
                    var color = ToHex(table.BorderBrush) ?? "000000";
                    var borders = new W.TableBorders();
                    if (outer.Top > 0) borders.TopBorder = Border<W.TopBorder>(outer.Top, color);
                    if (outer.Left > 0) borders.LeftBorder = Border<W.LeftBorder>(outer.Left, color);
                    if (outer.Bottom > 0) borders.BottomBorder = Border<W.BottomBorder>(outer.Bottom, color);
                    if (outer.Right > 0) borders.RightBorder = Border<W.RightBorder>(outer.Right, color);
                    tableProps.TableBorders = borders;
                }

                var result = new W.Table(tableProps);
                var grid = new W.TableGrid();
                foreach (var w in gridWidths)
                    grid.Append(new W.GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) });
                result.Append(grid);

                var pending = new PendingMerge?[gridCount];
                foreach (var row in rows)
                {
                    var tr = new W.TableRow();
                    var col = 0;
                    var cellIndex = 0;

                    while (col < gridCount)
                    {
                        if (pending[col] is { } merge)
                        {
                            tr.Append(CreateMergeContinuationCell(merge, gridWidths, col));
                            pending[col] = merge.Remaining > 1 ? merge with { Remaining = merge.Remaining - 1 } : null;
                            col += merge.Span;
                            continue;
                        }

                        if (cellIndex >= row.Cells.Count)
                            break;

                        var cell = row.Cells[cellIndex++];
                        var span = Math.Clamp(cell.ColumnSpan, 1, gridCount - col);
                        var width = gridWidths.Skip(col).Take(span).Sum();
                        var tc = ConvertCell(cell, span, width);
                        if (cell.RowSpan > 1)
                        {
                            tc.TableCellProperties!.VerticalMerge = new W.VerticalMerge { Val = W.MergedCellValues.Restart };
                            pending[col] = new PendingMerge(cell.RowSpan - 1, span, tc.TableCellProperties.TableCellBorders?.CloneNode(true) as W.TableCellBorders);
                        }
                        tr.Append(tc);
                        col += span;
                    }

                    if (!tr.Elements<W.TableCell>().Any())
                        tr.Append(new W.TableCell(new W.TableCellProperties(new W.TableCellWidth { Width = gridWidths[0].ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa }), new W.Paragraph()));

                    result.Append(tr);
                }

                return result;
            }

            private sealed record PendingMerge(int Remaining, int Span, W.TableCellBorders? Borders);

            private static W.TableCell CreateMergeContinuationCell(PendingMerge merge, int[] gridWidths, int col)
            {
                var width = gridWidths.Skip(col).Take(merge.Span).Sum();
                var props = new W.TableCellProperties(new W.TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa });
                if (merge.Span > 1)
                    props.GridSpan = new W.GridSpan { Val = merge.Span };
                props.VerticalMerge = new W.VerticalMerge();
                if (merge.Borders != null)
                    props.TableCellBorders = (W.TableCellBorders)merge.Borders.CloneNode(true);
                return new W.TableCell(props, new W.Paragraph());
            }

            private W.TableCell ConvertCell(WD.TableCell cell, int span, int width)
            {
                var props = new W.TableCellProperties(new W.TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = W.TableWidthUnitValues.Dxa });
                if (span > 1)
                    props.GridSpan = new W.GridSpan { Val = span };

                var t = cell.BorderThickness;
                if (t.Left > 0 || t.Top > 0 || t.Right > 0 || t.Bottom > 0)
                {
                    var color = ToHex(cell.BorderBrush) ?? "000000";
                    props.TableCellBorders = new W.TableCellBorders
                    {
                        TopBorder = t.Top > 0 ? Border<W.TopBorder>(t.Top, color) : NilBorder<W.TopBorder>(),
                        LeftBorder = t.Left > 0 ? Border<W.LeftBorder>(t.Left, color) : NilBorder<W.LeftBorder>(),
                        BottomBorder = t.Bottom > 0 ? Border<W.BottomBorder>(t.Bottom, color) : NilBorder<W.BottomBorder>(),
                        RightBorder = t.Right > 0 ? Border<W.RightBorder>(t.Right, color) : NilBorder<W.RightBorder>()
                    };
                }

                var fill = ToHex(cell.Background);
                if (fill != null)
                    props.Shading = new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = fill };

                var pad = cell.Padding;
                if (!double.IsNaN(pad.Left) && (pad.Left > 0 || pad.Top > 0 || pad.Right > 0 || pad.Bottom > 0))
                {
                    props.TableCellMargin = new W.TableCellMargin
                    {
                        TopMargin = new W.TopMargin { Width = Twips(pad.Top), Type = W.TableWidthUnitValues.Dxa },
                        LeftMargin = new W.LeftMargin { Width = Twips(pad.Left), Type = W.TableWidthUnitValues.Dxa },
                        BottomMargin = new W.BottomMargin { Width = Twips(pad.Bottom), Type = W.TableWidthUnitValues.Dxa },
                        RightMargin = new W.RightMargin { Width = Twips(pad.Right), Type = W.TableWidthUnitValues.Dxa }
                    };
                }

                var tc = new W.TableCell(props);
                var innerWidth = Math.Max(360, width - 216);
                foreach (var block in cell.Blocks)
                    AppendBlock(tc, block, ListScope.None, innerWidth);
                if (tc.LastChild is not W.Paragraph)
                    tc.Append(new W.Paragraph());
                return tc;
            }

            private static int[] ComputeGridWidths(WD.Table table, int gridCount, int availableWidth)
            {
                var widths = new double[gridCount];
                var starWeights = new double[gridCount];
                double fixedTotal = 0;

                for (var i = 0; i < gridCount; i++)
                {
                    var length = i < table.Columns.Count ? table.Columns[i].Width : new GridLength(1, GridUnitType.Star);
                    if (length.IsAbsolute && length.Value > 0)
                    {
                        widths[i] = length.Value * 15;
                        fixedTotal += widths[i];
                    }
                    else
                    {
                        starWeights[i] = length.IsStar && length.Value > 0 ? length.Value : 1;
                    }
                }

                var starTotal = starWeights.Sum();
                if (starTotal > 0)
                {
                    var remaining = Math.Max(availableWidth - fixedTotal, 360 * starWeights.Count(w => w > 0));
                    for (var i = 0; i < gridCount; i++)
                    {
                        if (starWeights[i] > 0)
                            widths[i] = remaining * starWeights[i] / starTotal;
                    }
                }

                var total = widths.Sum();
                if (total > availableWidth && total > 0)
                {
                    var scale = availableWidth / total;
                    for (var i = 0; i < gridCount; i++)
                        widths[i] *= scale;
                }

                return widths.Select(w => Math.Max(120, (int)Math.Round(w))).ToArray();
            }

            private W.Run? TryCreateImageRun(UIElement? element, int maxWidthTwips)
            {
                if (element is not WpfImage { Source: BitmapSource bitmap } image)
                    return null;

                try
                {
                    var widthPx = !double.IsNaN(image.Width) && image.Width > 0 ? image.Width : bitmap.Width;
                    var heightPx = !double.IsNaN(image.Height) && image.Height > 0 ? image.Height : bitmap.Height;
                    if (widthPx <= 0 || heightPx <= 0)
                        return null;

                    var maxWidthPx = maxWidthTwips / 15.0;
                    if (widthPx > maxWidthPx)
                    {
                        heightPx *= maxWidthPx / widthPx;
                        widthPx = maxWidthPx;
                    }

                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var png = new MemoryStream();
                    encoder.Save(png);
                    png.Position = 0;

                    var imagePart = _mainPart.AddImagePart(ImagePartType.Png);
                    imagePart.FeedData(png);
                    var relId = _mainPart.GetIdOfPart(imagePart);

                    var cx = (long)Math.Round(widthPx * 9525);
                    var cy = (long)Math.Round(heightPx * 9525);
                    var id = _drawingId++;
                    var name = $"Picture {id}";

                    var inline = new DW.Inline(
                        new DW.Extent { Cx = cx, Cy = cy },
                        new DW.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 },
                        new DW.DocProperties { Id = id, Name = name },
                        new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(new A.Blip { Embed = relId }, new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
                    {
                        DistanceFromTop = 0U,
                        DistanceFromBottom = 0U,
                        DistanceFromLeft = 0U,
                        DistanceFromRight = 0U
                    };

                    return new W.Run(new W.Drawing(inline));
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FlowDocumentDocxConverter.Image", ex.Message);
                    return null;
                }
            }
        }

        private static T Border<T>(double thicknessPx, string color) where T : W.BorderType, new() => new()
        {
            Val = W.BorderValues.Single,
            Size = (UInt32Value)(uint)Math.Clamp(Math.Round(thicknessPx * 6), 2, 96),
            Space = 0U,
            Color = color
        };

        private static T NilBorder<T>() where T : W.BorderType, new() => new() { Val = W.BorderValues.Nil };

        private static string NormalizeFontFamily(FontFamily? family)
        {
            var source = family?.Source;
            if (string.IsNullOrWhiteSpace(source))
                return DefaultFontFamily;

            var first = source.Split(',')[0].Trim();
            var hash = first.LastIndexOf('#');
            if (hash >= 0)
                first = first[(hash + 1)..].Trim();

            return first switch
            {
                "" => DefaultFontFamily,
                "Global User Interface" => DefaultFontFamily,
                "Global Sans Serif" => "Arial",
                "Global Serif" => "Times New Roman",
                "Global Monospace" => "Courier New",
                _ => first
            };
        }

        private static string HalfPoints(double fontSizePx)
        {
            var px = double.IsNaN(fontSizePx) || fontSizePx <= 0 ? DefaultFontSizePx : fontSizePx;
            return Math.Max(2, (int)Math.Round(px * 1.5)).ToString(CultureInfo.InvariantCulture);
        }

        private static string Twips(double px) => Round(Math.Max(0, double.IsNaN(px) ? 0 : px) * 15);

        private static double PaddingTwips(double px) => double.IsNaN(px) ? 0 : Math.Max(0, px) * 15;

        private static string Round(double twips) => ((int)Math.Round(twips)).ToString(CultureInfo.InvariantCulture);

        private static string? ToHex(Brush? brush)
        {
            if (brush is not SolidColorBrush solid || solid.Color.A == 0 || solid.Opacity <= 0)
                return null;
            var c = solid.Color;
            return $"{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        private static bool IsValidXmlChar(char ch) =>
            ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD);
    }
}

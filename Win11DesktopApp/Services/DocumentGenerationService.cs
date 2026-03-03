using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using System.Windows;
using Win11DesktopApp.Models;
using AlternativeFormatImportPartType = DocumentFormat.OpenXml.Packaging.AlternativeFormatImportPartType;

namespace Win11DesktopApp.Services
{
    public class DocumentGenerationService
    {
        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        /// <summary>
        /// Generates a DOCX document from a template by replacing all ${TAG} placeholders with values.
        /// Preserves all formatting, styles, tables, images — only tag text is replaced.
        /// </summary>
        public string GenerateDocx(string templatePath, string outputPath, Dictionary<string, string> tagValues)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException(Res("MsgTemplateMissing"), templatePath);

            File.Copy(templatePath, outputPath, true);

            using (var doc = WordprocessingDocument.Open(outputPath, true))
            {
                if (doc.MainDocumentPart?.Document.Body != null)
                    ReplaceTags(doc.MainDocumentPart.Document.Body, tagValues);

                foreach (var headerPart in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                {
                    if (headerPart.Header != null)
                        ReplaceTags(headerPart.Header, tagValues);
                }

                foreach (var footerPart in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                {
                    if (footerPart.Footer != null)
                        ReplaceTags(footerPart.Footer, tagValues);
                }

                doc.Save();
            }

            return outputPath;
        }

        private void ReplaceTags(OpenXmlElement root, Dictionary<string, string> tagValues)
        {
            foreach (var paragraph in root.Descendants<Paragraph>())
            {
                ReplaceTagsInParagraph(paragraph, tagValues);
            }
        }

        private void ReplaceTagsInParagraph(Paragraph paragraph, Dictionary<string, string> tagValues)
        {
            // Collect all (Run, Text) segments preserving order
            var segments = new List<TextSegment>();
            foreach (var run in paragraph.Descendants<Run>())
            {
                foreach (var text in run.Elements<Text>())
                {
                    segments.Add(new TextSegment { Run = run, TextElement = text, OriginalText = text.Text });
                }
            }

            if (segments.Count == 0) return;

            // Build full concatenated text
            var fullText = string.Concat(segments.Select(s => s.OriginalText));
            var tagPattern = new Regex(@"\$\{([^}]+)\}", RegexOptions.None, TimeSpan.FromSeconds(5));
            if (!tagPattern.IsMatch(fullText)) return;

            // Map each character position in fullText → segment index + position within segment
            var charMap = new List<(int segIdx, int posInSeg)>();
            for (int si = 0; si < segments.Count; si++)
            {
                for (int ci = 0; ci < segments[si].OriginalText.Length; ci++)
                {
                    charMap.Add((si, ci));
                }
            }

            if (charMap.Count == 0) return;

            // Find all tags and collect replacements
            var matches = tagPattern.Matches(fullText);
            var replacements = new List<TagReplacement>();
            foreach (Match m in matches)
            {
                var tagName = m.Groups[1].Value;
                if (tagValues.TryGetValue(tagName, out var val))
                {
                    replacements.Add(new TagReplacement
                    {
                        Start = m.Index,
                        Length = m.Length,
                        Value = val ?? string.Empty
                    });
                }
            }

            if (replacements.Count == 0) return;

            // Process from LAST to FIRST so earlier positions remain valid
            for (int i = replacements.Count - 1; i >= 0; i--)
            {
                var repl = replacements[i];
                int startPos = repl.Start;
                int endPos = repl.Start + repl.Length - 1;

                var (startSegIdx, startPosInSeg) = charMap[startPos];
                var (endSegIdx, endPosInSeg) = charMap[endPos];

                if (startSegIdx == endSegIdx)
                {
                    // Tag is entirely within ONE Text element — simple in-place replace
                    var seg = segments[startSegIdx];
                    var currentText = seg.TextElement.Text;
                    var newText = currentText.Substring(0, startPosInSeg)
                                + repl.Value
                                + currentText.Substring(endPosInSeg + 1);
                    seg.TextElement.Text = newText;
                    seg.TextElement.Space = SpaceProcessingModeValues.Preserve;
                }
                else
                {
                    // Tag spans MULTIPLE Text elements (Word split the tag across Runs)
                    // Strategy: keep formatting of each Run, only modify the text content

                    // First segment: keep text before tag, append replacement value
                    var firstSeg = segments[startSegIdx];
                    var firstText = firstSeg.TextElement.Text;
                    firstSeg.TextElement.Text = firstText.Substring(0, startPosInSeg) + repl.Value;
                    firstSeg.TextElement.Space = SpaceProcessingModeValues.Preserve;

                    // Last segment: remove tag portion, keep text after tag
                    var lastSeg = segments[endSegIdx];
                    var lastText = lastSeg.TextElement.Text;
                    lastSeg.TextElement.Text = lastText.Substring(endPosInSeg + 1);
                    if (lastSeg.TextElement.Text.Length > 0)
                        lastSeg.TextElement.Space = SpaceProcessingModeValues.Preserve;

                    // Middle segments: clear their text (the tag characters in between)
                    for (int si = startSegIdx + 1; si < endSegIdx; si++)
                    {
                        segments[si].TextElement.Text = string.Empty;
                    }
                }
            }
        }

        private class TextSegment
        {
            public Run Run { get; set; } = null!;
            public Text TextElement { get; set; } = null!;
            public string OriginalText { get; set; } = string.Empty;
        }

        private class TagReplacement
        {
            public int Start { get; set; }
            public int Length { get; set; }
            public string Value { get; set; } = string.Empty;
        }

        /// <summary>
        /// Generates a document from an RTF template by replacing all ${TAG} placeholders.
        /// In RTF format, { and } are escaped as \{ and \}, so we search for $\{TAG\}.
        /// Output is saved as .rtf (opens in Word).
        /// </summary>
        public string GenerateFromRtf(string rtfTemplatePath, string outputPath, Dictionary<string, string> tagValues)
        {
            if (!File.Exists(rtfTemplatePath))
                throw new FileNotFoundException("RTF шаблон не знайдено.", rtfTemplatePath);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansiEncoding = Encoding.GetEncoding(1252);

            var rtfContent = File.ReadAllText(rtfTemplatePath, ansiEncoding);

            foreach (var kvp in tagValues)
            {
                var rtfTag = "$\\{" + kvp.Key + "\\}";
                var safeValue = EscapeRtf(kvp.Value ?? string.Empty);
                rtfContent = rtfContent.Replace(rtfTag, safeValue);
            }

            File.WriteAllText(outputPath, rtfContent, ansiEncoding);
            return outputPath;
        }

        /// <summary>
        /// Generates a DOCX from an RTF template by replacing tags and embedding
        /// the result as an AltChunk in a DOCX container. Word renders the RTF
        /// natively, so all formatting (alignment, fonts, spacing) is preserved.
        /// </summary>
        public string GenerateDocxFromRtf(string rtfTemplatePath, string outputPath, Dictionary<string, string> tagValues)
        {
            if (!File.Exists(rtfTemplatePath))
                throw new FileNotFoundException("RTF шаблон не знайдено.", rtfTemplatePath);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ansiEncoding = Encoding.GetEncoding(1252);

            var rtfContent = File.ReadAllText(rtfTemplatePath, ansiEncoding);

            rtfContent = ReplaceRtfTags(rtfContent, tagValues);

            var docxPath = outputPath;
            if (!docxPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                docxPath = Path.ChangeExtension(docxPath, ".docx");

            using (var doc = WordprocessingDocument.Create(docxPath, WordprocessingDocumentType.Document))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());

                var sectionProps = new SectionProperties(
                    new PageMargin
                    {
                        Top = 720,
                        Right = (UInt32Value)720U,
                        Bottom = 720,
                        Left = (UInt32Value)720U,
                        Header = (UInt32Value)720U,
                        Footer = (UInt32Value)720U
                    });
                mainPart.Document.Body!.Append(sectionProps);

                var chunk = mainPart.AddAlternativeFormatImportPart(
                    AlternativeFormatImportPartType.Rtf, "altChunkRtf1");

                using (var ms = new MemoryStream(ansiEncoding.GetBytes(rtfContent)))
                {
                    chunk.FeedData(ms);
                }

                var altChunk = new AltChunk { Id = "altChunkRtf1" };
                mainPart.Document.Body!.Append(altChunk);
                mainPart.Document.Save();
            }

            return docxPath;
        }

        /// <summary>
        /// Performs RTF-aware tag replacement. Handles cases where the editor may have
        /// inserted formatting codes within the tag text (e.g. underscores escaped as \_).
        /// Also performs a secondary pass using regex for tags split across RTF groups.
        /// </summary>
        private string ReplaceRtfTags(string rtfContent, Dictionary<string, string> tagValues)
        {
            foreach (var kvp in tagValues)
            {
                var safeValue = EscapeRtf(kvp.Value ?? string.Empty);

                var simpleTag = "$\\{" + kvp.Key + "\\}";
                rtfContent = rtfContent.Replace(simpleTag, safeValue);

                if (kvp.Key.Contains('_'))
                {
                    var escapedUnderscoreTag = "$\\{" + kvp.Key.Replace("_", "\\_") + "\\}";
                    rtfContent = rtfContent.Replace(escapedUnderscoreTag, safeValue);
                }
            }

            var splitPattern = new Regex(@"\$\\\{([^}\\]*(?:\\.[^}\\]*)*?)\\\}", RegexOptions.None, TimeSpan.FromSeconds(5));
            rtfContent = splitPattern.Replace(rtfContent, match =>
            {
                var rawTag = match.Groups[1].Value;
                var cleanTag = rawTag
                    .Replace("\\_", "_")
                    .Replace("\\-", "-");
                cleanTag = Regex.Replace(cleanTag, @"\{[^}]*\}", "", RegexOptions.None, TimeSpan.FromSeconds(5));
                cleanTag = Regex.Replace(cleanTag, @"\\[a-z]+\d*\s?", "", RegexOptions.None, TimeSpan.FromSeconds(5));
                cleanTag = cleanTag.Trim();

                if (tagValues.TryGetValue(cleanTag, out var val))
                    return EscapeRtf(val ?? string.Empty);

                return match.Value;
            });

            return rtfContent;
        }

        /// <summary>
        /// Escapes special RTF characters and converts non-ASCII characters
        /// (Czech, Ukrainian, etc.) to RTF Unicode escape sequences (\uNNNN?).
        /// </summary>
        private static string EscapeRtf(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length * 2);
            foreach (var ch in text)
            {
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '{') sb.Append("\\{");
                else if (ch == '}') sb.Append("\\}");
                else if (ch > 127) sb.Append($"\\u{(int)ch}?");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Generates an XLSX document from a template by replacing all ${TAG} placeholders with values.
        /// Preserves all formatting — only tag text in cell values is replaced.
        /// </summary>
        public string GenerateXlsx(string templatePath, string outputPath, Dictionary<string, string> tagValues)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("XLSX шаблон не знайдено.", templatePath);

            File.Copy(templatePath, outputPath, true);

            var tagPattern = new Regex(@"\$\{([^}]+)\}", RegexOptions.None, TimeSpan.FromSeconds(5));

            using (var workbook = new XLWorkbook(outputPath))
            {
                foreach (var ws in workbook.Worksheets)
                {
                    var rangeUsed = ws.RangeUsed();
                    if (rangeUsed == null) continue;

                    for (int r = rangeUsed.FirstRow().RowNumber(); r <= rangeUsed.LastRow().RowNumber(); r++)
                    {
                        for (int c = rangeUsed.FirstColumn().ColumnNumber(); c <= rangeUsed.LastColumn().ColumnNumber(); c++)
                        {
                            var cell = ws.Cell(r, c);
                            var cellValue = cell.GetString();
                            if (string.IsNullOrEmpty(cellValue)) continue;
                            if (!tagPattern.IsMatch(cellValue)) continue;

                            var result = tagPattern.Replace(cellValue, match =>
                            {
                                var tagName = match.Groups[1].Value;
                                return tagValues.TryGetValue(tagName, out var val) ? (val ?? string.Empty) : match.Value;
                            });

                            cell.SetValue(result);
                        }
                    }
                }

                workbook.Save();
            }

            return outputPath;
        }

        /// <summary>
        /// Generates a PDF document by overlaying tag values at positions defined in a .tags.json file.
        /// The original PDF is preserved; text is drawn on top at the specified coordinates.
        /// </summary>
        public string GeneratePdf(string templatePath, string outputPath, Dictionary<string, string> tagValues)
        {
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("PDF template not found.", templatePath);

            var tagMapPath = Path.ChangeExtension(templatePath, ".tags.json");
            if (!File.Exists(tagMapPath))
            {
                // No tag map — just copy
                File.Copy(templatePath, outputPath, true);
                return outputPath;
            }

            var json = File.ReadAllText(tagMapPath);
            var tagMap = JsonSerializer.Deserialize<PdfTagMap>(json);
            if (tagMap?.Placements == null || tagMap.Placements.Count == 0)
            {
                File.Copy(templatePath, outputPath, true);
                return outputPath;
            }

            // Open source PDF
            using var sourceDoc = PdfReader.Open(templatePath, PdfDocumentOpenMode.Import);
            using var outputDoc = new PdfDocument();

            // Copy all pages
            for (int i = 0; i < sourceDoc.PageCount; i++)
            {
                outputDoc.AddPage(sourceDoc.Pages[i]);
            }

            // Top-left string format so that (x, y) is the top-left corner of the text,
            // matching exactly where the user placed the tag marker in the editor.
            var topLeftFormat = new XStringFormat
            {
                Alignment = XStringAlignment.Near,
                LineAlignment = XLineAlignment.Near
            };

            // Draw tag values on each page
            foreach (var placement in tagMap.Placements)
            {
                if (placement.Page < 0 || placement.Page >= outputDoc.PageCount) continue;

                var tagName = placement.Tag;
                if (!tagValues.TryGetValue(tagName, out var value) || string.IsNullOrEmpty(value))
                    continue;

                var page = outputDoc.Pages[placement.Page];
                using var gfx = XGraphics.FromPdfPage(page);

                var fontSize = placement.FontSize > 0 ? placement.FontSize : 10;
                var fontFamily = !string.IsNullOrEmpty(placement.FontFamily) ? placement.FontFamily : "Arial";
                var font = new XFont(fontFamily, fontSize);

                var x = placement.X * page.Width.Point;
                var y = placement.Y * page.Height.Point;

                if (placement.MaxWidth > 0)
                {
                    var maxW = placement.MaxWidth;
                    var rect = new XRect(x, y, maxW, page.Height.Point - y);
                    var wrapFormat = new XStringFormat
                    {
                        Alignment = XStringAlignment.Near,
                        LineAlignment = XLineAlignment.Near
                    };
                    gfx.DrawString(value, font, XBrushes.Black, rect, wrapFormat);
                }
                else
                {
                    gfx.DrawString(value, font, XBrushes.Black, new XPoint(x, y), topLeftFormat);
                }
            }

            outputDoc.Save(outputPath);

            return outputPath;
        }

        /// <summary>
        /// Opens a file in its default application.
        /// </summary>
        public static void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }
}

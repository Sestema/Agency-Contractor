using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class TemplateService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly TagCatalogService? _tagCatalogService;
        private readonly FolderService _folderService;
        private const string LogFileName = "template-errors.log";
        private const string EmptyTemplateRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs22\pard\f0\par}";

        public TemplateService(AppSettingsService appSettingsService, FolderService folderService, TagCatalogService? tagCatalogService = null)
        {
            _appSettingsService = appSettingsService;
            _folderService = folderService;
            _tagCatalogService = tagCatalogService;
        }

        public List<TemplateEntry> GetTemplates(string firmName)
        {
            if (string.IsNullOrEmpty(firmName))
                return new List<TemplateEntry>();

            var templatesFolder = _folderService.GetTemplatesFolder(firmName);
            var templatesIndexFile = Path.Combine(templatesFolder, "index.json");

            if (!File.Exists(templatesIndexFile))
                return new List<TemplateEntry>();

            try
            {
                var indexEntries = SafeFileService.ReadJsonOrDefault(templatesIndexFile, new List<TemplateIndexEntry>());
                NormalizeTemplateIndexPaths(templatesFolder, indexEntries, templatesIndexFile);

                return indexEntries?.Select(e => new TemplateEntry
                {
                    Name = e.Name,
                    Format = e.Format,
                    FilePath = e.Path,
                    UpdatedAt = e.Updated
                }).ToList() ?? new List<TemplateEntry>();
            }
            catch
            {
                return new List<TemplateEntry>();
            }
        }

        public TemplateEntry? AddTemplate(string firmName, string templateName, string description, string format, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            // Create Template Folder
            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = Path.Combine(templatesRoot, safeName);

            // Handle duplicate names
            int counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }
            Directory.CreateDirectory(templateFolder);

            // Copy and Rename File
            var ext = Path.GetExtension(sourceFilePath).ToLower();
            var destFileName = $"template{ext}";
            var destPath = Path.Combine(templateFolder, destFileName);
            SafeFileService.CopyFile(sourceFilePath, destPath);

            // Create metadata.json
            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = format,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplate", ex); }
            }

            // Relative path for storage (uses the current templates folder name)
            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            // Create versions folder
            Directory.CreateDirectory(Path.Combine(templateFolder, "versions"));

            // Create preview folder
            Directory.CreateDirectory(Path.Combine(templateFolder, "preview"));

            return new TemplateEntry
            {
                Name = templateName,
                Format = format,
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        /// <summary>
        /// Creates a template without a source file (for DOCX — user will use built-in editor).
        /// </summary>
        public TemplateEntry? AddTemplateWithoutFile(string firmName, string templateName, string description, string format)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = Path.Combine(templatesRoot, safeName);

            int counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }
            Directory.CreateDirectory(templateFolder);

            // New DOCX templates start with an empty RTF so the editor can open immediately.
            var destFileName = "template.docx";
            SafeFileService.WriteTextAtomic(Path.Combine(templateFolder, "content.rtf"), EmptyTemplateRtf);

            // Create metadata.json
            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = format,
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            // Update index.json
            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index = new List<TemplateIndexEntry>();

            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex) { LoggingService.LogError("TemplateService.AddTemplateFromRtf", ex); }
            }

            var templatesFolderName = Path.GetFileName(templatesRoot);
            var relativePath = Path.Combine(templatesFolderName, Path.GetFileName(templateFolder), destFileName);

            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = format,
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            // Create subfolders
            Directory.CreateDirectory(Path.Combine(templateFolder, "versions"));
            Directory.CreateDirectory(Path.Combine(templateFolder, "preview"));

            return new TemplateEntry
            {
                Name = templateName,
                Format = format,
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        public TemplateEntry? AddTemplateFromDocxImport(string firmName, string templateName, string description, string sourceFilePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return null;
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("DOCX import file was not found.", sourceFilePath);

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            Directory.CreateDirectory(templatesRoot);

            var safeName = FolderService.NormalizeFolderName(templateName);
            var templateFolder = CreateUniqueTemplateFolder(templatesRoot, safeName);

            const string destFileName = "template.docx";
            SafeFileService.CopyFile(sourceFilePath, Path.Combine(templateFolder, destFileName));

            SaveImportedDocxEditorContent(sourceFilePath, templateFolder);

            var now = DateTime.Now;
            var metadata = new TemplateMetadata
            {
                Name = templateName,
                Format = "DOCX",
                Description = description,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };

            SafeFileService.WriteJsonAtomic(Path.Combine(templateFolder, "metadata.json"), metadata);

            var indexFile = Path.Combine(templatesRoot, "index.json");
            List<TemplateIndexEntry> index;
            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.AddTemplateFromDocxImport", ex);
                    index = new List<TemplateIndexEntry>();
                }
            }
            else
            {
                index = new List<TemplateIndexEntry>();
            }

            var relativePath = Path.Combine(Path.GetFileName(templatesRoot), Path.GetFileName(templateFolder), destFileName);
            var entry = new TemplateIndexEntry
            {
                Name = templateName,
                Format = "DOCX",
                Path = relativePath,
                Updated = now
            };
            index.Add(entry);

            SafeFileService.WriteJsonAtomic(indexFile, index);

            Directory.CreateDirectory(Path.Combine(templateFolder, "versions"));
            Directory.CreateDirectory(Path.Combine(templateFolder, "preview"));

            return new TemplateEntry
            {
                Name = templateName,
                Format = "DOCX",
                Description = description,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>()
            };
        }

        private static string CreateUniqueTemplateFolder(string templatesRoot, string safeName)
        {
            var templateFolder = Path.Combine(templatesRoot, safeName);
            var counter = 1;
            while (Directory.Exists(templateFolder))
            {
                templateFolder = Path.Combine(templatesRoot, $"{safeName}_{counter}");
                counter++;
            }

            Directory.CreateDirectory(templateFolder);
            return templateFolder;
        }

        private static void SaveImportedDocxEditorContent(string sourceFilePath, string templateFolder)
        {
            var document = BuildFlowDocumentFromDocx(sourceFilePath);
            var range = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd);

            using (var xamlStream = new MemoryStream())
            {
                range.Save(xamlStream, System.Windows.DataFormats.XamlPackage);
                SafeFileService.WriteBytesAtomic(Path.Combine(templateFolder, "content.xamlpackage"), xamlStream.ToArray());
            }

            using (var rtfStream = new MemoryStream())
            {
                range.Save(rtfStream, System.Windows.DataFormats.Rtf);
                SafeFileService.WriteBytesAtomic(Path.Combine(templateFolder, "content.rtf"), rtfStream.ToArray());
            }
        }

        private static System.Windows.Documents.FlowDocument BuildFlowDocumentFromDocx(string sourceFilePath)
        {
            var flowDocument = new System.Windows.Documents.FlowDocument
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                PagePadding = new System.Windows.Thickness(96)
            };

            using var document = WordprocessingDocument.Open(sourceFilePath, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body == null)
                return flowDocument;

            System.Windows.Documents.List? currentList = null;
            string? currentListKey = null;

            foreach (var child in body.ChildElements)
            {
                if (child is Paragraph paragraph)
                {
                    var listKey = GetListKey(paragraph);
                    if (!string.IsNullOrEmpty(listKey))
                    {
                        if (currentList == null || !string.Equals(currentListKey, listKey, StringComparison.Ordinal))
                        {
                            currentList = new System.Windows.Documents.List
                            {
                                MarkerStyle = GetTextMarkerStyle(document, paragraph),
                                Margin = new System.Windows.Thickness(0, 0, 0, 6),
                                Padding = new System.Windows.Thickness(24, 0, 0, 0)
                            };
                            flowDocument.Blocks.Add(currentList);
                            currentListKey = listKey;
                        }

                        currentList.ListItems.Add(new System.Windows.Documents.ListItem(CreateFlowParagraph(paragraph)));
                        continue;
                    }

                    currentList = null;
                    currentListKey = null;
                    flowDocument.Blocks.Add(CreateFlowParagraph(paragraph));
                }
                else if (child is Table table)
                {
                    currentList = null;
                    currentListKey = null;
                    flowDocument.Blocks.Add(CreateFlowTable(table));
                }
            }

            return flowDocument;
        }

        private static System.Windows.Documents.Paragraph CreateFlowParagraph(Paragraph paragraph)
        {
            var result = new System.Windows.Documents.Paragraph
            {
                Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };

            ApplyParagraphProperties(result, paragraph);

            foreach (var run in paragraph.Elements<Run>())
                AppendRunToParagraph(result, run);

            if (!result.Inlines.Any())
                result.Inlines.Add(new System.Windows.Documents.Run(string.Empty));

            return result;
        }

        private static System.Windows.Documents.Table CreateFlowTable(Table table)
        {
            var result = new System.Windows.Documents.Table
            {
                CellSpacing = 0,
                Margin = new System.Windows.Thickness(0, 8, 0, 8)
            };

            var maxColumns = table.Elements<TableRow>()
                .Select(row => row.Elements<TableCell>().Count())
                .DefaultIfEmpty(0)
                .Max();
            for (var i = 0; i < maxColumns; i++)
                result.Columns.Add(new System.Windows.Documents.TableColumn());

            var group = new System.Windows.Documents.TableRowGroup();
            result.RowGroups.Add(group);

            foreach (var sourceRow in table.Elements<TableRow>())
            {
                var row = new System.Windows.Documents.TableRow();
                group.Rows.Add(row);

                foreach (var sourceCell in sourceRow.Elements<TableCell>())
                {
                    var cell = new System.Windows.Documents.TableCell
                    {
                        BorderBrush = System.Windows.Media.Brushes.Gray,
                        BorderThickness = new System.Windows.Thickness(0.5),
                        Padding = new System.Windows.Thickness(6, 3, 6, 3)
                    };

                    var paragraphs = sourceCell.Elements<Paragraph>().ToList();
                    if (paragraphs.Count == 0)
                    {
                        var text = string.Join(" ", sourceCell.Descendants<Text>().Select(t => t.Text));
                        cell.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(text)));
                    }
                    else
                    {
                        foreach (var paragraph in paragraphs)
                            cell.Blocks.Add(CreateFlowParagraph(paragraph));
                    }

                    row.Cells.Add(cell);
                }
            }

            return result;
        }

        private static void ApplyParagraphProperties(System.Windows.Documents.Paragraph target, Paragraph source)
        {
            var props = source.ParagraphProperties;
            var alignment = props?.Justification?.Val?.Value;
            if (alignment == JustificationValues.Center)
                target.TextAlignment = System.Windows.TextAlignment.Center;
            else if (alignment == JustificationValues.Right)
                target.TextAlignment = System.Windows.TextAlignment.Right;
            else if (alignment == JustificationValues.Both)
                target.TextAlignment = System.Windows.TextAlignment.Justify;
            else
                target.TextAlignment = System.Windows.TextAlignment.Left;

            var ind = props?.Indentation;
            var left = TryParseTwip(ind?.Left?.Value, out var leftTwip) ? TwipsToDeviceIndependentPixels(leftTwip) : 0;
            var right = TryParseTwip(ind?.Right?.Value, out var rightTwip) ? TwipsToDeviceIndependentPixels(rightTwip) : 0;
            target.Margin = new System.Windows.Thickness(left, target.Margin.Top, right, target.Margin.Bottom);

            if (TryParseTwip(ind?.FirstLine?.Value, out var firstLine))
                target.TextIndent = TwipsToDeviceIndependentPixels(firstLine);
            else if (TryParseTwip(ind?.Hanging?.Value, out var hanging))
                target.TextIndent = -TwipsToDeviceIndependentPixels(hanging);

            var spacing = props?.SpacingBetweenLines;
            if (TryParseTwip(spacing?.Before?.Value, out var before) || TryParseTwip(spacing?.After?.Value, out var after))
            {
                target.Margin = new System.Windows.Thickness(
                    target.Margin.Left,
                    TryParseTwip(spacing?.Before?.Value, out before) ? TwipsToDeviceIndependentPixels(before) : target.Margin.Top,
                    target.Margin.Right,
                    TryParseTwip(spacing?.After?.Value, out after) ? TwipsToDeviceIndependentPixels(after) : target.Margin.Bottom);
            }
        }

        private static void AppendRunToParagraph(System.Windows.Documents.Paragraph paragraph, Run run)
        {
            foreach (var child in run.ChildElements)
            {
                if (child is Text text)
                {
                    var inline = new System.Windows.Documents.Run(text.Text);
                    ApplyRunProperties(inline, run.RunProperties);
                    paragraph.Inlines.Add(inline);
                }
                else if (child is TabChar)
                {
                    paragraph.Inlines.Add(new System.Windows.Documents.Run("\t"));
                }
                else if (child is Break)
                {
                    paragraph.Inlines.Add(new System.Windows.Documents.LineBreak());
                }
            }
        }

        private static void ApplyRunProperties(System.Windows.Documents.Run target, RunProperties? props)
        {
            if (props == null)
                return;

            if (props.Bold != null)
                target.FontWeight = System.Windows.FontWeights.Bold;
            if (props.Italic != null)
                target.FontStyle = System.Windows.FontStyles.Italic;
            if (props.Underline != null)
                target.TextDecorations = System.Windows.TextDecorations.Underline;
            if (int.TryParse(props.FontSize?.Val?.Value, out var halfPoints) && halfPoints > 0)
                target.FontSize = Math.Max(1, halfPoints / 2.0 * 96.0 / 72.0);

            var fontName = props.RunFonts?.Ascii?.Value
                ?? props.RunFonts?.HighAnsi?.Value
                ?? props.RunFonts?.ComplexScript?.Value;
            if (!string.IsNullOrWhiteSpace(fontName))
                target.FontFamily = new System.Windows.Media.FontFamily(fontName);
        }

        private static string GetListKey(Paragraph paragraph)
        {
            var numbering = paragraph.ParagraphProperties?.NumberingProperties;
            var numId = numbering?.NumberingId?.Val?.Value;
            if (numId == null)
                return string.Empty;

            var level = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
            return $"{numId}:{level}";
        }

        private static System.Windows.TextMarkerStyle GetTextMarkerStyle(WordprocessingDocument document, Paragraph paragraph)
        {
            var numbering = paragraph.ParagraphProperties?.NumberingProperties;
            var numId = numbering?.NumberingId?.Val?.Value;
            if (numId == null)
                return System.Windows.TextMarkerStyle.Decimal;

            var level = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
            var format = GetNumberingFormat(document, numId.Value, level)?.Value;
            if (Equals(format, NumberFormatValues.Bullet))
                return System.Windows.TextMarkerStyle.Disc;
            if (Equals(format, NumberFormatValues.LowerLetter))
                return System.Windows.TextMarkerStyle.LowerLatin;
            if (Equals(format, NumberFormatValues.UpperLetter))
                return System.Windows.TextMarkerStyle.UpperLatin;
            if (Equals(format, NumberFormatValues.LowerRoman))
                return System.Windows.TextMarkerStyle.LowerRoman;
            if (Equals(format, NumberFormatValues.UpperRoman))
                return System.Windows.TextMarkerStyle.UpperRoman;

            return System.Windows.TextMarkerStyle.Decimal;
        }

        private static double TwipsToDeviceIndependentPixels(int twips)
        {
            return twips / 15.0;
        }

        private static string BuildRtfFromDocx(string sourceFilePath)
        {
            var builder = new StringBuilder();
            builder.Append(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}{\f1 Arial;}{\f2 Calibri;}{\f3 Times New Roman;}}\fs22 ");

            using var document = WordprocessingDocument.Open(sourceFilePath, false);
            var body = document.MainDocumentPart?.Document?.Body;
            var listCounters = new Dictionary<string, int>();
            if (body != null)
            {
                foreach (var child in body.ChildElements)
                {
                    if (child is Paragraph paragraph)
                    {
                        AppendParagraphRtf(builder, document, paragraph, listCounters);
                    }
                    else if (child is Table table)
                    {
                        AppendTableAsParagraphs(builder, table);
                    }
                }
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendTableAsParagraphs(StringBuilder builder, Table table)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(cell => string.Join(" ", cell.Descendants<Text>().Select(t => t.Text)))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToList();

                if (cells.Count == 0)
                    continue;

                builder.Append(@"\pard ");
                AppendEscapedRtf(builder, string.Join("    |    ", cells));
                builder.Append(@"\par ");
            }
        }

        private static void AppendParagraphRtf(
            StringBuilder builder,
            WordprocessingDocument document,
            Paragraph paragraph,
            Dictionary<string, int> listCounters)
        {
            var props = paragraph.ParagraphProperties;
            builder.Append(@"\pard ");

            var alignment = props?.Justification?.Val?.Value;
            if (alignment == JustificationValues.Center)
                builder.Append(@"\qc ");
            else if (alignment == JustificationValues.Right)
                builder.Append(@"\qr ");
            else if (alignment == JustificationValues.Both)
                builder.Append(@"\qj ");
            else
                builder.Append(@"\ql ");

            var ind = props?.Indentation;
            if (TryParseTwip(ind?.Left?.Value, out var left))
                builder.Append(@"\li").Append(left).Append(' ');
            if (TryParseTwip(ind?.Right?.Value, out var right))
                builder.Append(@"\ri").Append(right).Append(' ');
            if (TryParseTwip(ind?.FirstLine?.Value, out var firstLine))
                builder.Append(@"\fi").Append(firstLine).Append(' ');
            if (TryParseTwip(ind?.Hanging?.Value, out var hanging))
                builder.Append(@"\fi-").Append(hanging).Append(' ');

            var numberingPrefix = GetNumberingPrefix(document, paragraph, listCounters);
            if (!string.IsNullOrEmpty(numberingPrefix))
            {
                builder.Append(@"\tx720 ");
                AppendEscapedRtf(builder, numberingPrefix);
                builder.Append(@"\tab ");
            }

            foreach (var run in paragraph.Elements<Run>())
                AppendRunRtf(builder, run);

            builder.Append(@"\par ");
        }

        private static void AppendRunRtf(StringBuilder builder, Run run)
        {
            var props = run.RunProperties;
            var bold = props?.Bold != null;
            var italic = props?.Italic != null;
            var underline = props?.Underline != null;
            var size = props?.FontSize?.Val?.Value;

            if (bold) builder.Append(@"\b ");
            if (italic) builder.Append(@"\i ");
            if (underline) builder.Append(@"\ul ");
            if (int.TryParse(size, out var halfPoints) && halfPoints > 0)
                builder.Append(@"\fs").Append(halfPoints).Append(' ');

            foreach (var text in run.Descendants<Text>())
                AppendEscapedRtf(builder, text.Text);

            foreach (var tab in run.Descendants<TabChar>())
                builder.Append(@"\tab ");

            foreach (var br in run.Descendants<Break>())
                builder.Append(@"\line ");

            if (underline) builder.Append(@"\ul0 ");
            if (italic) builder.Append(@"\i0 ");
            if (bold) builder.Append(@"\b0 ");
        }

        private static bool TryParseTwip(string? value, out int twip)
        {
            return int.TryParse(value, out twip);
        }

        private static string GetNumberingPrefix(
            WordprocessingDocument document,
            Paragraph paragraph,
            Dictionary<string, int> listCounters)
        {
            var numbering = paragraph.ParagraphProperties?.NumberingProperties;
            var numId = numbering?.NumberingId?.Val?.Value;
            if (numId == null)
                return string.Empty;

            var level = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
            var key = $"{numId}:{level}";
            listCounters.TryGetValue(key, out var current);
            current++;
            listCounters[key] = current;

            var format = GetNumberingFormat(document, numId.Value, level);
            var formatValue = format?.Value;
            if (Equals(formatValue, NumberFormatValues.Bullet))
                return "•";
            if (Equals(formatValue, NumberFormatValues.LowerLetter))
                return $"{ToLetters(current).ToLowerInvariant()}.";
            if (Equals(formatValue, NumberFormatValues.UpperLetter))
                return $"{ToLetters(current).ToUpperInvariant()}.";
            if (Equals(formatValue, NumberFormatValues.LowerRoman))
                return $"{ToRoman(current).ToLowerInvariant()}.";
            if (Equals(formatValue, NumberFormatValues.UpperRoman))
                return $"{ToRoman(current).ToUpperInvariant()}.";

            return $"{current}.";
        }

        private static EnumValue<NumberFormatValues>? GetNumberingFormat(WordprocessingDocument document, int numId, int level)
        {
            var numberingPart = document.MainDocumentPart?.NumberingDefinitionsPart;
            var numberingRoot = numberingPart?.Numbering;
            if (numberingRoot == null)
                return null;

            var instance = numberingRoot.Elements<NumberingInstance>()
                .FirstOrDefault(item => item.NumberID?.Value == numId);
            var abstractNumId = instance?.AbstractNumId?.Val?.Value;
            if (abstractNumId == null)
                return null;

            var abstractNumbering = numberingRoot.Elements<AbstractNum>()
                .FirstOrDefault(item => item.AbstractNumberId?.Value == abstractNumId.Value);
            var levelDefinition = abstractNumbering?.Elements<Level>()
                .FirstOrDefault(item => item.LevelIndex?.Value == level);

            return levelDefinition?.NumberingFormat?.Val;
        }

        private static string ToLetters(int number)
        {
            if (number <= 0)
                return number.ToString();

            var result = string.Empty;
            while (number > 0)
            {
                number--;
                result = (char)('A' + number % 26) + result;
                number /= 26;
            }

            return result;
        }

        private static string ToRoman(int number)
        {
            if (number <= 0)
                return number.ToString();

            var map = new[]
            {
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
                (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
                (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
            };
            var result = new StringBuilder();
            foreach (var (value, symbol) in map)
            {
                while (number >= value)
                {
                    result.Append(symbol);
                    number -= value;
                }
            }

            return result.ToString();
        }

        private static void AppendEscapedRtf(StringBuilder builder, string? text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            foreach (var ch in text)
            {
                if (ch == '\\') builder.Append(@"\\");
                else if (ch == '{') builder.Append(@"\{");
                else if (ch == '}') builder.Append(@"\}");
                else if (ch == '\n') builder.Append(@"\line ");
                else if (ch == '\r') { }
                else if (ch > 127) builder.Append(@"\u").Append((short)ch).Append('?');
                else builder.Append(ch);
            }
        }

        public string? DetectTemplateFormat(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".docx" => "DOCX",
                ".xlsx" => "XLSX",
                ".pdf" => "PDF",
                _ => null
            };
        }

        public bool TryValidateTemplateFile(string filePath, out string detectedFormat, out string error)
        {
            detectedFormat = string.Empty;
            error = string.Empty;

            if (!File.Exists(filePath))
            {
                error = "File not found.";
                return false;
            }

            var info = new FileInfo(filePath);
            if (info.Length == 0)
            {
                error = "File is empty.";
                return false;
            }

            var format = DetectTemplateFormat(filePath);
            if (format == null)
            {
                error = "Unsupported file format.";
                return false;
            }

            try
            {
                if (format == "PDF")
                {
                    using var stream = File.OpenRead(filePath);
                    var buffer = new byte[4];
                    var read = stream.Read(buffer, 0, buffer.Length);
                    var header = System.Text.Encoding.ASCII.GetString(buffer, 0, read);
                    if (!header.StartsWith("%PDF", StringComparison.Ordinal))
                    {
                        error = "Invalid PDF header.";
                        return false;
                    }
                }
                else if (format == "DOCX" || format == "XLSX")
                {
                    using var stream = File.OpenRead(filePath);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false);
                    if (!archive.Entries.Any())
                    {
                        error = "Document archive is empty.";
                        return false;
                    }
                }

                detectedFormat = format;
                return true;
            }
            catch (Exception ex)
            {
                error = $"Invalid file content: {ex.Message}";
                return false;
            }
        }

        public void LogTemplateError(string message)
        {
            LoggingService.LogError("TemplateService", message);
        }

        public string GetTemplateFullPath(string firmName, string relativePath)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            var companyFolder = _folderService.GetCompanyFolder(firmName);
            if (string.IsNullOrWhiteSpace(companyFolder))
                return string.Empty;

            var directPath = Path.Combine(companyFolder, relativePath);
            if (TemplatePathExists(directPath))
                return directPath;

            var templatesFolder = _folderService.GetTemplatesFolder(firmName);
            var currentFolderName = Path.GetFileName(templatesFolder);
            var normalizedRelativePath = NormalizeTemplateRelativePath(relativePath, currentFolderName);
            var normalizedPath = Path.Combine(companyFolder, normalizedRelativePath);
            if (TemplatePathExists(normalizedPath))
            {
                HealTemplateIndexPath(firmName, relativePath, normalizedRelativePath);
                return normalizedPath;
            }

            var segments = SplitRelativePath(relativePath);
            if (segments.Length >= 2)
            {
                var pathInsideTemplates = segments.Skip(1).ToArray();
                foreach (var folderName in FolderNames.AllTemplatesFolderNames)
                {
                    var candidatePath = Path.Combine(companyFolder, folderName, Path.Combine(pathInsideTemplates));
                    if (!TemplatePathExists(candidatePath))
                        continue;

                    var healedRelativePath = Path.Combine(folderName, Path.Combine(pathInsideTemplates));
                    HealTemplateIndexPath(firmName, relativePath, healedRelativePath);
                    return candidatePath;
                }
            }

            return normalizedPath;
        }

        public string GenerateDocumentFromTemplate(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath)) return string.Empty;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return string.Empty;

            var mdPath = Path.Combine(templateDirectory, "content.md");
            if (!File.Exists(mdPath)) return string.Empty;

            var content = SafeFileService.ReadAllText(mdPath);
            var tagMap = _tagCatalogService?.GetTagValueMap(firmName) ?? new Dictionary<string, string>();

            var result = System.Text.RegularExpressions.Regex.Replace(content, @"\$\{(.*?)\}", match =>
            {
                var key = match.Groups[1].Value;
                return tagMap.TryGetValue(key, out var value) ? value : match.Value;
            });

            var outputPath = Path.Combine(templateDirectory, $"generated_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            SafeFileService.WriteTextAtomic(outputPath, result);
            return outputPath;
        }

        public void RenameTemplate(string firmName, TemplateEntry template, string newName)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null || string.IsNullOrWhiteSpace(newName)) return;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            var indexFile = Path.Combine(templatesRoot, "index.json");

            if (File.Exists(indexFile))
            {
                var index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                var item = index.FirstOrDefault(x => x.Path == template.FilePath);
                if (item != null)
                {
                    item.Name = newName;
                    item.Updated = DateTime.Now;
                    SafeFileService.WriteJsonAtomic(indexFile, index);
                }
            }

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory != null)
            {
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    var meta = SafeFileService.ReadJson<TemplateMetadata>(metadataPath);
                    if (meta != null)
                    {
                        meta.Name = newName;
                        meta.UpdatedAt = DateTime.Now;
                        SafeFileService.WriteJsonAtomic(metadataPath, meta);
                    }
                }
            }

            template.Name = newName;
            template.UpdatedAt = DateTime.Now;
        }

        public TemplateEntry CopyTemplateToCompany(string sourceFirmName, TemplateEntry template, string targetFirmName, string newName)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath))
                throw new InvalidOperationException("Root folder is not configured.");
            if (template == null)
                throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Template name is required.", nameof(newName));

            var targetTemplatesRoot = _folderService.GetTemplatesFolder(targetFirmName);
            Directory.CreateDirectory(targetTemplatesRoot);

            var sourceFullPath = GetTemplateFullPath(sourceFirmName, template.FilePath);
            var sourceTemplateDirectory = Path.GetDirectoryName(sourceFullPath);
            if (string.IsNullOrEmpty(sourceTemplateDirectory) || !Directory.Exists(sourceTemplateDirectory))
                throw new DirectoryNotFoundException("Source template folder was not found.");

            var safeName = FolderService.NormalizeFolderName(newName);
            var targetTemplateDirectory = Path.Combine(targetTemplatesRoot, safeName);
            var counter = 1;
            while (Directory.Exists(targetTemplateDirectory))
            {
                targetTemplateDirectory = Path.Combine(targetTemplatesRoot, $"{safeName}_{counter}");
                counter++;
            }

            CopyDirectory(sourceTemplateDirectory, targetTemplateDirectory);

            var now = DateTime.Now;
            var templateFileName = Path.GetFileName(template.FilePath);
            var metadataPath = Path.Combine(targetTemplateDirectory, "metadata.json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var metadata = SafeFileService.ReadJsonOrDefault(metadataPath, new TemplateMetadata());
                    metadata.Name = newName.Trim();
                    metadata.Format = string.IsNullOrWhiteSpace(metadata.Format) ? template.Format : metadata.Format;
                    metadata.CreatedAt = now;
                    metadata.UpdatedAt = now;
                    SafeFileService.WriteJsonAtomic(metadataPath, metadata);
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.CopyTemplateToCompany.Metadata", ex);
                }
            }

            var relativePath = Path.Combine(Path.GetFileName(targetTemplatesRoot), Path.GetFileName(targetTemplateDirectory), templateFileName);
            var indexFile = Path.Combine(targetTemplatesRoot, "index.json");
            List<TemplateIndexEntry> index;
            if (File.Exists(indexFile))
            {
                try
                {
                    index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                }
                catch
                {
                    index = new List<TemplateIndexEntry>();
                }
            }
            else
            {
                index = new List<TemplateIndexEntry>();
            }

            index.Add(new TemplateIndexEntry
            {
                Name = newName.Trim(),
                Format = template.Format,
                Path = relativePath,
                Updated = now
            });

            SafeFileService.WriteJsonAtomic(indexFile, index);

            return new TemplateEntry
            {
                Name = newName.Trim(),
                Description = template.Description,
                Format = template.Format,
                FilePath = relativePath,
                CreatedAt = now,
                UpdatedAt = now,
                TagsUsed = new List<string>(template.TagsUsed ?? new List<string>())
            };
        }

        public async Task DeleteTemplateAsync(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var templatesRoot = _folderService.GetTemplatesFolder(firmName);
            var indexFile = Path.Combine(templatesRoot, "index.json");

            // 1. Delete the template folder FIRST
            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(templateDirectory) && Directory.Exists(templateDirectory))
            {
                var deleted = await TryDeleteTemplateDirectoryAsync(templateDirectory);
                if (!deleted && Directory.Exists(templateDirectory))
                {
                    var message = $"Folder still exists after delete, scheduling deferred cleanup: {templateDirectory}";
                    LoggingService.LogWarning("TemplateService.DeleteTemplate", message);
                    await PendingCleanupService.EnqueueAsync(templateDirectory, "template-delete-folder");
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            await System.Threading.Tasks.Task.Delay(15000);
                            if (await TryDeleteTemplateDirectoryAsync(templateDirectory))
                                await PendingCleanupService.RemoveAsync(templateDirectory);
                        }
                        catch (Exception ex)
                        {
                            LoggingService.LogWarning("TemplateService.DeleteTemplateCleanup", ex.Message);
                        }
                    });
                }
            }

            // 2. Remove from index.json AFTER folder is deleted
            if (File.Exists(indexFile))
            {
                try
                {
                    var index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());

                    var itemToRemove = index.FirstOrDefault(x => x.Path == template.FilePath);
                    if (itemToRemove != null)
                    {
                        index.Remove(itemToRemove);
                        SafeFileService.WriteJsonAtomic(indexFile, index);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("TemplateService.DeleteTemplate", ex);
                    throw new Exception("Failed to update template index.", ex);
                }
            }
        }

        private static async Task<bool> TryDeleteTemplateDirectoryAsync(string templateDirectory)
        {
            if (!Directory.Exists(templateDirectory))
                return true;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(300);

            NormalizeAttributesRecursive(templateDirectory);

            if (await TryBulkDeleteTemplateDirectoryAsync(templateDirectory))
                return true;

            TryDeleteTemplateFilesIndividually(templateDirectory, logWarnings: false);
            TryRemoveEmptyTemplateDirectories(templateDirectory, logWarnings: false);

            if (Directory.Exists(templateDirectory))
            {
                if (TryForceDeleteTemplateDirectory(templateDirectory))
                    return true;

                LoggingService.LogWarning("TemplateService.TryDeleteTemplateDirectory",
                    $"Template folder cleanup deferred because Windows still denies access: {templateDirectory}");
            }

            return !Directory.Exists(templateDirectory);
        }

        private static async Task<bool> TryBulkDeleteTemplateDirectoryAsync(string templateDirectory)
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(templateDirectory))
                        return true;

                    NormalizeAttributesRecursive(templateDirectory);

                    Directory.Delete(templateDirectory, true);
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await Task.Delay(400 * (attempt + 1));
                }
            }

            if (Directory.Exists(templateDirectory) && lastError != null)
                LoggingService.LogInfo("TemplateService.TryBulkDeleteTemplateDirectory", $"Bulk delete fallback needed for '{templateDirectory}': {lastError.Message}");

            return false;
        }

        private static void TryDeleteTemplateFilesIndividually(string templateDirectory, bool logWarnings = true)
        {
            try
            {
                foreach (var file in Directory.GetFiles(templateDirectory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        SafeFileService.DeleteFile(file);
                    }
                    catch (Exception ex)
                    {
                        if (logWarnings)
                            LoggingService.LogWarning("TemplateService.TryDeleteTemplateFilesIndividually", $"Cannot delete {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (logWarnings)
                    LoggingService.LogWarning("TemplateService.TryDeleteTemplateFilesIndividually", ex.Message);
            }
        }

        private static void TryRemoveEmptyTemplateDirectories(string templateDirectory, bool logWarnings = true)
        {
            if (!Directory.Exists(templateDirectory))
                return;

            foreach (var subDirectory in Directory.GetDirectories(templateDirectory))
                TryRemoveEmptyTemplateDirectories(subDirectory, logWarnings);

            try
            {
                if (Directory.Exists(templateDirectory)
                    && Directory.GetFiles(templateDirectory).Length == 0
                    && Directory.GetDirectories(templateDirectory).Length == 0)
                {
                    Directory.Delete(templateDirectory, false);
                }
            }
            catch (Exception ex)
            {
                if (logWarnings)
                    LoggingService.LogWarning("TemplateService.TryRemoveEmptyTemplateDirectories", $"Cannot delete {templateDirectory}: {ex.Message}");
            }
        }

        private static bool TryForceDeleteTemplateDirectory(string templateDirectory)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rd /s /q \"{templateDirectory}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(10000);
                    if (!Directory.Exists(templateDirectory))
                    {
                        LoggingService.LogInfo("TemplateService", $"Force-deleted via cmd: {templateDirectory}");
                        return true;
                    }

                    var error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(error))
                        LoggingService.LogWarning("TemplateService.TryForceDeleteTemplateDirectory", error.Trim());
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateService.TryForceDeleteTemplateDirectory", $"Force delete failed: {ex.Message}");
            }

            return !Directory.Exists(templateDirectory);
        }

        private static void NormalizeAttributesRecursive(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            try
            {
                foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    catch
                    {
                        // OneDrive may temporarily deny attribute changes; deletion fallbacks handle that.
                    }
                }

                foreach (var subDirectory in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.SetAttributes(subDirectory, FileAttributes.Normal);
                    }
                    catch
                    {
                        // Keep deletion moving even when a synced folder is temporarily locked.
                    }
                }

                File.SetAttributes(directory, FileAttributes.Normal);
            }
            catch
            {
                // Best-effort cleanup only; the next delete path will decide the final result.
            }
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            var sourceInfo = new DirectoryInfo(sourceDirectory);
            if (!sourceInfo.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");

            Directory.CreateDirectory(destinationDirectory);

            foreach (var file in sourceInfo.GetFiles())
            {
                var targetFile = Path.Combine(destinationDirectory, file.Name);
                file.CopyTo(targetFile, false);
            }

            foreach (var directory in sourceInfo.GetDirectories())
            {
                var targetSubdirectory = Path.Combine(destinationDirectory, directory.Name);
                CopyDirectory(directory.FullName, targetSubdirectory);
            }
        }

        public void SaveTagPositions(string firmName, TemplateEntry template, List<TagPosition> positions)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return;

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            SafeFileService.WriteJsonAtomic(path, positions ?? new List<TagPosition>());
        }

        public List<TagPosition> LoadTagPositions(string firmName, TemplateEntry template)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return new List<TagPosition>();

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);
            if (templateDirectory == null || !Directory.Exists(templateDirectory)) return new List<TagPosition>();

            var path = Path.Combine(templateDirectory, "tag_positions.json");
            if (!File.Exists(path)) return new List<TagPosition>();

            try
            {
                return SafeFileService.ReadJsonOrDefault(path, new List<TagPosition>());
            }
            catch
            {
                return new List<TagPosition>();
            }
        }

        public void SaveTemplateContent(string firmName, TemplateEntry template, string markdownContent, List<string> tagsUsed)
        {
            if (string.IsNullOrEmpty(_folderService.RootPath) || template == null) return;

            var fullPath = GetTemplateFullPath(firmName, template.FilePath);
            var templateDirectory = Path.GetDirectoryName(fullPath);

            if (templateDirectory != null && Directory.Exists(templateDirectory))
            {
                // Save Markdown
                var mdPath = Path.Combine(templateDirectory, "content.md");
                SafeFileService.WriteTextAtomic(mdPath, markdownContent);

                // Update Metadata
                var metadataPath = Path.Combine(templateDirectory, "metadata.json");
                if (File.Exists(metadataPath))
                {
                    try
                    {
                        var metadata = SafeFileService.ReadJson<TemplateMetadata>(metadataPath);
                        if (metadata != null)
                        {
                            metadata.UpdatedAt = DateTime.Now;
                            metadata.TagsUsed = tagsUsed ?? new List<string>();

                            SafeFileService.WriteJsonAtomic(metadataPath, metadata);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("TemplateService.SaveTagPositions.Metadata", ex); }
                }

                // Update Index timestamp
                var templatesRoot = _folderService.GetTemplatesFolder(firmName);
                var indexFile = Path.Combine(templatesRoot, "index.json");
                if (File.Exists(indexFile))
                {
                    try
                    {
                        var index = SafeFileService.ReadJson<List<TemplateIndexEntry>>(indexFile);
                        var entry = index?.FirstOrDefault(x => x.Path == template.FilePath);
                        if (entry != null)
                        {
                            entry.Updated = DateTime.Now;
                            SafeFileService.WriteJsonAtomic(indexFile, index);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("TemplateService.SaveTagPositions.Index", ex); }
                }
            }
        }

        private static string[] SplitRelativePath(string relativePath)
        {
            return (relativePath ?? string.Empty)
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizeTemplateRelativePath(string relativePath, string? currentTemplatesFolderName)
        {
            var segments = SplitRelativePath(relativePath);
            if (segments.Length == 0)
                return relativePath ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(currentTemplatesFolderName)
                && FolderNames.AllTemplatesFolderNames.Contains(segments[0], StringComparer.OrdinalIgnoreCase))
            {
                segments[0] = currentTemplatesFolderName;
            }

            return Path.Combine(segments);
        }

        private static bool TemplatePathExists(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            if (File.Exists(fullPath))
                return true;

            var templateDirectory = Path.GetDirectoryName(fullPath);
            return !string.IsNullOrWhiteSpace(templateDirectory) && Directory.Exists(templateDirectory);
        }

        private void NormalizeTemplateIndexPaths(string templatesFolder, List<TemplateIndexEntry>? indexEntries, string indexFile)
        {
            if (indexEntries == null || indexEntries.Count == 0)
                return;

            var currentFolderName = Path.GetFileName(templatesFolder);
            var changed = false;

            foreach (var entry in indexEntries)
            {
                var normalizedPath = NormalizeTemplateRelativePath(entry.Path, currentFolderName);
                if (string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
                    continue;

                entry.Path = normalizedPath;
                changed = true;
            }

            if (changed)
                SafeFileService.WriteJsonAtomic(indexFile, indexEntries);
        }

        private void HealTemplateIndexPath(string firmName, string oldRelativePath, string newRelativePath)
        {
            if (string.IsNullOrWhiteSpace(oldRelativePath)
                || string.IsNullOrWhiteSpace(newRelativePath)
                || string.Equals(oldRelativePath, newRelativePath, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                var templatesRoot = _folderService.GetTemplatesFolder(firmName);
                var indexFile = Path.Combine(templatesRoot, "index.json");
                if (!File.Exists(indexFile))
                    return;

                var index = SafeFileService.ReadJsonOrDefault(indexFile, new List<TemplateIndexEntry>());
                var changed = false;
                foreach (var entry in index.Where(e => string.Equals(e.Path, oldRelativePath, StringComparison.Ordinal)))
                {
                    entry.Path = newRelativePath;
                    changed = true;
                }

                if (changed)
                    SafeFileService.WriteJsonAtomic(indexFile, index);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TemplateService.HealTemplateIndexPath", ex.Message);
            }
        }
    }
}

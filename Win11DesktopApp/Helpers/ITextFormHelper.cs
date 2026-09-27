using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using iTextSharp.text.pdf;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Helpers
{
    public sealed class PdfFormFillIssue
    {
        public string FieldName { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;
    }

    public sealed class PdfFormFillResult
    {
        public bool Success { get; init; }
        public List<PdfFormFillIssue> FailedFields { get; init; } = new();
    }

    public static class ITextFormHelper
    {
        private static readonly object FontSync = new();
        private static BaseFont? _unicodeFont;
        private static bool _unicodeFontResolved;

        private static readonly string[] UnicodeFontCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "calibri.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialuni.ttf")
        };

        public static IReadOnlyList<PdfFormFieldBinding> ReadFieldBindings(string pdfPath)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return Array.Empty<PdfFormFieldBinding>();

            PdfReader? reader = null;
            try
            {
                reader = new PdfReader(pdfPath);
                var form = reader.AcroFields;
                if (form?.Fields == null || form.Fields.Count == 0)
                {
                    LoggingService.LogWarning(
                        "ITextFormHelper.ReadFieldBindings",
                        $"PDF has no AcroForm fields: {pdfPath}");
                    return Array.Empty<PdfFormFieldBinding>();
                }

                var pageText = new Dictionary<int, List<TextChunk>>();
                var detected = new List<PdfFormFieldBinding>();
                foreach (var key in form.Fields.Keys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    var binding = new PdfFormFieldBinding
                    {
                        FieldName = key,
                        DecodedFieldName = DecodePdfFieldName(key),
                        FieldType = MapFieldType(form.GetFieldType(key))
                    };

                    var tooltip = ReadTooltip(form, key);
                    if (TryReadFirstWidget(reader, form, key, out var pageIndex, out var left, out var bottom, out var width, out var height))
                    {
                        binding.Page = pageIndex;
                        binding.X = Math.Round(left, 2);
                        binding.Y = Math.Round(bottom, 2);
                        binding.Width = Math.Round(width, 2);
                        binding.Height = Math.Round(height, 2);
                        binding.NearbyText = !string.IsNullOrWhiteSpace(tooltip)
                            ? TrimNearby(tooltip)
                            : TrimNearby(ExtractNearbyText(reader, pageText, pageIndex, left, bottom, width, height));
                    }
                    else if (!string.IsNullOrWhiteSpace(tooltip))
                    {
                        binding.NearbyText = TrimNearby(tooltip);
                    }

                    detected.Add(binding);
                }

                return detected
                    .GroupBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ITextFormHelper.ReadFieldBindings", ex);
                return Array.Empty<PdfFormFieldBinding>();
            }
            finally
            {
                try { reader?.Close(); } catch { }
            }
        }

        public static PdfFormFillResult TryFillFormFields(
            string templatePath,
            string outputPath,
            IEnumerable<PdfFormFieldBinding> bindings,
            Dictionary<string, string> tagValues)
        {
            var failedFields = new List<PdfFormFillIssue>();
            if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                return new PdfFormFillResult { Success = false, FailedFields = failedFields };

            PdfReader? reader = null;
            PdfStamper? stamper = null;
            FileStream? output = null;
            var createdOutput = false;
            var succeeded = false;
            try
            {
                reader = new PdfReader(templatePath);
                var probe = reader.AcroFields;
                if (probe?.Fields == null || probe.Fields.Count == 0)
                {
                    LoggingService.LogWarning(
                        "ITextFormHelper.TryFillFormFields",
                        $"PDF has no AcroForm fields: {templatePath}");
                    return new PdfFormFillResult { Success = false, FailedFields = failedFields };
                }

                output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                createdOutput = true;
                stamper = new PdfStamper(reader, output);
                stamper.Writer.CloseStream = false;
                reader = null;
                stamper.FormFlattening = false;

                var form = stamper.AcroFields;
                form.GenerateAppearances = true;
                var lookup = BuildFieldLookup(form);
                var unicodeFont = GetUnicodeFont();

                foreach (var binding in bindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.FieldName) || string.IsNullOrWhiteSpace(binding.TemplateText))
                        continue;

                    if (!TryResolveFieldKey(lookup, binding.FieldName, out var fieldKey))
                        continue;

                    var resolvedValue = PdfInlineTextResolver.ResolveTemplate(binding.TemplateText, tagValues) ?? string.Empty;
                    TryApplyFieldValue(form, fieldKey, resolvedValue, unicodeFont, failedFields);
                }

                stamper.Close();
                stamper = null;
                succeeded = true;
                return new PdfFormFillResult
                {
                    Success = true,
                    FailedFields = failedFields
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ITextFormHelper.TryFillFormFields", ex);
                return new PdfFormFillResult { Success = false, FailedFields = failedFields };
            }
            finally
            {
                try { stamper?.Close(); } catch { }
                try { output?.Dispose(); } catch { }
                try { reader?.Close(); } catch { }
                if (!succeeded && createdOutput)
                {
                    try
                    {
                        if (File.Exists(outputPath))
                            File.Delete(outputPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void TryApplyFieldValue(
            AcroFields form,
            string fieldKey,
            string value,
            BaseFont? unicodeFont,
            List<PdfFormFillIssue> failedFields)
        {
            try
            {
                var fieldType = form.GetFieldType(fieldKey);
                if (fieldType is AcroFields.FIELD_TYPE_PUSHBUTTON or AcroFields.FIELD_TYPE_SIGNATURE)
                    return;

                if (fieldType is AcroFields.FIELD_TYPE_CHECKBOX or AcroFields.FIELD_TYPE_RADIOBUTTON)
                {
                    ApplyToggleValue(form, fieldKey, value);
                    return;
                }

                if (fieldType is AcroFields.FIELD_TYPE_COMBO or AcroFields.FIELD_TYPE_LIST)
                {
                    ApplyChoiceValue(form, fieldKey, value);
                    return;
                }

                if (unicodeFont != null)
                    form.SetFieldProperty(fieldKey, "textfont", unicodeFont, null);

                if (form.SetField(fieldKey, value))
                    return;

                if (TrySetSanitizedValue(form, fieldKey, value))
                    return;

                failedFields.Add(new PdfFormFillIssue
                {
                    FieldName = fieldKey,
                    Value = value,
                    Message = "SetField returned false."
                });
                LoggingService.LogWarning(
                    "ITextFormHelper.TryApplyFieldValue",
                    $"Skipping field '{fieldKey}'.");
            }
            catch (Exception ex)
            {
                if (TrySetSanitizedValue(form, fieldKey, value))
                    return;

                LoggingService.LogWarning(
                    "ITextFormHelper.TryApplyFieldValue",
                    $"Skipping field '{fieldKey}': {ex.Message}");
                failedFields.Add(new PdfFormFillIssue
                {
                    FieldName = fieldKey,
                    Value = value,
                    Message = ex.Message
                });
            }
        }

        private static void ApplyChoiceValue(AcroFields form, string fieldKey, string value)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return;

            if (TrySelectChoiceOption(form, fieldKey, trimmed, simplifyOptions: false))
                return;

            var sanitized = SimplifyPdfUnsafeCharacters(trimmed);
            if (!string.Equals(sanitized, trimmed, StringComparison.Ordinal)
                && TrySelectChoiceOption(form, fieldKey, sanitized, simplifyOptions: true))
            {
                return;
            }

            LoggingService.LogWarning(
                "ITextFormHelper.ApplyChoiceValue",
                $"No list option matched '{trimmed}' for field '{fieldKey}'.");
        }

        private static bool TrySelectChoiceOption(AcroFields form, string fieldKey, string wanted, bool simplifyOptions)
        {
            var displays = form.GetListOptionDisplay(fieldKey) ?? Array.Empty<string>();
            var exports = form.GetListOptionExport(fieldKey) ?? Array.Empty<string>();
            var count = Math.Max(displays.Length, exports.Length);
            for (var i = 0; i < count; i++)
            {
                var display = i < displays.Length ? displays[i] : null;
                var export = i < exports.Length ? exports[i] : null;
                var displayKey = simplifyOptions ? SimplifyPdfUnsafeCharacters(display ?? string.Empty) : display;
                var exportKey = simplifyOptions ? SimplifyPdfUnsafeCharacters(export ?? string.Empty) : export;
                if (!OptionEquals(displayKey, wanted) && !OptionEquals(exportKey, wanted))
                    continue;

                var exportValue = string.IsNullOrWhiteSpace(export) ? display : export;
                var displayValue = string.IsNullOrWhiteSpace(display) ? exportValue : display;
                if (string.IsNullOrWhiteSpace(exportValue) || string.IsNullOrWhiteSpace(displayValue))
                    return false;

                return form.SetField(fieldKey, exportValue, displayValue);
            }

            return false;
        }

        private static bool OptionEquals(string? option, string wanted)
        {
            return !string.IsNullOrWhiteSpace(option)
                && string.Equals(option.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyToggleValue(AcroFields form, string fieldKey, string value)
        {
            var states = form.GetAppearanceStates(fieldKey) ?? Array.Empty<string>();
            if (states.Length == 0)
            {
                form.SetField(fieldKey, IsTruthyValue(value) ? "Yes" : "Off");
                return;
            }

            var trimmed = value.Trim();
            var explicitState = states.FirstOrDefault(state => string.Equals(state, trimmed, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(explicitState))
            {
                form.SetField(fieldKey, explicitState);
                return;
            }

            if (IsTruthyValue(value))
            {
                var onState = states.FirstOrDefault(state => !IsOffState(state));
                form.SetField(fieldKey, string.IsNullOrWhiteSpace(onState) ? "Yes" : onState);
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                var offState = states.FirstOrDefault(IsOffState) ?? "Off";
                form.SetField(fieldKey, offState);
                return;
            }

            form.SetField(fieldKey, trimmed);
        }

        private static bool TrySetSanitizedValue(AcroFields form, string fieldKey, string value)
        {
            var sanitized = SimplifyPdfUnsafeCharacters(value);
            if (string.Equals(sanitized, value, StringComparison.Ordinal))
                return false;

            try
            {
                if (!form.SetField(fieldKey, sanitized))
                    return false;

                LoggingService.LogWarning(
                    "ITextFormHelper.TryApplyFieldValue",
                    $"Field '{fieldKey}' used sanitized fallback value '{sanitized}' after font encoding failure.");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(
                    "ITextFormHelper.TryApplyFieldValue",
                    $"Sanitized fallback also failed for field '{fieldKey}': {ex.Message}");
                return false;
            }
        }

        private static Dictionary<string, string> BuildFieldLookup(AcroFields form)
        {
            var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in form.Fields.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                lookup.TryAdd(key, key);
                var decoded = DecodePdfFieldName(key);
                if (!string.IsNullOrWhiteSpace(decoded))
                    lookup.TryAdd(decoded, key);
            }

            return lookup;
        }

        private static bool TryResolveFieldKey(Dictionary<string, string> lookup, string requested, out string fieldKey)
        {
            if (lookup.TryGetValue(requested, out fieldKey!))
                return true;

            var decoded = DecodePdfFieldName(requested);
            if (!string.IsNullOrWhiteSpace(decoded) && lookup.TryGetValue(decoded, out fieldKey!))
                return true;

            fieldKey = string.Empty;
            return false;
        }

        private static string? ReadTooltip(AcroFields form, string fieldKey)
        {
            var item = form.GetFieldItem(fieldKey);
            if (item == null || item.Size <= 0)
                return null;

            return item.GetMerged(0)?.GetAsString(PdfName.Tu)?.ToUnicodeString();
        }

        private static bool TryReadFirstWidget(
            PdfReader reader,
            AcroFields form,
            string fieldKey,
            out int pageIndex,
            out float left,
            out float bottom,
            out float width,
            out float height)
        {
            pageIndex = -1;
            left = bottom = width = height = 0;

            var positions = form.GetFieldPositions(fieldKey);
            if (positions == null || positions.Length < 5)
                return false;

            var pageNumber = (int)positions[0];
            if (pageNumber <= 0 || pageNumber > reader.NumberOfPages)
                return false;

            var x1 = positions[1];
            var y1 = positions[2];
            var x2 = positions[3];
            var y2 = positions[4];
            left = Math.Min(x1, x2);
            bottom = Math.Min(y1, y2);
            width = Math.Abs(x2 - x1);
            height = Math.Abs(y2 - y1);
            if (width <= 0 || height <= 0)
                return false;

            pageIndex = pageNumber - 1;
            return true;
        }

        private static string ExtractNearbyText(
            PdfReader reader,
            Dictionary<int, List<TextChunk>> pageText,
            int pageIndex,
            float left,
            float bottom,
            float width,
            float height)
        {
            if (pageIndex < 0 || pageIndex >= reader.NumberOfPages)
                return string.Empty;

            if (!pageText.TryGetValue(pageIndex, out var chunks))
            {
                chunks = ReadPageTextChunks(reader, pageIndex + 1);
                pageText[pageIndex] = chunks;
            }

            if (chunks.Count == 0)
                return string.Empty;

            var pageSize = reader.GetPageSize(pageIndex + 1);
            var regionLeft = Math.Max(0, left - 180);
            var regionBottom = Math.Max(0, bottom - 18);
            var regionRight = Math.Min(pageSize.Width, left + width + 40);
            var regionTop = Math.Min(pageSize.Height, bottom + height + 18);

            var text = string.Join(" ", chunks
                .Where(chunk => chunk.X >= regionLeft && chunk.X <= regionRight && chunk.Y >= regionBottom && chunk.Y <= regionTop)
                .OrderByDescending(chunk => chunk.Y)
                .ThenBy(chunk => chunk.X)
                .Select(chunk => chunk.Text));
            return LooksReadable(text) ? text : string.Empty;
        }

        private static List<TextChunk> ReadPageTextChunks(PdfReader reader, int pageNumber)
        {
            var chunks = new List<TextChunk>();
            try
            {
                var content = reader.GetPageContent(pageNumber);
                if (content == null || content.Length == 0)
                    return chunks;

                var parser = new PdfContentParser(new PrTokeniser(new RandomAccessFileOrArray(content)));
                var operands = new List<PdfObject>();
                var ctm = PdfMatrix.Identity;
                var textMatrix = PdfMatrix.Identity;
                var lineMatrix = PdfMatrix.Identity;
                var ctmStack = new Stack<PdfMatrix>();
                float leading = 0;

                while (true)
                {
                    parser.Parse(operands);
                    if (operands.Count == 0)
                        break;

                    var command = operands[operands.Count - 1].ToString();
                    switch (command)
                    {
                        case "q":
                            ctmStack.Push(ctm);
                            break;
                        case "Q":
                            if (ctmStack.Count > 0)
                                ctm = ctmStack.Pop();
                            break;
                        case "cm" when operands.Count >= 7:
                            ctm = PdfMatrix.Concat(ReadMatrix(operands), ctm);
                            break;
                        case "BT":
                            textMatrix = PdfMatrix.Identity;
                            lineMatrix = PdfMatrix.Identity;
                            break;
                        case "Tm" when operands.Count >= 7:
                            textMatrix = ReadMatrix(operands);
                            lineMatrix = textMatrix;
                            break;
                        case "Td" when operands.Count >= 3:
                            lineMatrix = PdfMatrix.Concat(PdfMatrix.Translate(Number(operands[0]), Number(operands[1])), lineMatrix);
                            textMatrix = lineMatrix;
                            break;
                        case "TD" when operands.Count >= 3:
                            leading = -Number(operands[1]);
                            lineMatrix = PdfMatrix.Concat(PdfMatrix.Translate(Number(operands[0]), Number(operands[1])), lineMatrix);
                            textMatrix = lineMatrix;
                            break;
                        case "T*":
                            lineMatrix = PdfMatrix.Concat(PdfMatrix.Translate(0, -leading), lineMatrix);
                            textMatrix = lineMatrix;
                            break;
                        case "TL" when operands.Count >= 2:
                            leading = Number(operands[0]);
                            break;
                        case "Tj" when operands.Count >= 2:
                            AddTextChunk(chunks, operands[0], ctm, textMatrix);
                            break;
                        case "'" when operands.Count >= 2:
                            lineMatrix = PdfMatrix.Concat(PdfMatrix.Translate(0, -leading), lineMatrix);
                            textMatrix = lineMatrix;
                            AddTextChunk(chunks, operands[0], ctm, textMatrix);
                            break;
                        case "\"" when operands.Count >= 4:
                            lineMatrix = PdfMatrix.Concat(PdfMatrix.Translate(0, -leading), lineMatrix);
                            textMatrix = lineMatrix;
                            AddTextChunk(chunks, operands[2], ctm, textMatrix);
                            break;
                        case "TJ" when operands.Count >= 2 && operands[0] is PdfArray array:
                            AddTextArray(chunks, array, ctm, textMatrix);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ITextFormHelper.ReadPageTextChunks", ex.Message);
            }

            return chunks;
        }

        private static void AddTextArray(List<TextChunk> chunks, PdfArray array, PdfMatrix ctm, PdfMatrix textMatrix)
        {
            var builder = new StringBuilder();
            foreach (var item in array.ArrayList)
            {
                if (item is PdfString text)
                    builder.Append(text.ToUnicodeString());
            }

            AddTextChunk(chunks, builder.ToString(), ctm, textMatrix);
        }

        private static void AddTextChunk(List<TextChunk> chunks, PdfObject textObject, PdfMatrix ctm, PdfMatrix textMatrix)
        {
            if (textObject is not PdfString pdfString)
                return;

            AddTextChunk(chunks, pdfString.ToUnicodeString(), ctm, textMatrix);
        }

        private static void AddTextChunk(List<TextChunk> chunks, string? text, PdfMatrix ctm, PdfMatrix textMatrix)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var point = ctm.Transform(textMatrix.E, textMatrix.F);
            chunks.Add(new TextChunk(text.Trim(), point.X, point.Y));
        }

        private static PdfMatrix ReadMatrix(IList<PdfObject> operands)
        {
            return new PdfMatrix(
                Number(operands[0]),
                Number(operands[1]),
                Number(operands[2]),
                Number(operands[3]),
                Number(operands[4]),
                Number(operands[5]));
        }

        private static float Number(PdfObject value)
        {
            if (value is PdfNumber number)
                return number.FloatValue;

            return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
        }

        private static BaseFont? GetUnicodeFont()
        {
            if (_unicodeFontResolved)
                return _unicodeFont;

            lock (FontSync)
            {
                if (_unicodeFontResolved)
                    return _unicodeFont;

                foreach (var path in UnicodeFontCandidates)
                {
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        _unicodeFont = BaseFont.CreateFont(path, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
                        break;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("ITextFormHelper.GetUnicodeFont", ex.Message);
                    }
                }

                _unicodeFontResolved = true;
                return _unicodeFont;
            }
        }

        private static string MapFieldType(int fieldType)
        {
            return fieldType switch
            {
                AcroFields.FIELD_TYPE_TEXT => "text",
                AcroFields.FIELD_TYPE_CHECKBOX => "checkbox",
                AcroFields.FIELD_TYPE_RADIOBUTTON => "radio",
                AcroFields.FIELD_TYPE_LIST => "choice",
                AcroFields.FIELD_TYPE_COMBO => "choice",
                AcroFields.FIELD_TYPE_PUSHBUTTON => "button",
                AcroFields.FIELD_TYPE_SIGNATURE => "signature",
                _ => "field"
            };
        }

        private static string DecodePdfFieldName(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            var name = rawName.Trim();
            if (!name.Contains('#'))
                return name;

            try
            {
                var bytes = new List<byte>();
                var buffer = new StringBuilder();
                for (var i = 0; i < name.Length; i++)
                {
                    var ch = name[i];
                    if (ch == '#' && i + 2 < name.Length
                        && byte.TryParse(name.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    {
                        if (buffer.Length > 0)
                        {
                            bytes.AddRange(Encoding.UTF8.GetBytes(buffer.ToString()));
                            buffer.Clear();
                        }

                        bytes.Add(value);
                        i += 2;
                    }
                    else
                    {
                        buffer.Append(ch);
                    }
                }

                if (buffer.Length > 0)
                    bytes.AddRange(Encoding.UTF8.GetBytes(buffer.ToString()));

                var decoded = Encoding.UTF8.GetString(bytes.ToArray()).Trim();
                return string.IsNullOrWhiteSpace(decoded) ? name : decoded;
            }
            catch
            {
                return name;
            }
        }

        private static string SimplifyPdfUnsafeCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder
                .ToString()
                .Normalize(NormalizationForm.FormC)
                .Replace('\u0142', 'l')
                .Replace('\u0141', 'L')
                .Replace('\u0111', 'd')
                .Replace('\u0110', 'D')
                .Replace('\u00DF', 's');
        }

        private static bool IsTruthyValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "yes" or "ano" or "так" or "on" or "checked";
        }

        private static bool IsOffState(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                || value.Equals("Off", StringComparison.OrdinalIgnoreCase)
                || value.Equals("No", StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimNearby(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var collapsed = Regex.Replace(text, @"\s+", " ").Trim();
            return collapsed.Length > 220 ? collapsed.Substring(0, 220) : collapsed;
        }

        private static bool LooksReadable(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var readable = 0;
            var other = 0;
            foreach (var ch in text)
            {
                if (char.IsLetterOrDigit(ch) || char.IsPunctuation(ch) || char.IsWhiteSpace(ch))
                    readable++;
                else
                    other++;
            }

            return readable > 0 && other * 4 <= text.Length;
        }

        private readonly record struct TextChunk(string Text, float X, float Y);

        private readonly record struct PdfMatrix(float A, float B, float C, float D, float E, float F)
        {
            public static PdfMatrix Identity => new(1, 0, 0, 1, 0, 0);

            public static PdfMatrix Translate(float x, float y) => new(1, 0, 0, 1, x, y);

            public static PdfMatrix Concat(PdfMatrix left, PdfMatrix right)
            {
                return new PdfMatrix(
                    left.A * right.A + left.B * right.C,
                    left.A * right.B + left.B * right.D,
                    left.C * right.A + left.D * right.C,
                    left.C * right.B + left.D * right.D,
                    left.E * right.A + left.F * right.C + right.E,
                    left.E * right.B + left.F * right.D + right.F);
            }

            public (float X, float Y) Transform(float x, float y)
            {
                return (A * x + C * y + E, B * x + D * y + F);
            }
        }
    }
}

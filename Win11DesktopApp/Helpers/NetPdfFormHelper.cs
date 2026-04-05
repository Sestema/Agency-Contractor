using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JavaFile = Java.Io.File;
using JavaFileInputStream = Java.Io.FileInputStream;
using JavaArrayList = Java.Util.ArrayList<Org.Apache.Pdfbox.Pdmodel.Interactive.Form.PDField>;
using MASES.NetPDF;
using Org.Apache.Pdfbox;
using Org.Apache.Pdfbox.Cos;
using Org.Apache.Pdfbox.Pdmodel.Font;
using Org.Apache.Pdfbox.Pdmodel;
using Org.Apache.Pdfbox.Pdmodel.Interactive.Annotation;
using Org.Apache.Pdfbox.Pdmodel.Common;
using Org.Apache.Pdfbox.Pdmodel.Interactive.Form;
using Org.Apache.Pdfbox.Text;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Helpers
{
    internal sealed class AppNetPdfCore : NetPDFCore<AppNetPdfCore>
    {
    }

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

    public static class NetPdfFormHelper
    {
        private static readonly object SyncRoot = new();
        private static readonly Regex DefaultAppearanceFontRegex = new(@"/[^\s/]+\s+([0-9]+(?:\.[0-9]+)?)\s+Tf", RegexOptions.Compiled);
        private static readonly string[] UnicodeFontCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "tahoma.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "calibri.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialuni.ttf")
        };
        private const string TemurinInstallerEndpoint = "https://api.adoptium.net/v3/installer/latest/21/ga/windows/{0}/jdk/hotspot/normal/eclipse";
        private const string TemurinDownloadPageUrl = "https://adoptium.net/temurin/releases/?version=21&os=windows&package=jdk";
        private static bool _isInitialized;
        private static bool _initializationAttempted;
        private static string? _resolvedJvmPath;

        public static bool IsJavaRuntimeAvailable()
            => !string.IsNullOrWhiteSpace(ResolveJvmPath());

        public static void WarmUp()
        {
            if (!IsJavaRuntimeAvailable())
                return;

            _ = EnsureInitialized();
        }

        public static string GetJavaDownloadPageUrl()
            => TemurinDownloadPageUrl;

        public static void OpenJavaDownloadPage()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = TemurinDownloadPageUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NetPdfFormHelper.OpenJavaDownloadPage", ex.Message);
            }
        }

        public static async Task<bool> DownloadAndLaunchJavaInstallerAsync()
        {
            if (!OperatingSystem.IsWindows())
                return false;

            var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";
            var installerUrl = string.Format(TemurinInstallerEndpoint, arch);
            var installerDir = Path.Combine(Path.GetTempPath(), "Win11DesktopApp", "java-installer");
            var installerPath = Path.Combine(installerDir, $"Temurin21-{arch}.msi");

            try
            {
                Directory.CreateDirectory(installerDir);

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using var response = await client.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (var fileStream = File.Create(installerPath))
                {
                    await download.CopyToAsync(fileStream).ConfigureAwait(false);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\"",
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("NetPdfFormHelper.DownloadAndLaunchJavaInstallerAsync", ex);
                return false;
            }
        }

        public static IReadOnlyList<PdfFormFieldBinding> ReadFieldBindings(string pdfPath)
        {
            if (!EnsureInitialized() || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return Array.Empty<PdfFormFieldBinding>();

            try
            {
                using var document = Loader.LoadPDF(File.ReadAllBytes(pdfPath));
                var acroForm = document.DocumentCatalog?.AcroForm;
                if (acroForm == null)
                    return Array.Empty<PdfFormFieldBinding>();

                var detected = EnumerateFields(document, acroForm)
                    .GroupBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(f => f.FieldName, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (detected.Count == 0 && acroForm.HasXFA())
                {
                    LoggingService.LogWarning(
                        "NetPdfFormHelper.ReadFieldBindings",
                        $"PDF exposes XFA form data but no AcroForm fields were enumerated: {pdfPath}");
                }

                return detected;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("NetPdfFormHelper.ReadFieldBindings", ex);
                return Array.Empty<PdfFormFieldBinding>();
            }
        }

        public static PdfFormFillResult TryFillFormFields(
            string templatePath,
            string outputPath,
            IEnumerable<PdfFormFieldBinding> bindings,
            Dictionary<string, string> tagValues)
        {
            if (!EnsureInitialized())
                return new PdfFormFillResult { Success = false };

            try
            {
                using var document = Loader.LoadPDF(File.ReadAllBytes(templatePath));
                var acroForm = document.DocumentCatalog?.AcroForm;
                if (acroForm == null)
                    return new PdfFormFillResult { Success = false };

                acroForm.NeedAppearances = false;
                string? unicodeFontResourceName = null;
                PDFont? unicodeFont = null;
                var touchedFields = new JavaArrayList();
                var failedFields = new List<PdfFormFillIssue>();

                var availableFields = EnumerateFields(document, acroForm)
                    .Select(f => f.FieldName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (availableFields.Count == 0)
                {
                    if (acroForm.HasXFA())
                    {
                        LoggingService.LogWarning(
                            "NetPdfFormHelper.TryFillFormFields",
                            $"PDF exposes XFA form data but no AcroForm fields were enumerated: {templatePath}");
                    }

                    return new PdfFormFillResult { Success = false };
                }

                foreach (var binding in bindings)
                {
                    if (string.IsNullOrWhiteSpace(binding.FieldName))
                        continue;

                    if (!availableFields.Contains(binding.FieldName))
                        continue;

                    var field = acroForm.GetField(binding.FieldName);
                    if (field == null)
                        continue;

                    if (string.IsNullOrWhiteSpace(binding.TemplateText))
                        continue;

                    var resolvedValue = PdfInlineTextResolver.ResolveTemplate(binding.TemplateText, tagValues);
                    ResetFieldAppearanceState(field);
                    EnsureWidgetAppearances(document, field);
                    TryPrepareUnicodeTextField(document, acroForm, field, resolvedValue, ref unicodeFontResourceName, ref unicodeFont);
                    TryApplyFieldValue(field, resolvedValue ?? string.Empty, binding.FieldName, failedFields);
                    if (field is PDVariableText)
                        touchedFields.Add(field);
                }

                if (touchedFields.Size() > 0)
                {
                    try
                    {
                        acroForm.RefreshAppearances(touchedFields);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("NetPdfFormHelper.RefreshAppearances", ex.Message);
                    }
                }

                document.Save(outputPath);
                return new PdfFormFillResult
                {
                    Success = true,
                    FailedFields = failedFields
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("NetPdfFormHelper.TryFillFormFields", ex);
                return new PdfFormFillResult { Success = false };
            }
        }

        private static void TryApplyFieldValue(PDField field, string value, string fieldName, List<PdfFormFillIssue> failedFields)
        {
            try
            {
                if (field is PDCheckBox checkBox)
                {
                    if (IsTruthyValue(value))
                        checkBox.Check();
                    else
                        checkBox.UnCheck();
                    return;
                }

                if (field is PDRadioButton radioButton)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        field.SetValue("Off");
                        return;
                    }

                    if (IsTruthyValue(value))
                    {
                        field.SetValue(GetPreferredOnValue(radioButton));
                        return;
                    }

                    field.SetValue(value);
                    return;
                }

                if (field is PDButton button)
                {
                    if (button.IsPushButton())
                        return;

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        field.SetValue("Off");
                        return;
                    }

                    if (IsTruthyValue(value))
                    {
                        field.SetValue(GetPreferredOnValue(button));
                        return;
                    }
                }

                field.SetValue(value);
            }
            catch (Exception ex)
            {
                if (TryApplySanitizedFallbackValue(field, value, fieldName, ex))
                    return;

                LoggingService.LogWarning(
                    "NetPdfFormHelper.TryApplyFieldValue",
                    $"Skipping field '{fieldName}': {ex.Message}");
                failedFields.Add(new PdfFormFillIssue
                {
                    FieldName = fieldName,
                    Value = value,
                    Message = ex.Message
                });
            }
        }

        private static bool TryApplySanitizedFallbackValue(PDField field, string value, string fieldName, Exception originalException)
        {
            var normalizedFieldType = NormalizeFieldType(field.FieldType?.ToString());
            if (!string.Equals(normalizedFieldType, "text", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedFieldType, "choice", StringComparison.OrdinalIgnoreCase)
                && field is not PDVariableText)
            {
                return false;
            }

            var message = originalException.Message ?? string.Empty;
            if (!message.Contains("not available in the font", StringComparison.OrdinalIgnoreCase)
                && !message.Contains("WinAnsiEncoding", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var sanitizedValue = SimplifyPdfUnsafeCharacters(value);
            if (string.Equals(sanitizedValue, value, StringComparison.Ordinal))
                return false;

            try
            {
                field.SetValue(sanitizedValue);
                LoggingService.LogWarning(
                    "NetPdfFormHelper.TryApplyFieldValue",
                    $"Field '{fieldName}' used sanitized fallback value '{sanitizedValue}' after font encoding failure.");
                return true;
            }
            catch (Exception fallbackEx)
            {
                LoggingService.LogWarning(
                    "NetPdfFormHelper.TryApplyFieldValue",
                    $"Sanitized fallback also failed for field '{fieldName}': {fallbackEx.Message}");
                return false;
            }
        }

        private static void EnsureWidgetAppearances(PDDocument document, PDField field)
        {
            foreach (var widget in field.Widgets ?? Enumerable.Empty<PDAnnotationWidget>())
            {
                try
                {
                    var appearance = widget.Appearance;
                    if (appearance == null)
                    {
                        appearance = new PDAppearanceDictionary();
                        widget.Appearance = appearance;
                    }

                    if (appearance.NormalAppearance == null)
                    {
                        var stream = widget.NormalAppearanceStream ?? new PDAppearanceStream(document);
                        appearance.SetNormalAppearance(stream);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("NetPdfFormHelper.EnsureWidgetAppearances", ex.Message);
                }
            }
        }

        private static void ResetFieldAppearanceState(PDField field)
        {
            if (field is PDTextField textField)
            {
                try
                {
                    textField.SetActions(null);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("NetPdfFormHelper.ResetFieldAppearanceState", ex.Message);
                }
            }

            foreach (var widget in field.Widgets ?? Enumerable.Empty<PDAnnotationWidget>())
            {
                try
                {
                    widget.Action = null;
                    widget.Actions = null;
                    if (widget.COSObject is COSDictionary widgetDictionary)
                    {
                        widgetDictionary.RemoveItem(COSName.AP);
                        widgetDictionary.RemoveItem(COSName.AS);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("NetPdfFormHelper.ResetFieldAppearanceState", ex.Message);
                }
            }
        }

        private static void TryPrepareUnicodeTextField(
            PDDocument document,
            PDAcroForm acroForm,
            PDField field,
            string? value,
            ref string? unicodeFontResourceName,
            ref PDFont? unicodeFont)
        {
            if (field is not PDVariableText variableText || string.IsNullOrEmpty(value))
                return;

            try
            {
                unicodeFontResourceName ??= EnsureUnicodeFontResource(document, acroForm, ref unicodeFont);
                if (string.IsNullOrWhiteSpace(unicodeFontResourceName))
                    return;

                var acroDefaultAppearance = BuildUnicodeDefaultAppearance(acroForm.DefaultAppearance, unicodeFontResourceName);
                acroForm.DefaultAppearance = acroDefaultAppearance;
                ApplyUnicodeFontAliases(acroForm.DefaultResources, unicodeFontResourceName, unicodeFont);
                variableText.DefaultAppearance = BuildUnicodeDefaultAppearance(variableText.DefaultAppearance, unicodeFontResourceName, acroDefaultAppearance);
                ApplyWidgetDefaultAppearances(field, variableText.DefaultAppearance);
                if (field is PDTextField textField)
                    textField.SetActions(null);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(
                    "NetPdfFormHelper.TryPrepareUnicodeTextField",
                    $"Could not prepare Unicode font for field '{field.FullyQualifiedName}': {ex.Message}");
            }
        }

        private static void ApplyWidgetDefaultAppearances(PDField field, string defaultAppearance)
        {
            foreach (var widget in field.Widgets ?? Enumerable.Empty<PDAnnotationWidget>())
            {
                try
                {
                    if (widget.COSObject is COSDictionary widgetDictionary)
                    {
                        widgetDictionary.SetItem(COSName.DA, new COSString(defaultAppearance));
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("NetPdfFormHelper.ApplyWidgetDefaultAppearances", ex.Message);
                }
            }
        }

        private static string? EnsureUnicodeFontResource(PDDocument document, PDAcroForm acroForm, ref PDFont? unicodeFont)
        {
            var fontPath = ResolveUnicodeFontPath();
            if (string.IsNullOrWhiteSpace(fontPath))
                return null;

            var resources = acroForm.DefaultResources ?? new PDResources();
            acroForm.DefaultResources = resources;

            if (unicodeFont == null)
            {
                using var stream = new JavaFileInputStream(new JavaFile(fontPath));
                unicodeFont = PDType0Font.Load(document, stream, false);
            }

            var font = unicodeFont;
            if (font == null)
                return null;

            var resourceName = resources.Add(font);
            return resourceName?.Name;
        }

        private static void ApplyUnicodeFontAliases(PDResources? resources, string unicodeFontResourceName, PDFont? unicodeFont)
        {
            if (resources == null || unicodeFont == null)
                return;

            try
            {
                resources.Put(COSName.GetPDFName(unicodeFontResourceName), unicodeFont);
                resources.Put(COSName.GetPDFName("Helv"), unicodeFont);
                resources.Put(COSName.GetPDFName("Helvetica"), unicodeFont);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NetPdfFormHelper.ApplyUnicodeFontAliases", ex.Message);
            }
        }

        private static string? ResolveUnicodeFontPath()
        {
            foreach (var candidate in UnicodeFontCandidates)
            {
                if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string BuildUnicodeDefaultAppearance(string? existingAppearance, string fontResourceName, string? fallbackAppearance = null)
        {
            if (!string.IsNullOrWhiteSpace(existingAppearance))
            {
                var updated = DefaultAppearanceFontRegex.Replace(existingAppearance, $"/{fontResourceName} $1 Tf", 1);
                if (!string.Equals(updated, existingAppearance, StringComparison.Ordinal))
                    return updated;
            }

            if (!string.IsNullOrWhiteSpace(fallbackAppearance))
            {
                var updated = DefaultAppearanceFontRegex.Replace(fallbackAppearance, $"/{fontResourceName} $1 Tf", 1);
                if (!string.Equals(updated, fallbackAppearance, StringComparison.Ordinal))
                    return updated;
            }

            return $"/{fontResourceName} 0 Tf 0 g";
        }

        private static string SimplifyPdfUnsafeCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new System.Text.StringBuilder(normalized.Length);

            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
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

        private static IEnumerable<PdfFormFieldBinding> EnumerateFields(PDDocument document, PDAcroForm acroForm)
        {
            foreach (var field in acroForm.FieldTree ?? Enumerable.Empty<PDField>())
            {
                if (field == null)
                    continue;

                var rawName = FirstNonEmpty(field.FullyQualifiedName?.ToString(), field.PartialName?.ToString());
                var decodedName = DecodePdfFieldName(rawName);
                var name = FirstNonEmpty(decodedName, rawName);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var binding = new PdfFormFieldBinding
                {
                    FieldName = rawName,
                    DecodedFieldName = decodedName,
                    FieldType = NormalizeFieldType(field.FieldType?.ToString())
                };

                if (TryGetFieldBounds(document, field, out var page, out var rectangle))
                {
                    binding.Page = page;
                    binding.X = Math.Round(rectangle.LowerLeftX, 2);
                    binding.Y = Math.Round(rectangle.LowerLeftY, 2);
                    binding.Width = Math.Round(rectangle.Width, 2);
                    binding.Height = Math.Round(rectangle.Height, 2);
                    binding.NearbyText = ExtractNearbyText(document, page, rectangle);
                }

                yield return binding;
            }
        }

        private static bool TryGetFieldBounds(PDDocument document, PDField field, out int pageIndex, out PDRectangle rectangle)
        {
            pageIndex = -1;
            rectangle = default!;

            var widget = field.Widgets?.FirstOrDefault();
            if (widget?.Rectangle == null)
                return false;

            rectangle = widget.Rectangle;
            pageIndex = ResolvePageIndex(document, widget.Page);
            return rectangle.Width > 0 && rectangle.Height > 0;
        }

        private static int ResolvePageIndex(PDDocument document, PDPage? targetPage)
        {
            if (targetPage == null)
                return -1;

            try
            {
                for (var i = 0; i < document.NumberOfPages; i++)
                {
                    var candidate = document.GetPage(i);
                    if (ReferenceEquals(candidate, targetPage))
                        return i;

                    if (candidate?.COSObject != null && targetPage.COSObject != null && candidate.COSObject.Equals(targetPage.COSObject))
                        return i;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NetPdfFormHelper.ResolvePageIndex", ex.Message);
            }

            return -1;
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

        private static string ExtractNearbyText(PDDocument document, int pageIndex, PDRectangle fieldRectangle)
        {
            if (pageIndex < 0 || pageIndex >= document.NumberOfPages)
                return string.Empty;

            try
            {
                var page = document.GetPage(pageIndex);
                if (page == null)
                    return string.Empty;

                var mediaBox = page.MediaBox;
                if (mediaBox == null)
                    return string.Empty;

                var regionX = Math.Max(0, fieldRectangle.LowerLeftX - 180);
                var regionY = Math.Max(0, fieldRectangle.LowerLeftY - 18);
                var regionRight = Math.Min(mediaBox.Width, fieldRectangle.LowerLeftX + fieldRectangle.Width + 40);
                var regionTop = Math.Min(mediaBox.Height, fieldRectangle.LowerLeftY + fieldRectangle.Height + 18);
                var regionWidth = Math.Max(1, regionRight - regionX);
                var regionHeight = Math.Max(1, regionTop - regionY);

                var pdfYFromTop = Math.Max(0, mediaBox.Height - regionTop);
                var region = new Java.Awt.Rectangle(
                    (int)Math.Round(regionX),
                    (int)Math.Round(pdfYFromTop),
                    (int)Math.Round(regionWidth),
                    (int)Math.Round(regionHeight));

                using var stripper = new PDFTextStripperByArea();
                stripper.AddRegion("fieldContext", region);
                stripper.ExtractRegions(page);
                var text = stripper.GetTextForRegion("fieldContext")?.ToString()?.Trim() ?? string.Empty;
                text = Regex.Replace(text, @"\s+", " ").Trim();
                return text.Length > 220 ? text.Substring(0, 220) : text;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("NetPdfFormHelper.ExtractNearbyText", ex.Message);
                return string.Empty;
            }
        }

        private static bool EnsureInitialized()
        {
            if (_isInitialized)
                return true;

            lock (SyncRoot)
            {
                if (_isInitialized)
                    return true;

                if (_initializationAttempted)
                {
                    var candidate = ResolveJvmPath();
                    if (string.IsNullOrWhiteSpace(candidate) ||
                        string.Equals(candidate, _resolvedJvmPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }

                    _resolvedJvmPath = candidate;
                    _initializationAttempted = false;
                }

                _initializationAttempted = true;

                try
                {
                    _resolvedJvmPath = ResolveJvmPath();
                    if (!string.IsNullOrWhiteSpace(_resolvedJvmPath))
                    {
                        Environment.SetEnvironmentVariable("JCOBRIDGE_JVMPath", _resolvedJvmPath, EnvironmentVariableTarget.Process);
                        LoggingService.LogInfo("NetPdfFormHelper.EnsureInitialized", $"Using JVM at {_resolvedJvmPath}");
                    }
                    else
                    {
                        LoggingService.LogWarning("NetPdfFormHelper.EnsureInitialized", "JVM path auto-detection returned no result.");
                    }

                    AppNetPdfCore.CreateGlobalInstance();
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    LoggingService.LogError(
                        "NetPdfFormHelper.EnsureInitialized",
                        new InvalidOperationException(
                            $"NetPDF initialization failed. Resolved JVM path: {_resolvedJvmPath ?? "<none>"}",
                            ex));
                    _isInitialized = false;
                }

                return _isInitialized;
            }
        }

        private static string? ResolveJvmPath()
        {
            foreach (var candidate in GetJvmPathCandidates())
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static IEnumerable<string> GetJvmPathCandidates()
        {
            var directCandidates = new[]
            {
                Environment.GetEnvironmentVariable("JCOBRIDGE_JVMPath"),
                Environment.GetEnvironmentVariable("JAVA_HOME")
            }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(ExpandJavaHintToJvmCandidates);

            foreach (var candidate in directCandidates)
                yield return candidate;

            foreach (var root in GetKnownJavaRoots())
            {
                if (!Directory.Exists(root))
                    continue;

                IEnumerable<string> subdirectories;
                try
                {
                    subdirectories = Directory.EnumerateDirectories(root);
                }
                catch
                {
                    continue;
                }

                foreach (var dir in subdirectories)
                {
                    foreach (var candidate in ExpandJavaHintToJvmCandidates(dir))
                        yield return candidate;
                }
            }
        }

        private static IEnumerable<string> GetKnownJavaRoots()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            return new[]
            {
                Path.Combine(programFiles, "Java"),
                Path.Combine(programFiles, "Eclipse Adoptium"),
                Path.Combine(programFiles, "Amazon Corretto"),
                Path.Combine(programFiles, "Zulu"),
                Path.Combine(programFiles, "Microsoft"),
                Path.Combine(programFilesX86, "Java"),
                Path.Combine(programFilesX86, "Eclipse Adoptium"),
                Path.Combine(programFilesX86, "Amazon Corretto"),
                Path.Combine(programFilesX86, "Zulu"),
                Path.Combine(programFilesX86, "Microsoft")
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> ExpandJavaHintToJvmCandidates(string? hint)
        {
            if (string.IsNullOrWhiteSpace(hint))
                yield break;

            var normalized = hint.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(normalized))
                yield break;

            if (normalized.EndsWith("jvm.dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized;
                yield break;
            }

            if (normalized.EndsWith("java.exe", StringComparison.OrdinalIgnoreCase))
                normalized = Path.GetDirectoryName(normalized) ?? normalized;

            if (normalized.EndsWith(@"\bin", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(normalized, "server", "jvm.dll");
                yield return Path.Combine(normalized, "client", "jvm.dll");
            }

            yield return Path.Combine(normalized, "bin", "server", "jvm.dll");
            yield return Path.Combine(normalized, "bin", "client", "jvm.dll");
            yield return Path.Combine(normalized, "jre", "bin", "server", "jvm.dll");
            yield return Path.Combine(normalized, "jre", "bin", "client", "jvm.dll");
        }

        private static string NormalizeFieldType(string? fieldType)
        {
            var normalized = (fieldType ?? string.Empty).Trim();
            if (normalized.Equals("Tx", StringComparison.OrdinalIgnoreCase))
                return "text";
            if (normalized.Equals("Btn", StringComparison.OrdinalIgnoreCase))
                return "button";
            if (normalized.Equals("Ch", StringComparison.OrdinalIgnoreCase))
                return "choice";
            if (normalized.Equals("Sig", StringComparison.OrdinalIgnoreCase))
                return "signature";
            return string.IsNullOrWhiteSpace(normalized) ? "field" : normalized;
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        private static string GetPreferredOnValue(PDButton button)
        {
            if (button is PDCheckBox checkBox && !string.IsNullOrWhiteSpace(checkBox.OnValue))
                return checkBox.OnValue;

            foreach (var candidate in EnumerateStringValues(button.OnValues))
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "Off", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            foreach (var candidate in EnumerateStringValues(button.ExportValues))
            {
                if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "Off", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return "Yes";
        }

        private static bool IsTruthyValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "1" or "true" or "yes" or "ano" or "так" or "on" or "checked";
        }

        private static IEnumerable<string> EnumerateStringValues(IEnumerable? values)
        {
            if (values == null)
                yield break;

            foreach (var value in values)
            {
                var text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    yield return text;
            }
        }
    }
}

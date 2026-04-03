using System;
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using MASES.NetPDF;
using Org.Apache.Pdfbox;
using Org.Apache.Pdfbox.Pdmodel;
using Org.Apache.Pdfbox.Pdmodel.Common;
using Org.Apache.Pdfbox.Pdmodel.Interactive.Form;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Helpers
{
    internal sealed class AppNetPdfCore : NetPDFCore<AppNetPdfCore>
    {
    }

    public static class NetPdfFormHelper
    {
        private static readonly object SyncRoot = new();
        private const string TemurinInstallerEndpoint = "https://api.adoptium.net/v3/installer/latest/21/ga/windows/{0}/jdk/hotspot/normal/eclipse";
        private const string TemurinDownloadPageUrl = "https://adoptium.net/temurin/releases/?version=21&os=windows&package=jdk";
        private static bool _isInitialized;
        private static bool _initializationAttempted;
        private static string? _resolvedJvmPath;

        public static bool IsJavaRuntimeAvailable()
            => !string.IsNullOrWhiteSpace(ResolveJvmPath());

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

        public static bool TryFillFormFields(
            string templatePath,
            string outputPath,
            IEnumerable<PdfFormFieldBinding> bindings,
            Dictionary<string, string> tagValues)
        {
            if (!EnsureInitialized())
                return false;

            try
            {
                using var document = Loader.LoadPDF(File.ReadAllBytes(templatePath));
                var acroForm = document.DocumentCatalog?.AcroForm;
                if (acroForm == null)
                    return false;

                acroForm.NeedAppearances = true;

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

                    return false;
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
                    TryApplyFieldValue(field, resolvedValue ?? string.Empty, binding.FieldName);
                }

                document.Save(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("NetPdfFormHelper.TryFillFormFields", ex);
                return false;
            }
        }

        private static void TryApplyFieldValue(PDField field, string value, string fieldName)
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
                LoggingService.LogWarning(
                    "NetPdfFormHelper.TryApplyFieldValue",
                    $"Skipping field '{fieldName}': {ex.Message}");
            }
        }

        private static IEnumerable<PdfFormFieldBinding> EnumerateFields(PDDocument document, PDAcroForm acroForm)
        {
            foreach (var field in acroForm.FieldTree ?? Enumerable.Empty<PDField>())
            {
                if (field == null)
                    continue;

                var name = FirstNonEmpty(field.FullyQualifiedName?.ToString(), field.PartialName?.ToString());
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var binding = new PdfFormFieldBinding
                {
                    FieldName = name,
                    FieldType = NormalizeFieldType(field.FieldType?.ToString())
                };

                if (TryGetFieldBounds(document, field, out var page, out var rectangle))
                {
                    binding.Page = page;
                    binding.X = Math.Round(rectangle.LowerLeftX, 2);
                    binding.Y = Math.Round(rectangle.LowerLeftY, 2);
                    binding.Width = Math.Round(rectangle.Width, 2);
                    binding.Height = Math.Round(rectangle.Height, 2);
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

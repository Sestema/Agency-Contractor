using System;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// STA wrapper around <see cref="FlowDocumentDocxConverter"/> so legacy RTF
    /// can be turned into a native DOCX from either the UI thread or a worker.
    /// </summary>
    public static class RtfToNativeDocxService
    {
        public static byte[] ConvertLegacyEditorFiles(string? xamlPackagePath, string? rtfPath)
        {
            return ConvertLegacyEditorFiles(xamlPackagePath, rtfPath, DocxPageSetup.A4Portrait);
        }

        public static byte[] ConvertLegacyEditorFiles(string? xamlPackagePath, string? rtfPath, DocxPageSetup page)
        {
            return RunSta(() =>
            {
                var document = FlowDocumentDocxConverter.LoadLegacyDocument(xamlPackagePath, rtfPath)
                    ?? throw new InvalidOperationException("The legacy template could not be read.");
                return FlowDocumentDocxConverter.Convert(document, page);
            });
        }

        public static byte[] ConvertRtfFile(string rtfPath)
        {
            if (string.IsNullOrWhiteSpace(rtfPath) || !File.Exists(rtfPath))
                throw new FileNotFoundException("RTF template was not found.", rtfPath);

            return ConvertLegacyEditorFiles(null, rtfPath);
        }

        public static byte[] ConvertRtf(string rtf)
        {
            if (string.IsNullOrWhiteSpace(rtf))
                throw new ArgumentException("RTF content is empty.", nameof(rtf));

            return RunSta(() => FlowDocumentDocxConverter.RtfToDocx(rtf, DocxPageSetup.A4Portrait));
        }

        /// <summary>
        /// Word shows an AltChunk by importing the embedded RTF. The in-app editor only
        /// paints real paragraphs, so return those paragraphs without rewriting the file.
        /// </summary>
        public static byte[]? TryExpandAltChunkDocx(string docxPath)
        {
            if (!TryReadEmbeddedRtf(docxPath, out var rtfBytes, out var page) || rtfBytes.Length == 0)
                return null;

            return RunSta(() =>
            {
                using var stream = new MemoryStream(rtfBytes, writable: false);
                return FlowDocumentDocxConverter.Convert(FlowDocumentDocxConverter.LoadRtf(stream), page);
            });
        }

        private static bool TryReadEmbeddedRtf(string docxPath, out byte[] rtfBytes, out DocxPageSetup page)
        {
            rtfBytes = Array.Empty<byte>();
            page = DocxPageSetup.A4Portrait;
            if (string.IsNullOrWhiteSpace(docxPath) || !File.Exists(docxPath))
                return false;

            try
            {
                using var doc = WordprocessingDocument.Open(docxPath, false);
                var main = doc.MainDocumentPart;
                var body = main?.Document?.Body;
                var altChunk = body?.Elements<AltChunk>().FirstOrDefault();
                if (altChunk?.Id?.Value == null || main == null || body == null)
                    return false;
                if (main.GetPartById(altChunk.Id.Value) is not AlternativeFormatImportPart part)
                    return false;
                if (part.ContentType == null || part.ContentType.IndexOf("rtf", StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                page = ReadPageSetup(body);
                using var source = part.GetStream(FileMode.Open, FileAccess.Read);
                using var copy = new MemoryStream();
                source.CopyTo(copy);
                rtfBytes = copy.ToArray();
                return rtfBytes.Length > 0;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("RtfToNativeDocxService.TryExpandAltChunkDocx", ex.Message);
                return false;
            }
        }

        private static DocxPageSetup ReadPageSetup(Body body)
        {
            var section = body.Elements<SectionProperties>().LastOrDefault();
            var size = section?.GetFirstChild<PageSize>();
            var margin = section?.GetFirstChild<PageMargin>();
            if (size?.Width?.Value is not uint width || size.Height?.Value is not uint height)
                return DocxPageSetup.A4Portrait;

            return new DocxPageSetup(
                (int)width,
                (int)height,
                margin?.Left?.Value is uint left ? (int)left : 1440,
                margin?.Top?.Value ?? 1440,
                margin?.Right?.Value is uint right ? (int)right : 1440,
                margin?.Bottom?.Value ?? 1440);
        }

        private static T RunSta<T>(Func<T> action)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return action();

            T result = default!;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { result = action(); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null)
                throw error;
            return result;
        }
    }
}

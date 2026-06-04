using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Shared top-left text placement for PDF overlay tags (editor anchor = PDF draw origin).
    /// </summary>
    internal static class PdfTextLayoutHelper
    {
        public static XFont CreateFont(PdfTagPlacement placement)
        {
            var fontSize = placement.FontSize > 0 ? placement.FontSize : 10;
            var fontFamily = string.IsNullOrWhiteSpace(placement.FontFamily) ? "Arial" : placement.FontFamily;
            return new XFont(fontFamily, fontSize);
        }

        public static XStringFormat CreateTopAlignedFormat(string? textAlign)
        {
            var alignment = string.Equals(textAlign, "center", StringComparison.OrdinalIgnoreCase)
                ? XStringAlignment.Center
                : string.Equals(textAlign, "right", StringComparison.OrdinalIgnoreCase)
                    ? XStringAlignment.Far
                    : XStringAlignment.Near;

            return new XStringFormat
            {
                Alignment = alignment,
                LineAlignment = XLineAlignment.Near
            };
        }

        public static void DrawPlacement(XGraphics gfx, PdfPage page, PdfTagPlacement placement, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            var font = CreateFont(placement);
            var format = CreateTopAlignedFormat(placement.TextAlign);
            var x = placement.X * page.Width.Point;
            var y = placement.Y * page.Height.Point;

            if (string.Equals(placement.Kind, "field", StringComparison.OrdinalIgnoreCase))
            {
                var width = placement.MaxWidth > 0 ? placement.MaxWidth : 160;
                var fontSize = placement.FontSize > 0 ? placement.FontSize : 10;
                var height = placement.BoxHeight > 0
                    ? placement.BoxHeight
                    : Math.Max(fontSize * 1.25, fontSize + 2);
                gfx.DrawString(value, font, XBrushes.Black, new XRect(x, y, width, height), format);
                return;
            }

            if (placement.MaxWidth > 0)
            {
                var maxW = placement.MaxWidth;
                var rect = new XRect(x, y, maxW, page.Height.Point - y);
                gfx.DrawString(value, font, XBrushes.Black, rect, format);
                return;
            }

            var measured = gfx.MeasureString(value, font, format);
            var boxW = Math.Max(1, measured.Width);
            var boxH = Math.Max(1, measured.Height);
            gfx.DrawString(value, font, XBrushes.Black, new XRect(x, y, boxW, boxH), format);
        }
    }
}

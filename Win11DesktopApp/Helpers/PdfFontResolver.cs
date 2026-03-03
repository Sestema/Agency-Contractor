using System;
using System.IO;
using PdfSharp.Fonts;

namespace Win11DesktopApp.Helpers
{
    public class PdfFontResolver : IFontResolver
    {
        private static readonly string FontsDir =
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            string faceName = familyName.ToLowerInvariant();
            if (isBold) faceName += "|b";
            if (isItalic) faceName += "|i";
            return new FontResolverInfo(faceName);
        }

        public byte[]? GetFont(string faceName)
        {
            string? fileName = MapToFileName(faceName);
            if (fileName == null) return null;
            string path = Path.Combine(FontsDir, fileName);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        private static string? MapToFileName(string faceName)
        {
            return faceName switch
            {
                "segoe ui" => "segoeui.ttf",
                "segoe ui|b" => "segoeuib.ttf",
                "segoe ui|i" => "segoeuii.ttf",
                "segoe ui|b|i" => "segoeuiz.ttf",
                "arial" => "arial.ttf",
                "arial|b" => "arialbd.ttf",
                "arial|i" => "ariali.ttf",
                "arial|b|i" => "arialbi.ttf",
                "times new roman" => "times.ttf",
                "times new roman|b" => "timesbd.ttf",
                "times new roman|i" => "timesi.ttf",
                "times new roman|b|i" => "timesbi.ttf",
                "courier new" => "cour.ttf",
                "courier new|b" => "courbd.ttf",
                "courier new|i" => "couri.ttf",
                "courier new|b|i" => "courbi.ttf",
                _ => null
            };
        }
    }
}

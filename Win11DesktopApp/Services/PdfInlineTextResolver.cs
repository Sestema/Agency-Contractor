using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Win11DesktopApp.Services
{
    public static class PdfInlineTextResolver
    {
        private static readonly Regex TagTokenRegex = new(@"\$\{(?<tag>[A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        public static string ResolveTemplate(string? template, IReadOnlyDictionary<string, string> tagValues)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            return TagTokenRegex.Replace(template, match =>
            {
                var tagName = match.Groups["tag"].Value;
                return tagValues.TryGetValue(tagName, out var value) ? value ?? string.Empty : string.Empty;
            });
        }
    }
}

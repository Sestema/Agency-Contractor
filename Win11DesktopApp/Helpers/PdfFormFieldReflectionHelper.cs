using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Win11DesktopApp.Helpers
{
    public sealed class PdfFormFieldDescriptor
    {
        public string Name { get; init; } = string.Empty;
        public string FieldType { get; init; } = string.Empty;
        public object FieldObject { get; init; } = null!;
    }

    public static class PdfFormFieldReflectionHelper
    {
        public static IReadOnlyList<PdfFormFieldDescriptor> EnumerateFields(object pdfDocument)
        {
            var found = new List<object>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);

            CollectFromDocument(pdfDocument, found, visited);

            return found
                .Select(field => new PdfFormFieldDescriptor
                {
                    Name = GetFieldName(field),
                    FieldType = DetectFieldType(field),
                    FieldObject = field
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static object? FindFieldByName(object pdfDocument, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
                return null;

            return EnumerateFields(pdfDocument)
                .FirstOrDefault(x => string.Equals(x.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                ?.FieldObject;
        }

        private static void CollectFromDocument(object pdfDocument, IList<object> found, HashSet<object> visited)
        {
            var acroForm = GetProperty(pdfDocument, "AcroForm");
            if (acroForm != null)
            {
                CollectFromEnumerable(GetProperty(acroForm, "Fields"), found, visited);
                CollectFieldRecursive(acroForm, found, visited);
            }

            foreach (var page in EnumerateObjects(GetProperty(pdfDocument, "Pages")))
            {
                CollectFieldRecursive(page, found, visited);
                CollectFromEnumerable(GetProperty(page, "Annotations"), found, visited);
            }
        }

        private static void CollectFromEnumerable(object? source, IList<object> found, HashSet<object> visited)
        {
            foreach (var item in EnumerateObjects(source))
                CollectFieldRecursive(item, found, visited);
        }

        private static void CollectFieldRecursive(object? candidate, IList<object> found, HashSet<object> visited)
        {
            if (candidate == null || !visited.Add(candidate))
                return;

            if (LooksLikeField(candidate))
            {
                var name = GetFieldName(candidate);
                if (!string.IsNullOrWhiteSpace(name))
                    found.Add(candidate);
            }

            CollectFieldRecursive(GetProperty(candidate, "Field"), found, visited);
            CollectFieldRecursive(GetProperty(candidate, "Parent"), found, visited);

            CollectFromEnumerable(GetProperty(candidate, "Kids"), found, visited);
            CollectFromEnumerable(GetProperty(candidate, "Fields"), found, visited);
            CollectFromEnumerable(GetProperty(candidate, "Annotations"), found, visited);

            var elements = GetProperty(candidate, "Elements");
            if (elements != null)
            {
                CollectFieldRecursive(GetDictionaryValue(elements, "/Parent"), found, visited);
                CollectFieldRecursive(GetDictionaryValue(elements, "/Kids"), found, visited);
                CollectFieldRecursive(GetDictionaryValue(elements, "/FT"), found, visited);
            }
        }

        private static bool LooksLikeField(object candidate)
        {
            var typeName = candidate.GetType().Name.ToLowerInvariant();
            if (typeName.Contains("field") || typeName.Contains("widget"))
                return true;

            return candidate.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance) != null
                || candidate.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) != null
                || candidate.GetType().GetProperty("Checked", BindingFlags.Public | BindingFlags.Instance) != null;
        }

        private static string DetectFieldType(object field)
        {
            var typeName = field.GetType().Name.ToLowerInvariant();
            if (typeName.Contains("check"))
                return "checkbox";
            if (typeName.Contains("combo"))
                return "combo";
            if (typeName.Contains("list"))
                return "list";
            if (typeName.Contains("radio"))
                return "radio";
            if (typeName.Contains("text"))
                return "text";
            if (typeName.Contains("button"))
                return "button";
            if (typeName.Contains("widget"))
                return "widget";
            return "field";
        }

        private static string GetFieldName(object field)
        {
            return FirstNonEmpty(
                GetProperty(field, "Name")?.ToString(),
                GetProperty(field, "PartialName")?.ToString(),
                GetProperty(field, "Title")?.ToString(),
                GetProperty(field, "AlternateName")?.ToString(),
                GetProperty(field, "MappingName")?.ToString(),
                GetDictionaryValue(field, "/T")?.ToString(),
                GetDictionaryValue(field, "/TU")?.ToString(),
                GetDictionaryValue(field, "/TM")?.ToString());
        }

        private static string FirstNonEmpty(params string?[] values)
            => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

        private static object? GetProperty(object source, string propertyName)
            => source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);

        private static object? GetDictionaryValue(object source, string key)
        {
            var elements = source;
            if (!source.GetType().Name.Contains("Dictionary", StringComparison.OrdinalIgnoreCase))
                elements = GetProperty(source, "Elements") ?? source;

            var itemProperty = elements.GetType().GetProperty("Item", BindingFlags.Public | BindingFlags.Instance, null, null, new[] { typeof(string) }, null);
            if (itemProperty == null)
                return null;

            try
            {
                return itemProperty.GetValue(elements, new object[] { key });
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<object> EnumerateObjects(object? source)
        {
            if (source is string || source == null)
                yield break;

            if (source is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item != null)
                        yield return item;
                }
            }
        }
    }
}

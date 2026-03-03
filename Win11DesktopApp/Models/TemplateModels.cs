using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    public class TemplateEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty; // DOCX, XLSX, PDF
        public string FilePath { get; set; } = string.Empty; // Relative path
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<string> TagsUsed { get; set; } = new List<string>();
    }

    public class TemplateMetadata
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<string> TagsUsed { get; set; } = new List<string>();
    }

    public class TemplateIndexEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime Updated { get; set; }
    }

    public class TagPosition
    {
        public string Tag { get; set; } = string.Empty;
        public int Offset { get; set; }
    }

    /// <summary>
    /// Represents a tag placed on a PDF page at a specific position.
    /// </summary>
    public class PdfTagPlacement
    {
        public string Tag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double FontSize { get; set; } = 10;
        public string FontFamily { get; set; } = "Arial";
        public double MaxWidth { get; set; }
        public double PdfPageWidth { get; set; }
        public double PdfPageHeight { get; set; }
    }

    /// <summary>
    /// Stored alongside a PDF template, contains all tag placements.
    /// </summary>
    public class PdfTagMap
    {
        public List<PdfTagPlacement> Placements { get; set; } = new();
    }
}

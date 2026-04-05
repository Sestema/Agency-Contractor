using System;
using System.Collections.Generic;
using Win11DesktopApp.ViewModels;

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

    public class TemplateEditorLayoutSettings
    {
        public string PageSizeKey { get; set; } = "a4";
        public string OrientationKey { get; set; } = "portrait";
        public string MarginKey { get; set; } = "normal";
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
        public string Kind { get; set; } = "tag";
        public string Tag { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Page { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double FontSize { get; set; } = 10;
        public string FontFamily { get; set; } = "Arial";
        public double MaxWidth { get; set; }
        public double BoxHeight { get; set; } = 18;
        public string TextAlign { get; set; } = "left";
        public double PdfPageWidth { get; set; }
        public double PdfPageHeight { get; set; }
    }

    public class PdfFormFieldBinding
    {
        public string FieldName { get; set; } = string.Empty;
        public string DecodedFieldName { get; set; } = string.Empty;
        public string NearbyText { get; set; } = string.Empty;
        public string FieldType { get; set; } = string.Empty;
        public string TemplateText { get; set; } = string.Empty;
        public int Page { get; set; } = -1;
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// Stored alongside a PDF template, contains all tag placements.
    /// </summary>
    public class PdfTagMap
    {
        public string Mode { get; set; } = "overlay";
        public List<PdfTagPlacement> Placements { get; set; } = new();
        public List<PdfFormFieldBinding> FormFields { get; set; } = new();
    }

    public class AIPdfFieldSuggestion : ViewModelBase
    {
        public string FieldName { get; set; } = string.Empty;
        public string FieldType { get; set; } = string.Empty;
        public int FieldPage { get; set; }
        public string FieldLocationText { get; set; } = string.Empty;
        public string CurrentTemplateText { get; set; } = string.Empty;
        public string SuggestedText { get; set; } = string.Empty;
        public List<string> TagsUsed { get; set; } = new();
        public List<string> TagDisplayLines { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
        public string Confidence { get; set; } = "medium";

        private bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set => SetProperty(ref _isApplied, value);
        }

        private bool _isIgnored;
        public bool IsIgnored
        {
            get => _isIgnored;
            set => SetProperty(ref _isIgnored, value);
        }
    }
}

using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    public sealed class StarterTemplateCatalogEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PartyModel { get; set; } = string.Empty;
        public string ContractType { get; set; } = string.Empty;
        public string Format { get; set; } = "DOCX";
        public string RelativeContentPath { get; set; } = string.Empty;
        public string PreviewText { get; set; } = string.Empty;
        public List<string> TagsIncluded { get; set; } = new();
    }

    public sealed class StarterTemplateCatalogFile
    {
        public List<StarterTemplateCatalogEntry> Templates { get; set; } = new();
    }
}

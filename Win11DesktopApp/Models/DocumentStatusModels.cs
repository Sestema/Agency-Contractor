using System.Collections.Generic;

namespace Win11DesktopApp.EmployeeModels
{
    /// <summary>One document badge in the "documents in order" dialog, e.g. "Віза −40 дн.".</summary>
    public class DocumentStatusChip
    {
        public string Text { get; set; } = string.Empty;

        /// <summary>"expired", "soon" or "next" (the nearest end date for a person whose documents are fine).</summary>
        public string Kind { get; set; } = "next";
    }

    /// <summary>A person with the state of all their documents, for the "documents in order" dialog.</summary>
    public class DocumentStatusPerson
    {
        public string FullName { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;

        /// <summary>"expired", "soon" or "ok".</summary>
        public string Status { get; set; } = "ok";

        /// <summary>Days left on the worst document (negative = expired); used for sorting.</summary>
        public int WorstDays { get; set; } = int.MaxValue;

        public List<DocumentStatusChip> Chips { get; set; } = new();
    }
}

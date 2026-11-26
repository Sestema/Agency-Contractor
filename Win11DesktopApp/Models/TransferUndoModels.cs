using System;
using System.Collections.Generic;

namespace Win11DesktopApp.Models
{
    /// <summary>
    /// What the person looked like in the archive right before being restored into another firm.
    /// Saved with every restore, so a wrong transfer can be put back exactly as it was.
    /// </summary>
    public sealed class TransferSnapshot
    {
        public string RestoreOperationId { get; set; } = string.Empty;
        public string UniqueId { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>The firm the person was archived from (firm A).</summary>
        public string OriginalFirm { get; set; } = string.Empty;

        /// <summary>The firm the person was restored into (firm B).</summary>
        public string RestoredFirm { get; set; } = string.Empty;

        public string ArchiveFolder { get; set; } = string.Empty;
        public string RestoredFolder { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        /// <summary>employee.json of the archive copy, word for word.</summary>
        public string ArchivedJson { get; set; } = string.Empty;

        /// <summary>Files of the archive copy, relative to the employee folder.</summary>
        public List<string> Files { get; set; } = new();
    }

    public sealed class TransferUndoCheck
    {
        public bool CanUndo => string.IsNullOrEmpty(Reason);
        public string Reason { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public string OriginalFirm { get; init; } = string.Empty;
        public string RestoredFirm { get; init; } = string.Empty;
    }

    public sealed class TransferUndoResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string NewFolder { get; init; } = string.Empty;
        public string BackupPath { get; init; } = string.Empty;
        public string EmployeeName { get; init; } = string.Empty;
        public string OriginalFirm { get; init; } = string.Empty;
        public string RestoredFirm { get; init; } = string.Empty;
    }
}

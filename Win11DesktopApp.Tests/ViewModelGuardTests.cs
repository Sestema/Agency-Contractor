using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class ViewModelGuardTests
    {
        [Fact]
        public void GetVisibleArchiveLogEntries_ShouldExcludeRevertedEntries()
        {
            var entries = new List<ArchiveLogEntry>
            {
                new() { OperationId = "a1", Action = "Archived", IsReverted = false },
                new() { OperationId = "a2", Action = "Archived", IsReverted = true },
                new() { OperationId = "a3", Action = "Restored", IsReverted = false }
            };

            var visible = ReportViewModel.GetVisibleArchiveLogEntries(entries);

            Assert.Equal(2, visible.Count);
            Assert.DoesNotContain(visible, entry => entry.OperationId == "a2");
            Assert.Equal(new[] { "a1", "a3" }, visible.Select(entry => entry.OperationId).ToArray());
        }

        [Fact]
        public void IsUndoEligible_ShouldReturnTrue_ForRecentArchiveAction()
        {
            var now = new DateTime(2026, 3, 22, 20, 0, 0);
            var entry = new ActivityLogEntry
            {
                ActionType = "EmployeeArchived",
                RelatedOperationId = "op-1",
                Timestamp = now.AddHours(-2).ToString("yyyy-MM-dd HH:mm:ss")
            };

            var result = ActivityLogViewModel.IsUndoEligible(entry, new HashSet<string> { "op-1" }, now);

            Assert.True(result);
        }

        [Fact]
        public void IsUndoEligible_ShouldReturnFalse_WhenEntryIsTooOld()
        {
            var now = new DateTime(2026, 3, 22, 20, 0, 0);
            var entry = new ActivityLogEntry
            {
                ActionType = "EmployeeArchived",
                RelatedOperationId = "op-1",
                Timestamp = now.AddHours(-25).ToString("yyyy-MM-dd HH:mm:ss")
            };

            var result = ActivityLogViewModel.IsUndoEligible(entry, new HashSet<string> { "op-1" }, now);

            Assert.False(result);
        }

        [Fact]
        public void IsUndoEligible_ShouldReturnFalse_WhenOperationIsMissingOrUnsupported()
        {
            var now = new DateTime(2026, 3, 22, 20, 0, 0);
            var wrongAction = new ActivityLogEntry
            {
                ActionType = "EmployeeRestored",
                RelatedOperationId = "op-1",
                Timestamp = now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            var missingOperation = new ActivityLogEntry
            {
                ActionType = "EmployeeArchived",
                RelatedOperationId = "op-2",
                Timestamp = now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            Assert.False(ActivityLogViewModel.IsUndoEligible(wrongAction, new HashSet<string> { "op-1" }, now));
            Assert.False(ActivityLogViewModel.IsUndoEligible(missingOperation, new HashSet<string> { "op-1" }, now));
        }
    }
}

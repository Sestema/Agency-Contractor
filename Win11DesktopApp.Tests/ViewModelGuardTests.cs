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

        [Fact]
        public void ShouldResaveWhenCanonicalSavedEntryDuplicates_ShouldReturnTrue_WhenKeyAlreadyExists()
        {
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "emp-1|Firm A"
            };

            var result = SalaryViewModel.ShouldResaveWhenCanonicalSavedEntryDuplicates(existingKeys, "emp-1|Firm A");

            Assert.True(result);
        }

        [Fact]
        public void ShouldResaveWhenCanonicalSavedEntryDuplicates_ShouldReturnFalse_WhenKeyIsNew()
        {
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "emp-1|Firm A"
            };

            var result = SalaryViewModel.ShouldResaveWhenCanonicalSavedEntryDuplicates(existingKeys, "emp-2|Firm A");

            Assert.False(result);
        }

        [Fact]
        public void WorkedInAnyEmploymentPeriod_ShouldKeepOldMonthsAfterSameFirmRestore()
        {
            var periods = new Dictionary<string, List<(string StartDate, string EndDate)>>(StringComparer.OrdinalIgnoreCase);
            const string key = "emp-1|Firm A";

            SalaryViewModel.AddEmploymentPeriod(periods, key, "01.02.2026", "30.03.2026");
            SalaryViewModel.AddEmploymentPeriod(periods, key, "05.05.2026", "");

            Assert.True(SalaryViewModel.WorkedInAnyEmploymentPeriod(periods[key], 2026, 2));
            Assert.True(SalaryViewModel.WorkedInAnyEmploymentPeriod(periods[key], 2026, 3));
            Assert.False(SalaryViewModel.WorkedInAnyEmploymentPeriod(periods[key], 2026, 4));
            Assert.True(SalaryViewModel.WorkedInAnyEmploymentPeriod(periods[key], 2026, 5));
        }

        [Fact]
        public void ResolveHistoricalReportStartDate_ShouldPreferHistoricalPeriodStart()
        {
            var result = ReportViewModel.ResolveHistoricalReportStartDate("05.05.2026", "04.03.2026");

            Assert.Equal("04.03.2026", result);
        }

        [Fact]
        public void ResolveHistoricalReportStartDate_ShouldUseCreatedDate_WhenHistoricalStartIsBeforeEmployeeCreation()
        {
            var result = ReportViewModel.ResolveHistoricalReportStartDate(
                "04.05.2026",
                "11.08.2025",
                new DateTime(2026, 3, 4, 14, 40, 0),
                "13.04.2026");

            Assert.Equal("04.03.2026", result);
        }

        [Fact]
        public void ResolveHistoricalReportStartDate_ShouldKeepHistoricalDate_WhenCreationIsAfterHistoricalPeriod()
        {
            var result = ReportViewModel.ResolveHistoricalReportStartDate(
                "04.05.2026",
                "04.03.2026",
                new DateTime(2026, 5, 4, 10, 0, 0),
                "13.04.2026");

            Assert.Equal("04.03.2026", result);
        }

        [Fact]
        public void ShouldReplaceFirmExpenseForSelectedFirm_ShouldIgnoreCase()
        {
            var result = SalaryViewModel.ShouldReplaceFirmExpenseForSelectedFirm("firma a", "FirmA");

            Assert.False(result);
        }

        [Fact]
        public void ShouldReplaceFirmExpenseForSelectedFirm_ShouldMatchCaseInsensitiveFirmNames()
        {
            var result = SalaryViewModel.ShouldReplaceFirmExpenseForSelectedFirm("Firm A", "firm a");

            Assert.True(result);
        }
    }
}

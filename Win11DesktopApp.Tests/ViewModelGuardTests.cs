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
        public void CountReportPeriodMovement_ShouldUseDashboardRules_ForARangeLongerThanThreeMonths()
        {
            var from = new DateTime(2026, 4, 8);
            var to = new DateTime(2026, 9, 30);
            var active = new[]
            {
                new ReportViewModel.ReportMovementSource("a", "Active April", "Firm A", "10.04.2026", "", "folder-a"),
                new ReportViewModel.ReportMovementSource("b", "Still Working", "Firm A", "01.07.2026", "20.08.2026", "folder-b"),
                new ReportViewModel.ReportMovementSource("c", "Started Earlier", "Firm A", "01.01.2026", "", "folder-c")
            };
            var archived = new[]
            {
                new ReportViewModel.ReportMovementSource("d", "Left In May", "Firm A", "02.05.2026", "30.05.2026", "folder-d"),
                new ReportViewModel.ReportMovementSource("a", "Same Person Archived", "Firm A", "10.04.2026", "01.06.2026", "folder-a"),
                new ReportViewModel.ReportMovementSource("e", "Left Earlier", "Firm B", "01.02.2026", "01.03.2026", "folder-e")
            };

            var result = ReportViewModel.CountReportPeriodMovement(active, archived, from, to);

            Assert.Equal(3, result.NewCount);
            Assert.Equal(new[] { "a", "b", "d" }, result.Added.Select(person => person.UniqueId).ToArray());
            Assert.Equal(2, result.EndedCount);
            Assert.Equal(new[] { "d", "a" }, result.Ended.Select(person => person.UniqueId).ToArray());
            Assert.Equal(2, result.EndedByFirm["Firm A"]);
            Assert.False(result.EndedByFirm.ContainsKey("Firm B"));
        }

        [Fact]
        public void CountReportPeriodMovement_ShouldCountEachDistinctStartInsideTheSelectedPeriod()
        {
            var from = new DateTime(2026, 4, 8);
            var to = new DateTime(2026, 9, 30);
            var active = new[]
            {
                new ReportViewModel.ReportMovementSource("a", "Ivan", "Firm B", "01.08.2026", "", "folder-a")
            };
            var archived = new[]
            {
                new ReportViewModel.ReportMovementSource("a", "Ivan", "Firm A", "02.05.2026", "30.06.2026", "folder-a-old")
            };
            var history = new[]
            {
                new ReportViewModel.ReportMovementSource("a", "Ivan", "Firm A", "02.05.2026", "30.06.2026", "folder-a"),
                new ReportViewModel.ReportMovementSource("a", "Ivan", "Firm B", "1.8.2026", "", "folder-a"),
                new ReportViewModel.ReportMovementSource("b", "Olena", "Firm A", "15.04.2026", "01.05.2026", "folder-b")
            };

            var result = ReportViewModel.CountReportPeriodMovement(active, archived, from, to, history);

            Assert.Equal(3, result.NewCount);
            Assert.Equal(new[] { "01.08.2026", "02.05.2026", "15.04.2026" }, result.Added.Select(person => person.StartDate).ToArray());
            Assert.Equal(1, result.EndedCount);
            Assert.Equal("a", result.Ended[0].UniqueId);
        }

        [Fact]
        public void CountReportPeriodMovement_ShouldCountMissingUniqueIdOnce_ByFirmNameAndStart()
        {
            var from = new DateTime(2026, 4, 1);
            var to = new DateTime(2026, 9, 30);
            var active = new ReportViewModel.ReportMovementSource("", "Same Name", "Firm A", "01.05.2026", "", "folder-1");
            var archived = new ReportViewModel.ReportMovementSource("", "Same Name", "Firm A", "01.05.2026", "01.06.2026", "folder-2");

            var result = ReportViewModel.CountReportPeriodMovement(new[] { active }, new[] { archived }, from, to);

            Assert.Equal(1, result.NewCount);
            Assert.Equal(1, result.EndedCount);
            Assert.Equal(1, result.EndedByFirm["firm a"]);
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

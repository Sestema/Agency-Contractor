using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class TransferUndoTests
    {
        private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);
        private static string T(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss");

        [Fact]
        public void UndoneProfile_GetsFirmAValuesBack_AndKeepsNewEdits()
        {
            var archived = new EmployeeData
            {
                StartDate = "01.03.2026", ContractSignDate = "28.02.2026", PositionTag = "Skladník", PositionNumber = "7",
                WorkAddressTag = "Praha 1", IsArchived = true, ArchivedFromFirm = "A", EndDate = "25.11.2026", Status = "Dismissed",
                FirmHistory = new List<FirmHistoryEntry>
                {
                    new() { FirmName = "Old", StartDate = "01.01.2025", EndDate = "31.12.2025" },
                    new() { FirmName = "A", StartDate = "01.03.2026", EndDate = "25.11.2026" }
                },
                RequiredDocumentStatuses = new List<RequiredDocumentStatus> { new() { FirmName = "A", IsPresent = true } }
            };
            var current = new EmployeeData
            {
                StartDate = "26.11.2026", PositionTag = "Pole", Phone = "+420 111", Status = "Active",
                FirmHistory = archived.FirmHistory.Select(e => new FirmHistoryEntry { FirmName = e.FirmName, StartDate = e.StartDate, EndDate = e.EndDate }).ToList(),
                RequiredDocumentStatuses = new List<RequiredDocumentStatus> { new() { FirmName = "A", IsPresent = true, IsPeriodClosed = true } }
            };

            var result = EmployeeHistoryCleanupService.BuildUndoneProfile(current, archived, "A");

            Assert.Equal("01.03.2026", result.StartDate);
            Assert.Equal("28.02.2026", result.ContractSignDate);
            Assert.Equal("Skladník", result.PositionTag);
            Assert.Equal("7", result.PositionNumber);
            Assert.Equal("Praha 1", result.WorkAddressTag);
            Assert.Equal("+420 111", result.Phone);
            Assert.False(result.IsArchived);
            Assert.Equal(string.Empty, result.EndDate);
            Assert.Equal(string.Empty, result.ArchivedFromFirm);
            Assert.Equal(string.Empty, result.FirmHistory.Single(e => e.FirmName == "A").EndDate);
            Assert.Equal("31.12.2025", result.FirmHistory.Single(e => e.FirmName == "Old").EndDate);
            Assert.False(result.RequiredDocumentStatuses.Single().IsPeriodClosed);
            Assert.Equal("25.11.2026", archived.FirmHistory[1].EndDate); // the archive copy itself is not changed
        }

        [Fact]
        public void History_OnlyThisArchiveAndRestoreAreSelected()
        {
            var archivedAt = Now.AddMinutes(-20);
            var restoredAt = Now;
            var history = new List<EmployeeHistoryEntry>
            {
                new() { Id = 1, EventType = "Archived", OldValue = "A", Timestamp = Now.AddMonths(-5) },   // an older archive
                new() { Id = 2, EventType = "Archived", OldValue = "A", Timestamp = archivedAt.AddSeconds(2) },
                new() { Id = 3, EventType = "Restored", Field = "B", Timestamp = restoredAt.AddSeconds(1) },
                new() { Id = 4, EventType = "Restored", Field = "B", Timestamp = Now.AddMonths(-3) },      // an older restore
                new() { Id = 5, EventType = "ProfileChanged", Field = "Phone", Timestamp = Now.AddMinutes(-5) }
            };

            var picked = EmployeeHistoryCleanupService.SelectTransferHistory(history, "A", "B", archivedAt, restoredAt);

            Assert.Equal(new long[] { 2, 3 }, picked.Select(e => e.Id).OrderBy(id => id));
        }

        [Fact]
        public void NewFiles_IgnoreServiceFiles()
        {
            var before = new[] { "employee.json", @"Docs\passport.pdf" };
            var now = new[] { "employee.json", @"Docs\passport.pdf", "history.json", "~$smlouva.docx", "desktop.ini", "Smlouva B.docx" };

            Assert.Equal(new[] { "Smlouva B.docx" }, EmployeeHistoryCleanupService.NewFilesSince(before, now));
        }

        [Fact]
        public void ArchiveUndo_IsHidden_WhenThePersonWasRestoredAfterwards()
        {
            var log = new List<ArchiveLogEntry>
            {
                new() { OperationId = "arch-1", Action = "Archived", EmployeeName = "Ivan Petrenko", Timestamp = T(Now.AddMinutes(-10)) },
                new() { OperationId = "rest-1", Action = "Restored", EmployeeName = "Ivan Petrenko", Timestamp = T(Now.AddMinutes(-5)) },
                new() { OperationId = "arch-2", Action = "Archived", EmployeeName = "Olena K", Timestamp = T(Now.AddMinutes(-3)) }
            };

            var ids = ActivityLogViewModel.BuildUndoableArchiveIds(log);

            Assert.Equal(new[] { "arch-2" }, ids.ToArray());
        }

        [Fact]
        public void TransferUndo_OnlyForTheLastRecentMoveThatIsStillInPlace()
        {
            var log = new List<ArchiveLogEntry>
            {
                new() { OperationId = "r-old", Action = "Restored", EmployeeName = "Old One", Timestamp = T(Now.AddDays(-11)) },
                new() { OperationId = "r-moved", Action = "Restored", EmployeeName = "Ivan", Timestamp = T(Now.AddHours(-3)) },
                new() { OperationId = "a-moved", Action = "Archived", EmployeeName = "Ivan", Timestamp = T(Now.AddHours(-2)) },
                new() { OperationId = "r-ok", Action = "Restored", EmployeeName = "Olena", Timestamp = T(Now.AddDays(-9)) },
                new() { OperationId = "r-nofolder", Action = "Restored", EmployeeName = "Petro", Timestamp = T(Now.AddHours(-1)) }
            };

            var ids = ActivityLogViewModel.BuildUndoableRestoreIds(log, Now, entry => entry.OperationId != "r-nofolder");

            Assert.Equal(new[] { "r-ok" }, ids.ToArray());

            var entry = new ActivityLogEntry { ActionType = "EmployeeRestored", RelatedOperationId = "r-ok", Timestamp = T(Now.AddDays(-9)) };
            Assert.True(ActivityLogViewModel.IsTransferUndoEligible(entry, ids, Now));
            Assert.False(ActivityLogViewModel.IsTransferUndoEligible(entry, ids, Now.AddDays(2)));
            Assert.False(ActivityLogViewModel.IsTransferUndoEligible(
                new ActivityLogEntry { ActionType = "EmployeeArchived", RelatedOperationId = "r-ok", Timestamp = entry.Timestamp }, ids, Now));
        }
    }
}

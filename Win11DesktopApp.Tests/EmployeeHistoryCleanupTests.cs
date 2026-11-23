using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeHistoryCleanupTests
    {
        private const string Wrong = "RUMPOLD UHB s.r.o.";
        private const string Right = "DCK Holoubkov Bohemia a.s.";

        private static HistoryCleanupPerson Person() => new()
        {
            FullName = "Ivan Melesh",
            UniqueId = "id-1",
            EmployeeFolder = @"C:\Root\DCK\Працівники\Ivan_Melesh",
            CurrentFirm = Right
        };

        // Real shape: FirmHistory keeps only finished periods, the current firm comes from the profile.
        private static EmployeeData Data() => new()
        {
            UniqueId = "id-1",
            StartDate = "06.11.2026",
            FirmHistory = new List<FirmHistoryEntry>
            {
                new() { FirmName = Wrong, StartDate = "05.11.2026", EndDate = "06.11.2026" }
            }
        };

        private static List<EmployeeHistoryEntry> History() => new()
        {
            new() { Id = 1, EventType = "Created", Field = Wrong, Timestamp = new DateTime(2026, 11, 5, 14, 20, 0) },
            new() { Id = 2, EventType = "Archived", Field = "Архів", OldValue = Wrong, NewValue = "06.11.2026", Timestamp = new DateTime(2026, 11, 6, 9, 5, 0) },
            new() { Id = 3, EventType = "Restored", Field = Right, Timestamp = new DateTime(2026, 11, 6, 9, 12, 0) },
            new() { Id = 4, EventType = "ProfileChanged", Field = "Телефон", Timestamp = new DateTime(2026, 11, 5, 15, 0, 0) },
        };

        private static List<ActivityLogEntry> Activity() => new()
        {
            new() { Id = "a1", ActionType = "EmployeeAdded", FirmName = Wrong, EmployeeName = "Ivan Melesh", Timestamp = "2026-11-05 14:20:00" },
            new() { Id = "a2", ActionType = "EmployeeArchived", FirmName = Wrong, EmployeeName = "Ivan Melesh", Timestamp = "2026-11-06 09:05:00" },
            new() { Id = "a3", ActionType = "EmployeeRestored", FirmName = Right, EmployeeName = "Ivan Melesh", Timestamp = "2026-11-06 09:12:00" },
            // Someone else in the same firm on the same day must stay.
            new() { Id = "a4", ActionType = "EmployeeAdded", FirmName = Wrong, EmployeeName = "Petro Bondar", Timestamp = "2026-11-05 14:30:00" },
            // A document generation is not part of the move.
            new() { Id = "a5", ActionType = "DocGenerated", FirmName = Wrong, EmployeeName = "Ivan Melesh", Timestamp = "2026-11-05 14:40:00" },
        };

        private static List<ArchiveLogEntry> Archive() => new()
        {
            new() { OperationId = "op1", Action = "Archived", FirmName = Wrong, EmployeeName = "Ivan Melesh", Date = "06.11.2026" },
            new() { OperationId = "op2", Action = "Archived", FirmName = Wrong, EmployeeName = "Petro Bondar", Date = "06.11.2026" },
        };

        [Fact]
        public void WrongPeriod_FindsOnlyItsOwnTraces()
        {
            var periods = EmployeeHistoryCleanupService.BuildPeriods(Data(), Person(), History(), Activity(), Archive());
            var wrong = periods.Single(period => period.FirmName == Wrong);

            Assert.True(wrong.CanDelete);
            Assert.Equal(new long[] { 1, 2 }, wrong.HistoryEntries.Select(entry => entry.Id).OrderBy(id => id));
            Assert.Equal(new[] { "a1", "a2" }, wrong.ActivityEntries.Select(entry => entry.Id).OrderBy(id => id));
            Assert.Equal(new[] { "op1" }, wrong.ArchiveEntries.Select(entry => entry.OperationId));
        }

        [Fact]
        public void CurrentPeriod_CannotBeDeleted()
        {
            var periods = EmployeeHistoryCleanupService.BuildPeriods(Data(), Person(), History(), Activity(), Archive());
            var current = periods.Single(period => period.FirmName == Right);

            Assert.True(current.IsCurrent);
            Assert.False(current.CanDelete);
            Assert.Same(current, periods[0]);
        }

        [Fact]
        public void OnlyPeriodOfArchivedPerson_CannotBeDeleted()
        {
            var data = new EmployeeData
            {
                IsArchived = true,
                ArchivedFromFirm = Wrong,
                FirmHistory = new List<FirmHistoryEntry> { new() { FirmName = Wrong, StartDate = "05.11.2026", EndDate = "06.11.2026" } }
            };
            var person = new HistoryCleanupPerson { FullName = "Ivan Melesh", EmployeeFolder = @"C:\Root\Archive\Ivan", CurrentFirm = Wrong, IsArchived = true };

            var period = EmployeeHistoryCleanupService.BuildPeriods(data, person, History(), Activity(), Archive()).Single();
            Assert.False(period.CanDelete);
        }

        [Fact]
        public void OpenEntryInHistory_IsNotDoubledWithTheProfileFirm()
        {
            var data = Data();
            data.FirmHistory.Add(new FirmHistoryEntry { FirmName = Right, StartDate = "06.11.2026", EndDate = "" });

            var periods = EmployeeHistoryCleanupService.BuildPeriods(data, Person(), History(), Activity(), Archive());
            Assert.Equal(2, periods.Count);
            Assert.Single(periods, period => period.IsCurrent);
        }

        [Fact]
        public void ReturnToTheSameFirm_RestoreBelongsToTheNewPeriod()
        {
            // Like the real case: worked in AGRA until 30.04, restored back into AGRA on 05.05, new start 13.05.
            var data = new EmployeeData
            {
                StartDate = "13.05.2026",
                FirmHistory = new List<FirmHistoryEntry> { new() { FirmName = Wrong, StartDate = "13.01.2026", EndDate = "30.04.2026" } }
            };
            var person = new HistoryCleanupPerson { FullName = "Ivan Turok", EmployeeFolder = @"C:\Root\AGRA\Ivan", CurrentFirm = Wrong };
            var history = new List<EmployeeHistoryEntry>
            {
                new() { Id = 1, EventType = "Created", Field = Wrong, Timestamp = new DateTime(2026, 1, 12) },
                new() { Id = 2, EventType = "Archived", OldValue = Wrong, NewValue = "30.04.2026", Timestamp = new DateTime(2026, 4, 30) },
                new() { Id = 3, EventType = "Restored", Field = Wrong, Timestamp = new DateTime(2026, 5, 5) },
            };

            var periods = EmployeeHistoryCleanupService.BuildPeriods(data, person, history, new List<ActivityLogEntry>(), new List<ArchiveLogEntry>());
            var old = periods.Single(period => !period.IsCurrent);
            var now = periods.Single(period => period.IsCurrent);

            Assert.Equal(new long[] { 1, 2 }, old.HistoryEntries.Select(entry => entry.Id).OrderBy(id => id));
            Assert.Equal(new long[] { 3 }, now.HistoryEntries.Select(entry => entry.Id));
            Assert.True(old.CanDelete);
        }

        [Fact]
        public void LastPeriodOfArchivedPerson_CannotBeDeleted()
        {
            var data = new EmployeeData
            {
                IsArchived = true,
                ArchivedFromFirm = Wrong,
                FirmHistory = new List<FirmHistoryEntry>
                {
                    new() { FirmName = Right, StartDate = "01.01.2026", EndDate = "01.06.2026" },
                    new() { FirmName = Wrong, StartDate = "05.11.2026", EndDate = "06.11.2026" }
                }
            };

            var periods = EmployeeHistoryCleanupService.BuildPeriods(data, Person(), History(), Activity(), Archive());
            Assert.False(periods.Single(period => period.FirmName == Wrong).CanDelete);
            Assert.True(periods.Single(period => period.FirmName == Right).CanDelete);
        }

        [Fact]
        public void FirmWithoutPeriod_BecomesOrphanGroup_AndRealPeriodsKeepTheirRecords()
        {
            // Real case (Alina Korovka): restored into LESS & TIMBER with start 12.11, archived a minute later with end 11.11,
            // so the profile never got a LESS & TIMBER period. RUMPOLD periods are real.
            const string Rumpold = "RUMPOLD-P s.r.o. - Březnice";
            const string Less = "LESS & TIMBER, a.s.";
            var data = new EmployeeData
            {
                StartDate = "12.11.2026",
                FirmHistory = new List<FirmHistoryEntry>
                {
                    new() { FirmName = Rumpold, StartDate = "15.07.2025", EndDate = "16.03.2026" },
                    new() { FirmName = Rumpold, StartDate = "26.03.2026", EndDate = "07.09.2026" }
                }
            };
            var person = new HistoryCleanupPerson { FullName = "Alina Korovka", EmployeeFolder = @"C:\Root\RUMPOLD\Alina", CurrentFirm = Rumpold };
            var history = new List<EmployeeHistoryEntry>
            {
                new() { Id = 2700, EventType = "Restored", Field = Less, Timestamp = new DateTime(2026, 11, 11, 14, 22, 2) },
                new() { Id = 2701, EventType = "Archived", Field = "Архів", OldValue = Less, NewValue = "11.11.2026", Timestamp = new DateTime(2026, 11, 11, 14, 22, 44) },
                new() { Id = 2702, EventType = "Restored", Field = Rumpold, Timestamp = new DateTime(2026, 11, 11, 14, 23, 12) },
            };
            var activity = new List<ActivityLogEntry>
            {
                new() { Id = "a1", ActionType = "EmployeeRestored", FirmName = Less, EmployeeName = "Alina Korovka", Timestamp = "2026-11-11 14:22:02" },
                new() { Id = "a2", ActionType = "EmployeeArchived", FirmName = Less, EmployeeName = "Alina Korovka", Timestamp = "2026-11-11 14:22:53" },
                new() { Id = "a3", ActionType = "EmployeeArchived", FirmName = Rumpold, EmployeeName = "Alina Korovka", Timestamp = "2026-09-15 12:40:03",
                        Description = "Архівовано Alina Korovka з RUMPOLD-P s.r.o. - Březnice, дата закінчення: 07.09.2026" },
            };
            var archive = new List<ArchiveLogEntry>
            {
                new() { OperationId = "o1", Action = "Restored", FirmName = Less, EmployeeName = "Alina Korovka", Date = "12.11.2026" },
                new() { OperationId = "o2", Action = "Archived", FirmName = Less, EmployeeName = "Alina Korovka", Date = "11.11.2026" },
            };

            var periods = EmployeeHistoryCleanupService.BuildPeriods(data, person, history, activity, archive);
            var orphans = EmployeeHistoryCleanupService.BuildOrphans(periods, person, history, activity, archive);

            var less = Assert.Single(orphans);
            Assert.Equal(Less, less.FirmName);
            Assert.Equal(new long[] { 2700, 2701 }, less.HistoryEntries.Select(entry => entry.Id).OrderBy(id => id));
            Assert.Equal(new[] { "a1", "a2" }, less.ActivityEntries.Select(entry => entry.Id).OrderBy(id => id));
            Assert.Equal(2, less.ArchiveEntries.Count);

            // The second archive of 07.09 (made on 15.09 from the other PC) belongs to the real RUMPOLD period.
            var summer = periods.Single(period => period.Entry.StartDate == "26.03.2026");
            Assert.Contains(summer.ActivityEntries, entry => entry.Id == "a3");
            Assert.Contains(periods.Single(period => period.IsCurrent).HistoryEntries, entry => entry.Id == 2702);
        }

        [Fact]
        public void EventsOfAnotherPeriodInTheSameFirm_AreLeftAlone()
        {
            var data = new EmployeeData
            {
                StartDate = "06.11.2026",
                FirmHistory = new List<FirmHistoryEntry>
                {
                    new() { FirmName = Wrong, StartDate = "01.03.2026", EndDate = "30.04.2026" },
                    new() { FirmName = Wrong, StartDate = "05.11.2026", EndDate = "06.11.2026" }
                }
            };
            var history = History();
            history.Add(new EmployeeHistoryEntry { Id = 9, EventType = "Created", Field = Wrong, Timestamp = new DateTime(2026, 2, 27) });

            var periods = EmployeeHistoryCleanupService.BuildPeriods(data, Person(), history, Activity(), Archive());
            var november = periods.Single(period => period.FirmName == Wrong && period.Entry.StartDate == "05.11.2026");
            var spring = periods.Single(period => period.FirmName == Wrong && period.Entry.StartDate == "01.03.2026");

            Assert.DoesNotContain(november.HistoryEntries, entry => entry.Id == 9);
            Assert.Contains(spring.HistoryEntries, entry => entry.Id == 9);
            Assert.DoesNotContain(spring.HistoryEntries, entry => entry.Id == 1);
        }
    }
}

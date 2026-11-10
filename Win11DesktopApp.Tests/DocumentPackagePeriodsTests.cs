using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class DocumentPackagePeriodsTests
    {
        private static RequiredDocumentStatus Status(string firm, bool present = true) =>
            new() { FirmName = firm, TemplateId = "t1", NameSnapshot = "Smlouva", IsPresent = present };

        private static FirmHistoryEntry Period(string firm, string start, string end) =>
            new() { FirmName = firm, StartDate = start, EndDate = end };

        [Fact]
        public void Close_StampsDatesFromFirmHistory()
        {
            var data = new EmployeeData
            {
                RequiredDocumentStatuses = new List<RequiredDocumentStatus> { Status("A") },
                FirmHistory = new List<FirmHistoryEntry> { Period("A", "15.09.2026", "20.10.2026") }
            };

            DocumentPackagePeriods.CloseOpenPeriods(data);

            var status = Assert.Single(data.RequiredDocumentStatuses);
            Assert.True(status.IsPeriodClosed);
            Assert.Equal("15.09.2026", status.PeriodStart);
            Assert.Equal("20.10.2026", status.PeriodEnd);
            Assert.True(status.IsPresent);
        }

        [Fact]
        public void Close_DoesNotRewriteAlreadyClosedPeriods()
        {
            var old = Status("A");
            old.IsPeriodClosed = true;
            old.PeriodStart = "01.01.2025";
            old.PeriodEnd = "01.02.2025";
            var data = new EmployeeData
            {
                RequiredDocumentStatuses = new List<RequiredDocumentStatus> { old, Status("A") },
                FirmHistory = new List<FirmHistoryEntry>
                {
                    Period("A", "01.01.2025", "01.02.2025"),
                    Period("A", "15.09.2026", "20.10.2026")
                }
            };

            DocumentPackagePeriods.CloseOpenPeriods(data);

            Assert.Equal("01.01.2025", data.RequiredDocumentStatuses[0].PeriodStart);
            Assert.Equal("15.09.2026", data.RequiredDocumentStatuses[1].PeriodStart);
        }

        [Fact]
        public void Close_UsesArchivedFirmForLegacyRowsWithoutFirm()
        {
            var data = new EmployeeData
            {
                ArchivedFromFirm = "A",
                RequiredDocumentStatuses = new List<RequiredDocumentStatus> { Status(string.Empty) },
                FirmHistory = new List<FirmHistoryEntry> { Period("A", "15.09.2026", "20.10.2026") }
            };

            DocumentPackagePeriods.CloseOpenPeriods(data);

            Assert.Equal("A", data.RequiredDocumentStatuses[0].FirmName);
            Assert.Equal("20.10.2026", data.RequiredDocumentStatuses[0].PeriodEnd);
        }

        [Fact]
        public void History_SeparatesTwoPeriodsInSameFirm_NewestFirst()
        {
            var older = Status("A");
            older.IsPeriodClosed = true;
            older.PeriodStart = "01.01.2025";
            older.PeriodEnd = "01.02.2025";
            var newer = Status("A");
            newer.IsPeriodClosed = true;
            newer.PeriodStart = "15.09.2026";
            newer.PeriodEnd = "20.10.2026";

            var history = DocumentPackagePeriods.BuildHistory(new[] { older, newer }, "A", null);

            Assert.Equal(2, history.Count);
            Assert.Equal("15.09.2026", history[0].PeriodStart);
            Assert.Equal("01.01.2025", history[1].PeriodStart);
        }

        [Fact]
        public void History_ExcludesOpenStatusesOfCurrentFirm()
        {
            var history = DocumentPackagePeriods.BuildHistory(new[] { Status("A") }, "A", null);

            Assert.Empty(history);
        }

        [Fact]
        public void History_KeepsOpenStatusesOfOtherFirmsWithHistoryDates()
        {
            var history = DocumentPackagePeriods.BuildHistory(
                new[] { Status("B") },
                "A",
                new[] { Period("B", "01.03.2026", "01.04.2026") });

            var group = Assert.Single(history);
            Assert.False(group.IsClosedPeriod);
            Assert.Equal("01.04.2026", group.PeriodEnd);
        }

        [Fact]
        public void History_HidesClosedPeriodsWithNothingFilledIn()
        {
            var empty = Status("A", present: false);
            empty.IsPeriodClosed = true;
            empty.PeriodStart = "03.11.2026";

            var history = DocumentPackagePeriods.BuildHistory(new[] { empty }, "A", null);

            Assert.Empty(history);
        }
    }
}

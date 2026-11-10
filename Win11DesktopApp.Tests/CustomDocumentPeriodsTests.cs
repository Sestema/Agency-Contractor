using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class CustomDocumentPeriodsTests
    {
        private static CustomSignedDocument Doc(string name, string signDate) =>
            new() { Name = name, SignDate = signDate };

        private static readonly FirmHistoryEntry[] History =
        {
            new() { FirmName = "A", StartDate = "02.03.2026", EndDate = "15.06.2026" }
        };

        private static IReadOnlyList<CustomDocumentPeriodGroup> Split(params CustomSignedDocument[] documents) =>
            CustomDocumentPeriods.Split(documents, "A", History, hasOpenPeriod: true, "11.09.2026");

        [Fact]
        public void SplitsOldAndNew_NewestFirst()
        {
            var groups = Split(Doc("old", "02.03.2026"), Doc("new", "11.09.2026"));

            Assert.Equal(2, groups.Count);
            Assert.True(groups[0].IsOpen);
            Assert.Equal("11.09.2026", groups[0].PeriodStart);
            Assert.Equal("new", Assert.Single(groups[0].Documents).Name);
            Assert.False(groups[1].IsOpen);
            Assert.Equal("15.06.2026", groups[1].PeriodEnd);
            Assert.Equal("old", Assert.Single(groups[1].Documents).Name);
        }

        [Fact]
        public void SignedOnLastDay_StaysInOldPeriod()
        {
            var group = Assert.Single(Split(Doc("end", "15.06.2026")));

            Assert.False(group.IsOpen);
        }

        [Fact]
        public void SignedBeforeNewStart_GoesToNewPeriod()
        {
            var group = Assert.Single(Split(Doc("early", "10.09.2026")));

            Assert.True(group.IsOpen);
        }

        [Fact]
        public void SignedBeforeFirstPeriod_GoesToFirstPeriod()
        {
            var group = Assert.Single(Split(Doc("before", "01.03.2026")));

            Assert.False(group.IsOpen);
        }

        [Fact]
        public void ArchivedEmployee_LateDocumentStaysInLastPeriod()
        {
            var groups = CustomDocumentPeriods.Split(
                new[] { Doc("late", "01.08.2026") }, "A", History, hasOpenPeriod: false, string.Empty);

            Assert.False(Assert.Single(groups).IsOpen);
        }

        [Fact]
        public void NoHistory_KeepsAllDocumentsTogether()
        {
            var groups = CustomDocumentPeriods.Split(
                new[] { Doc("a", "01.01.2025"), Doc("b", "01.01.2026") }, "A", null, hasOpenPeriod: false, string.Empty);

            Assert.Equal(2, Assert.Single(groups).Documents.Count);
        }

        [Fact]
        public void HistoryOfOtherFirm_IsIgnored()
        {
            var history = new[] { new FirmHistoryEntry { FirmName = "B", StartDate = "01.01.2026", EndDate = "01.02.2026" } };

            var groups = CustomDocumentPeriods.Split(
                new[] { Doc("a", "01.01.2026") }, "A", history, hasOpenPeriod: true, "01.01.2026");

            Assert.True(Assert.Single(groups).IsOpen);
        }

        [Fact]
        public void UnreadableDate_GoesToOpenPeriod()
        {
            var group = Assert.Single(Split(Doc("bad", "")));

            Assert.True(group.IsOpen);
        }
    }
}

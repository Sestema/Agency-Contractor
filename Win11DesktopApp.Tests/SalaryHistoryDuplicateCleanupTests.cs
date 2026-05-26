using System;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class SalaryHistoryDuplicateCleanupTests
    {
        [Fact]
        public void GetDuplicateRecordIdsToRemove_KeepsNewestPaidAtWithinSameEmployeeAndPeriod()
        {
            var rows = new[]
            {
                new SalaryHistoryDuplicateCleanupRow
                {
                    Id = "old",
                    EmployeeId = "emp-1",
                    EmployeeFolder = "folder-a",
                    Year = 2026,
                    Month = 5,
                    FirmName = "Firma s.r.o.",
                    PaidAt = new DateTime(2026, 5, 1)
                },
                new SalaryHistoryDuplicateCleanupRow
                {
                    Id = "new",
                    EmployeeId = "emp-1",
                    EmployeeFolder = "folder-b",
                    Year = 2026,
                    Month = 5,
                    FirmName = "Firma  s.r.o.",
                    PaidAt = new DateTime(2026, 5, 10)
                }
            };

            var idsToRemove = SalaryHistoryDuplicateCleanup.GetDuplicateRecordIdsToRemove(rows);

            Assert.Single(idsToRemove);
            Assert.Equal("old", idsToRemove[0]);
        }

        [Fact]
        public void GetDuplicateRecordIdsToRemove_DoesNotMergeDifferentEmployees()
        {
            var rows = new[]
            {
                new SalaryHistoryDuplicateCleanupRow
                {
                    Id = "a",
                    EmployeeId = "emp-1",
                    EmployeeFolder = "folder-a",
                    Year = 2026,
                    Month = 5,
                    FirmName = "Firma",
                    PaidAt = new DateTime(2026, 5, 1)
                },
                new SalaryHistoryDuplicateCleanupRow
                {
                    Id = "b",
                    EmployeeId = "emp-2",
                    EmployeeFolder = "folder-b",
                    Year = 2026,
                    Month = 5,
                    FirmName = "Firma",
                    PaidAt = new DateTime(2026, 5, 1)
                }
            };

            var idsToRemove = SalaryHistoryDuplicateCleanup.GetDuplicateRecordIdsToRemove(rows);

            Assert.Empty(idsToRemove);
        }

        [Fact]
        public void DeduplicateRecords_MatchesCleanupWinnerSelection()
        {
            var records = new[]
            {
                new SalaryHistoryRecord
                {
                    Id = "old",
                    Year = 2026,
                    Month = 5,
                    FirmName = "Firma",
                    PaidAt = new DateTime(2026, 5, 1)
                },
                new SalaryHistoryRecord
                {
                    Id = "new",
                    Year = 2026,
                    Month = 5,
                    FirmName = "FIRMA",
                    PaidAt = new DateTime(2026, 5, 15)
                }
            };

            var deduped = SalaryHistoryDuplicateCleanup.DeduplicateRecords(records);

            Assert.Single(deduped);
            Assert.Equal("new", deduped[0].Id);
        }
    }
}

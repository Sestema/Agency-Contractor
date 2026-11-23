using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeHistoryCleanupSalaryTests : IDisposable
    {
        private readonly string _root;
        private readonly SalaryDbService _salaryDbService;

        public EmployeeHistoryCleanupSalaryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "AgencyContractorHistoryCleanupTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_root);
            var settings = new AppSettingsService();
            settings.Settings.RootFolderPath = _root;
            _salaryDbService = new SalaryDbService(new FolderService(settings));
        }

        [Fact]
        public void FindAndDelete_TouchesOnlyThePersonInTheFirm()
        {
            _salaryDbService.SaveMonthPayments(2026, 11, new List<SalaryEntry>
            {
                new() { EmployeeId = "id-1", EmployeeFolder = @"C:\a\Ivan", FullName = "Ivan", FirmName = "Wrong" },
                new() { EmployeeId = "id-1", EmployeeFolder = @"C:\b\Ivan", FullName = "Ivan", FirmName = "Right", HoursWorked = 160 },
                new() { EmployeeId = "id-2", EmployeeFolder = @"C:\a\Petro", FullName = "Petro", FirmName = "Wrong" },
            }, new List<FirmExpense>());

            var rows = _salaryDbService.FindEmployeeFirmRows(2026, 11, "wrong", "id-1", new[] { @"C:\a\Ivan" });
            Assert.Single(rows);
            Assert.False(rows[0].HasMeaningfulData);

            Assert.Equal(1, _salaryDbService.DeleteSalaryEntries(2026, 11, rows.Select(row => row.Id).ToList()));

            var left = _salaryDbService.LoadMonthPayments(2026, 11).entries;
            Assert.Equal(2, left.Count);
            Assert.Contains(left, entry => entry.FirmName == "Right" && entry.EmployeeId == "id-1");
            Assert.Contains(left, entry => entry.FirmName == "Wrong" && entry.EmployeeId == "id-2");
        }

        [Fact]
        public void RowWithHours_IsReportedAsMoney()
        {
            _salaryDbService.SaveMonthPayments(2026, 11, new List<SalaryEntry>
            {
                new() { EmployeeId = "id-1", EmployeeFolder = @"C:\a\Ivan", FullName = "Ivan", FirmName = "Wrong", HoursWorked = 8 },
            }, new List<FirmExpense>());

            var row = Assert.Single(_salaryDbService.FindEmployeeFirmRows(2026, 11, "Wrong", "id-1", Array.Empty<string>()));
            Assert.True(row.HasMeaningfulData);
        }

        [Fact]
        public void MissingMonth_ReturnsNothing()
        {
            Assert.Empty(_salaryDbService.FindEmployeeFirmRows(2025, 1, "Wrong", "id-1", Array.Empty<string>()));
            Assert.Equal(0, _salaryDbService.DeleteSalaryEntries(2025, 1, new long[] { 1 }));
        }

        public void Dispose()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(_root, true);
            }
            catch
            {
                // Temp folder cleanup is best effort.
            }
        }
    }
}

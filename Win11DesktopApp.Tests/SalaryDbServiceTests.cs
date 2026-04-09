using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class SalaryDbServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly SalaryDbService _salaryDbService;

        public SalaryDbServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorSalaryDbTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _folderService = new FolderService(_appSettingsService);
            _salaryDbService = new SalaryDbService(_folderService);
        }

        [Fact]
        public void ReplaceMonthData_ShouldCreateMonthDatabase()
        {
            var entries = new List<SalaryEntry>
            {
                new()
                {
                    EmployeeId = "emp-1",
                    EmployeeFolder = @"C:\Employees\John",
                    FullName = "John Doe",
                    FirmName = "Firm A",
                    HoursWorked = 160m,
                    HourlyRate = 120m,
                    Advance = 2000m,
                    SavedNetSalary = 17200m,
                    Status = "paid"
                }
            };

            var expenses = new List<FirmExpense>
            {
                new()
                {
                    Id = "exp-1",
                    FirmName = "Firm A",
                    Year = 2026,
                    Month = 3,
                    Name = "Fuel",
                    Amount = 1200.50m
                }
            };

            _salaryDbService.ReplaceMonthData(2026, 3, entries, expenses);

            Assert.True(File.Exists(_salaryDbService.GetMonthDbPath(2026, 3)));
        }

        [Fact]
        public void GetMonthValidationSnapshot_ShouldReturnImportedCountsAndTotals()
        {
            var entries = new List<SalaryEntry>
            {
                new()
                {
                    EmployeeId = "emp-1",
                    EmployeeFolder = @"C:\Employees\John",
                    FullName = "John Doe",
                    FirmName = "Firm A",
                    HoursWorked = 160m,
                    HourlyRate = 120m,
                    Advance = 2000m,
                    SavedNetSalary = 17200m,
                    Status = "paid",
                    CustomValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["bonus"] = 1500m
                    }
                },
                new()
                {
                    EmployeeId = "emp-2",
                    EmployeeFolder = @"C:\Employees\Jane",
                    FullName = "Jane Doe",
                    FirmName = "Firm B",
                    HoursWorked = 150m,
                    HourlyRate = 110m,
                    Advance = 1000m,
                    SavedNetSalary = 15500m,
                    Status = "pending"
                }
            };

            var expenses = new List<FirmExpense>
            {
                new() { Id = "exp-1", FirmName = "Firm A", Year = 2026, Month = 4, Name = "Fuel", Amount = 500m },
                new() { Id = "exp-2", FirmName = "Firm B", Year = 2026, Month = 4, Name = "Hotel", Amount = 700m }
            };

            _salaryDbService.ReplaceMonthData(2026, 4, entries, expenses);

            var snapshot = _salaryDbService.GetMonthValidationSnapshot(2026, 4);

            Assert.Equal(2, snapshot.EntryCount);
            Assert.Equal(2, snapshot.ExpenseCount);
            Assert.Equal(32700m, snapshot.SavedNetSalaryTotal);
            Assert.Equal(1, snapshot.StatusCounts["paid"]);
            Assert.Equal(1, snapshot.StatusCounts["pending"]);
        }

        [Fact]
        public void ReplaceMonthData_ShouldOverwriteExistingMonthData()
        {
            _salaryDbService.ReplaceMonthData(2026, 5,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeFolder = @"C:\Employees\One",
                        FirmName = "Firm A",
                        FullName = "One User",
                        SavedNetSalary = 100m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            _salaryDbService.ReplaceMonthData(2026, 5,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeFolder = @"C:\Employees\Two",
                        FirmName = "Firm B",
                        FullName = "Two User",
                        SavedNetSalary = 250m,
                        Status = "paid"
                    }
                },
                new List<FirmExpense>());

            var snapshot = _salaryDbService.GetMonthValidationSnapshot(2026, 5);

            Assert.Equal(1, snapshot.EntryCount);
            Assert.Equal(250m, snapshot.SavedNetSalaryTotal);
            Assert.DoesNotContain(snapshot.StatusCounts, pair => pair.Key.Equals("pending", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, snapshot.StatusCounts["paid"]);
        }

        [Fact]
        public void GetSavedPaymentsForEmployee_ShouldFallbackToFolder_WhenDbRowEmployeeIdIsEmpty()
        {
            _salaryDbService.ReplaceMonthData(2026, 6,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = string.Empty,
                        EmployeeFolder = @"C:\Employees\John",
                        FirmName = "Firm A",
                        FullName = "John Doe",
                        SavedNetSalary = 1234m,
                        Status = "paid"
                    }
                },
                new List<FirmExpense>());

            var result = _salaryDbService.GetSavedPaymentsForEmployee(
                @"C:\Employees\John",
                "emp-123",
                "Firm A",
                "2026-07");

            var month = Assert.Single(result);
            Assert.Equal("2026-06", month.Key);
            Assert.Equal(1234m, month.Value.netSalary);
            Assert.True(month.Value.paid);
        }

        [Fact]
        public void UpdateHourlyRateForward_ShouldFallbackToFolder_WhenDbRowEmployeeIdIsEmpty()
        {
            _salaryDbService.ReplaceMonthData(2026, 7,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = string.Empty,
                        EmployeeFolder = @"C:\Employees\John",
                        FirmName = "Firm A",
                        FullName = "John Doe",
                        HourlyRate = 50m,
                        SavedNetSalary = 1000m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            _salaryDbService.UpdateHourlyRateForward(
                "emp-123",
                @"C:\Employees\John",
                "Firm A",
                77m,
                "2026-06");

            var (entries, _) = _salaryDbService.LoadMonthPayments(2026, 7);
            var updated = Assert.Single(entries);
            Assert.Equal(77m, updated.HourlyRate);
        }

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}

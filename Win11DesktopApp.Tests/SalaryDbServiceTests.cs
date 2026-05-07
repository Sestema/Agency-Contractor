using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;
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
        public void MonthDbExists_ShouldRefreshIndex_WhenMonthFileAppearsAfterInitialMiss()
        {
            Assert.False(_salaryDbService.MonthDbExists(2026, 12));

            var folder = _salaryDbService.SalaryDbFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "salary_2026_12.db"), string.Empty);

            Assert.True(_salaryDbService.MonthDbExists(2026, 12));
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
        public void SaveMonthPayments_ShouldMergeChangedRows_WithoutDeletingOtherEmployees()
        {
            _salaryDbService.ReplaceMonthData(2026, 12,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-a",
                        EmployeeFolder = @"C:\Employees\A",
                        FirmName = "Firm A",
                        FullName = "Employee A",
                        HoursWorked = 0m,
                        HourlyRate = 100m,
                        SavedNetSalary = 0m,
                        Status = "pending"
                    },
                    new()
                    {
                        EmployeeId = "emp-b",
                        EmployeeFolder = @"C:\Employees\B",
                        FirmName = "Firm A",
                        FullName = "Employee B",
                        HoursWorked = 8m,
                        HourlyRate = 150m,
                        SavedNetSalary = 1200m,
                        Status = "paid"
                    }
                },
                new List<FirmExpense>());

            _salaryDbService.SaveMonthPayments(2026, 12,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-a",
                        EmployeeFolder = @"C:\Employees\A",
                        FirmName = "Firm A",
                        FullName = "Employee A",
                        HoursWorked = 10m,
                        HourlyRate = 100m,
                        SavedNetSalary = 1000m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            var (entries, _) = _salaryDbService.LoadMonthPayments(2026, 12);

            Assert.Equal(2, entries.Count);
            Assert.Contains(entries, entry => entry.EmployeeId == "emp-a" && entry.HoursWorked == 10m && entry.SavedNetSalary == 1000m);
            Assert.Contains(entries, entry => entry.EmployeeId == "emp-b" && entry.HoursWorked == 8m && entry.SavedNetSalary == 1200m);
        }

        [Fact]
        public void SaveMonthPayments_ShouldRemoveOldFolderDuplicate_ForSameEmployeeIdAndFirm()
        {
            _salaryDbService.ReplaceMonthData(2026, 5,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-a",
                        EmployeeFolder = @"C:\Employees\OldA",
                        FirmName = "Firm A",
                        FullName = "Employee A",
                        HoursWorked = 0m,
                        HourlyRate = 100m,
                        SavedNetSalary = 0m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            _salaryDbService.SaveMonthPayments(2026, 5,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-a",
                        EmployeeFolder = @"C:\Employees\NewA",
                        FirmName = "Firm A",
                        FullName = "Employee A",
                        HoursWorked = 233m,
                        HourlyRate = 100m,
                        SavedNetSalary = 23300m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            var (entries, _) = _salaryDbService.LoadMonthPayments(2026, 5);

            var employeeEntries = entries.Where(entry => entry.EmployeeId == "emp-a" && entry.FirmName == "Firm A").ToList();
            var entry = Assert.Single(employeeEntries);
            Assert.Equal(@"C:\Employees\NewA", entry.EmployeeFolder);
            Assert.Equal(233m, entry.HoursWorked);
            Assert.Equal(23300m, entry.SavedNetSalary);
        }

        [Fact]
        public void LoadMonthPayments_ShouldReturnNewestDuplicateFirst_ForLegacyDuplicateRows()
        {
            _salaryDbService.ReplaceMonthData(2026, 5,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-a",
                        EmployeeFolder = @"C:\Employees\OldA",
                        FirmName = "Firm A",
                        FullName = "Employee A",
                        HoursWorked = 0m,
                        HourlyRate = 100m,
                        SavedNetSalary = 0m,
                        Status = "pending"
                    }
                },
                new List<FirmExpense>());

            using (var connection = new SqliteConnection($"Data Source={_salaryDbService.GetMonthDbPath(2026, 5)};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
INSERT INTO salary_entries (
    firm_name, year, month, employee_id, employee_folder, full_name,
    hours_worked, hourly_rate, advance, saved_net_salary, status, note, color_tag, custom_values, updated_at
) VALUES (
    @firmName, 2026, 5, @employeeId, @employeeFolder, @fullName,
    @hoursWorked, @hourlyRate, '0', @savedNetSalary, 'pending', '', '', '{}', @updatedAt
);";
                command.Parameters.AddWithValue("@firmName", "Firm A");
                command.Parameters.AddWithValue("@employeeId", "emp-a");
                command.Parameters.AddWithValue("@employeeFolder", @"C:\Employees\NewA");
                command.Parameters.AddWithValue("@fullName", "Employee A");
                command.Parameters.AddWithValue("@hoursWorked", "233");
                command.Parameters.AddWithValue("@hourlyRate", "100");
                command.Parameters.AddWithValue("@savedNetSalary", "23300");
                command.Parameters.AddWithValue("@updatedAt", "2099-05-07T18:25:38.0000000Z");
                command.ExecuteNonQuery();
            }

            var (entries, _) = _salaryDbService.LoadMonthPayments(2026, 5);

            var employeeEntries = entries.Where(entry => entry.EmployeeId == "emp-a" && entry.FirmName == "Firm A").ToList();
            Assert.Equal(2, employeeEntries.Count);
            Assert.Equal(@"C:\Employees\NewA", employeeEntries[0].EmployeeFolder);
            Assert.Equal(233m, employeeEntries[0].HoursWorked);
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
        public void ParseDecimal_WhenValueIsInvalid_ShouldLogWarning_AndReturnZero()
        {
            LoggingService.Initialize(_testRootPath);
            var parseMethod = typeof(SalaryDbService).GetMethod("ParseDecimal", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);

            var result = (decimal)parseMethod!.Invoke(null, new object?[] { "not-a-number" })!;

            Assert.Equal(0m, result);
            Assert.Contains(LoggingService.GetRecentEntries(), entry =>
                entry.Module == "SalaryDbService.ParseDecimal"
                && entry.Severity == "WARN"
                && entry.Message.Contains("not-a-number", StringComparison.Ordinal));
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

        [Fact]
        public void UpsertFirmExpense_ShouldInsertOrUpdateSingleExpense()
        {
            _salaryDbService.ReplaceMonthData(2026, 8, new List<SalaryEntry>(), new List<FirmExpense>());

            _salaryDbService.UpsertFirmExpense(2026, 8, new FirmExpense
            {
                Id = "exp-1",
                FirmName = "Firm A",
                Year = 2026,
                Month = 8,
                Name = "Fuel",
                Amount = 100m
            });

            _salaryDbService.UpsertFirmExpense(2026, 8, new FirmExpense
            {
                Id = "exp-1",
                FirmName = "Firm A",
                Year = 2026,
                Month = 8,
                Name = "Fuel Updated",
                Amount = 250m
            });

            var (_, expenses) = _salaryDbService.LoadMonthPayments(2026, 8);
            var stored = Assert.Single(expenses);
            Assert.Equal("exp-1", stored.Id);
            Assert.Equal("Fuel Updated", stored.Name);
            Assert.Equal(250m, stored.Amount);
        }

        [Fact]
        public void DeleteFirmExpense_ShouldRemoveOnlyMatchingExpense()
        {
            _salaryDbService.ReplaceMonthData(2026, 9,
                new List<SalaryEntry>(),
                new List<FirmExpense>
                {
                    new() { Id = "exp-1", FirmName = "Firm A", Year = 2026, Month = 9, Name = "Fuel", Amount = 100m },
                    new() { Id = "exp-2", FirmName = "Firm B", Year = 2026, Month = 9, Name = "Hotel", Amount = 200m }
                });

            var deleted = _salaryDbService.DeleteFirmExpense(2026, 9, "exp-1");

            Assert.True(deleted);
            var (_, expenses) = _salaryDbService.LoadMonthPayments(2026, 9);
            var remaining = Assert.Single(expenses);
            Assert.Equal("exp-2", remaining.Id);
        }

        [Fact]
        public void ReplaceFirmExpensesForFirm_ShouldAffectOnlyTargetFirm()
        {
            _salaryDbService.ReplaceMonthData(2026, 10,
                new List<SalaryEntry>(),
                new List<FirmExpense>
                {
                    new() { Id = "exp-a1", FirmName = "Firm A", Year = 2026, Month = 10, Name = "Fuel", Amount = 100m },
                    new() { Id = "exp-b1", FirmName = "Firm B", Year = 2026, Month = 10, Name = "Hotel", Amount = 200m }
                });

            _salaryDbService.ReplaceFirmExpensesForFirm(2026, 10, "Firm A",
                new List<FirmExpense>
                {
                    new() { Id = "exp-a2", FirmName = "Firm A", Year = 2026, Month = 10, Name = "Parking", Amount = 300m }
                });

            var (_, expenses) = _salaryDbService.LoadMonthPayments(2026, 10);
            Assert.Equal(2, expenses.Count);
            Assert.Contains(expenses, e => e.Id == "exp-a2" && e.FirmName == "Firm A");
            Assert.Contains(expenses, e => e.Id == "exp-b1" && e.FirmName == "Firm B");
            Assert.DoesNotContain(expenses, e => e.Id == "exp-a1");
        }

        [Fact]
        public void RemapCustomFieldIdAcrossMonths_ShouldMoveStoredCustomValuesToLegacyId()
        {
            _salaryDbService.ReplaceMonthData(2026, 11,
                new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-1",
                        EmployeeFolder = @"C:\Employees\John",
                        FullName = "John Doe",
                        FirmName = "Firm A",
                        SavedNetSalary = 1000m,
                        Status = "paid",
                        CustomValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["sqlite-id"] = 333m
                        }
                    }
                },
                new List<FirmExpense>());

            var updatedRows = _salaryDbService.RemapCustomFieldIdAcrossMonths("sqlite-id", "legacy-id");

            Assert.Equal(1, updatedRows);

            var (entries, _) = _salaryDbService.LoadMonthPayments(2026, 11);
            var entry = Assert.Single(entries);
            Assert.True(entry.CustomValues.ContainsKey("legacy-id"));
            Assert.Equal(333m, entry.CustomValues["legacy-id"]);
            Assert.False(entry.CustomValues.ContainsKey("sqlite-id"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}

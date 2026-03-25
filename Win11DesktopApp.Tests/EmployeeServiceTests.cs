using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly FolderService _folderService;
        private readonly EmployeeService _employeeService;

        public EmployeeServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorEmployeesTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _tagCatalogService = new TagCatalogService();
            _folderService = new FolderService(_appSettingsService);
            _employeeService = new EmployeeService(_appSettingsService, _tagCatalogService, _folderService);
        }

        [Fact]
        public void GetEmployeesForFirm_ShouldReturnEmployeesFromJson()
        {
            var firmName = "TestFirm";
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);

            var employeeFolder = Path.Combine(employeesFolder, "John_Doe - 2026-03-01");
            Directory.CreateDirectory(employeeFolder);

            var data = new EmployeeData
            {
                FirstName = "John",
                LastName = "Doe",
                StartDate = "2026-03-01",
                PositionTag = "Welder"
            };

            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));

            var list = _employeeService.GetEmployeesForFirm(firmName);

            Assert.Single(list);
            Assert.Equal("John Doe", list[0].FullName);
        }

        [Fact]
        public void EmployeesViewModel_ShouldLoadEmployees()
        {
            var firmName = "TestFirm";
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);

            var employeeFolder = Path.Combine(employeesFolder, "Ann_Smith - 2026-03-01");
            Directory.CreateDirectory(employeeFolder);

            var data = new EmployeeData
            {
                FirstName = "Ann",
                LastName = "Smith",
                StartDate = "2026-03-01",
                PositionTag = "Operator"
            };

            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));

            var company = new EmployerCompany { Name = firmName };
            var vm = new EmployeesViewModel(company, _employeeService);

            Assert.True(vm.Employees.Count >= 1);
        }

        [Fact]
        public void EmployeesViewModel_ShouldFilterBySearchQuery()
        {
            var firmName = "TestFirm";
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);

            var folder1 = Path.Combine(employeesFolder, "Ann_Smith - 2026-03-01");
            Directory.CreateDirectory(folder1);
            var data1 = new EmployeeData { FirstName = "Ann", LastName = "Smith", PassportNumber = "AA111" };
            File.WriteAllText(Path.Combine(folder1, "employee.json"), JsonSerializer.Serialize(data1));

            var folder2 = Path.Combine(employeesFolder, "Bob_Jones - 2026-03-01");
            Directory.CreateDirectory(folder2);
            var data2 = new EmployeeData { FirstName = "Bob", LastName = "Jones", PassportNumber = "BB222" };
            File.WriteAllText(Path.Combine(folder2, "employee.json"), JsonSerializer.Serialize(data2));

            var company = new EmployerCompany { Name = firmName };
            var vm = new EmployeesViewModel(company, _employeeService);

            vm.SearchQuery = "AA111";

            Assert.Single(vm.Employees);
        }
        [Fact]
        public void GetEmployeesForFirmWithStatus_ShouldReturnEmptyStatus_WhenNoEmployees()
        {
            var firmName = "TestFirm";
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);

            var result = _employeeService.GetEmployeesForFirmWithStatus(firmName);

            Assert.Equal("NoEmployees", result.Status);
            Assert.Empty(result.Employees);
        }

        [Fact]
        public async Task AddHistoryEntry_ShouldStoreActorNameFromCurrentProfile()
        {
            var employeeFolder = Path.Combine(_testRootPath, "TestFirm", "Employees", "John_Doe - 2026-03-01");
            Directory.CreateDirectory(employeeFolder);

            App.SetCurrentProfile(new ClientProfileRecord
            {
                FirstName = "Ivan",
                LastName = "Petrenko"
            });

            try
            {
                await _employeeService.AddHistoryEntry(employeeFolder, new EmployeeHistoryEntry
                {
                    EventType = "ProfileChanged",
                    Action = "Updated",
                    Description = "Changed phone"
                });

                var history = _employeeService.LoadHistory(employeeFolder);
                Assert.Single(history);
                Assert.Equal("Ivan Petrenko", history[0].ActorName);
            }
            finally
            {
                App.SetCurrentProfile(null);
            }
        }

        [Fact]
        public async Task ArchiveEmployee_ShouldReturnOperationId_AndWriteArchiveLogEntry()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "John", "Doe", "2026-03-01", uniqueId: "emp-1");

            var result = await _employeeService.ArchiveEmployee(employeeFolder, firmName, "2026-03-20");

            Assert.True(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.OperationId));

            var archiveLog = _employeeService.LoadArchiveLog();
            var logEntry = Assert.Single(archiveLog);
            Assert.Equal(result.OperationId, logEntry.OperationId);
            Assert.Equal("Archived", logEntry.Action);
            Assert.False(logEntry.IsReverted);
        }

        [Fact]
        public async Task UndoArchiveAsync_ShouldRestoreEmployee_AndMarkArchiveEntryReverted()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Anna", "Smith", "2026-03-01", uniqueId: "emp-2");

            var archiveResult = await _employeeService.ArchiveEmployee(employeeFolder, firmName, "2026-03-20");
            Assert.True(archiveResult.Success);

            var undoResult = await _employeeService.UndoArchiveAsync(archiveResult.OperationId);

            Assert.True(undoResult.Success);
            Assert.False(string.IsNullOrWhiteSpace(undoResult.UndoOperationId));
            Assert.True(Directory.Exists(undoResult.RestoredFolder));

            var restoredData = _employeeService.LoadEmployeeData(undoResult.RestoredFolder);
            Assert.NotNull(restoredData);
            Assert.False(restoredData!.IsArchived);
            Assert.Equal("Active", restoredData.Status);
            Assert.Equal(string.Empty, restoredData.EndDate);

            var restoredEmployees = _employeeService.GetEmployeesForFirm(firmName);
            Assert.Single(restoredEmployees);
            Assert.Equal("emp-2", restoredEmployees[0].UniqueId);

            var archiveLog = _employeeService.LoadArchiveLog();
            var archivedEntry = Assert.Single(archiveLog, entry => entry.Action == "Archived");
            Assert.True(archivedEntry.IsReverted);
            Assert.Equal(undoResult.UndoOperationId, archivedEntry.RevertedByOperationId);
            Assert.False(string.IsNullOrWhiteSpace(archivedEntry.RevertedAt));

            var history = _employeeService.LoadHistory(undoResult.RestoredFolder);
            Assert.Contains(history, entry => entry.EventType == "ArchiveUndone");
        }

        private string CreateEmployee(string firmName, string firstName, string lastName, string startDate, string uniqueId)
        {
            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            Directory.CreateDirectory(employeesFolder);

            var employeeFolder = Path.Combine(employeesFolder, $"{firstName}_{lastName} - {startDate}");
            Directory.CreateDirectory(employeeFolder);

            var data = new EmployeeData
            {
                UniqueId = uniqueId,
                FirstName = firstName,
                LastName = lastName,
                StartDate = startDate,
                ContractSignDate = startDate,
                PositionTag = "Operator",
                Status = "Active",
                FirmHistory = new()
                {
                    new FirmHistoryEntry
                    {
                        FirmName = firmName,
                        StartDate = startDate,
                        EndDate = string.Empty
                    }
                }
            };

            File.WriteAllText(Path.Combine(employeeFolder, "employee.json"), JsonSerializer.Serialize(data));
            return employeeFolder;
        }

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}

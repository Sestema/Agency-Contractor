using System;
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

        public void Dispose()
        {
            try { Directory.Delete(_testRootPath, true); } catch { }
        }
    }
}

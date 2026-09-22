using System;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly EmployeeIndexDbService _employeeIndexDbService;
        private readonly CurrentProfileService _currentProfileService;
        private readonly EmployeeService _employeeService;
        private readonly NavigationService _navigationService;
        private readonly ProfileAuthService _profileAuthService;
        private readonly RecentlyDeletedService _recentlyDeletedService;
        private readonly GeminiApiService _geminiApiService;
        private readonly AddEmployeeWizardViewModelFactory _addEmployeeWizardViewModelFactory;
        private readonly DocumentLocalizationService _documentLocalizationService;
        private readonly EmployeeDetailsViewModelFactory _employeeDetailsViewModelFactory;
        private readonly ActivityLogService _activityLogService;
        private readonly TemplateService _templateService;
        private readonly DocumentGenerationService _documentGenerationService;
        private readonly PersistenceService _persistenceService;
        private readonly CompanyService _companyService;
        private readonly FinanceService _financeService;
        private readonly AiWindowFactory _aiWindowFactory;
        private readonly AppStatisticsService _appStatisticsService;

        public EmployeeServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorEmployeesTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _documentLocalizationService = new DocumentLocalizationService();
            _tagCatalogService = new TagCatalogService(_documentLocalizationService);
            _folderService = new FolderService(_appSettingsService);
            _employeeIndexDbService = new EmployeeIndexDbService(_folderService);
            _currentProfileService = new CurrentProfileService();
            _navigationService = new NavigationService(new ServiceCollection().BuildServiceProvider());
            _profileAuthService = new ProfileAuthService();
            _employeeService = new EmployeeService(
                _appSettingsService,
                _tagCatalogService,
                _folderService,
                employeeIndexDbService: _employeeIndexDbService,
                currentProfileService: _currentProfileService);
            _recentlyDeletedService = new RecentlyDeletedService(_folderService, _employeeService, _currentProfileService);
            _geminiApiService = new GeminiApiService();
            _activityLogService = new ActivityLogService(_folderService, currentProfileService: _currentProfileService);
            _appStatisticsService = new AppStatisticsService(_folderService);
            _addEmployeeWizardViewModelFactory = new AddEmployeeWizardViewModelFactory(_employeeService, _geminiApiService, _activityLogService, _appStatisticsService);
            _templateService = new TemplateService(_appSettingsService, _folderService, _tagCatalogService);
            _documentGenerationService = new DocumentGenerationService();
            _persistenceService = new PersistenceService(_appSettingsService, _folderService);
            _companyService = new CompanyService(_tagCatalogService, _appSettingsService, _persistenceService, _folderService);
            _financeService = new FinanceService(
                _folderService,
                companyService: _companyService,
                employeeIndexDbService: _employeeIndexDbService);
            _aiWindowFactory = new AiWindowFactory(_geminiApiService, _employeeService);
            _employeeDetailsViewModelFactory = new EmployeeDetailsViewModelFactory(
                _employeeService,
                _geminiApiService,
                _financeService,
                _appSettingsService,
                _activityLogService,
                _companyService,
                _documentLocalizationService,
                _templateService,
                _documentGenerationService,
                _tagCatalogService,
                _aiWindowFactory,
                _appStatisticsService,
                _currentProfileService,
                _profileAuthService,
                _recentlyDeletedService);
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
            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            var list = _employeeService.GetEmployeesForFirm(firmName);

            Assert.Single(list);
            Assert.Equal("John Doe", list[0].FullName);
        }

        [Fact]
        public async Task EmployeesViewModel_ShouldLoadEmployees()
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
            var vm = new EmployeesViewModel(
                company,
                _employeeService,
                _addEmployeeWizardViewModelFactory,
                _navigationService,
                _currentProfileService,
                _profileAuthService,
                _recentlyDeletedService,
                _appSettingsService,
                _documentLocalizationService,
                _employeeDetailsViewModelFactory,
                _activityLogService,
                _templateService,
                _documentGenerationService,
                _tagCatalogService,
                _geminiApiService);

            await WaitForAsync(() => !vm.IsLoading && vm.Employees.Count >= 1, timeoutMs: 10000);
            Assert.True(vm.Employees.Count >= 1);
        }

        [Fact]
        public void GetEmployeesForFirm_SearchFieldsSurviveCleanLoad()
        {
            var firmName = "FilterFirm_" + Guid.NewGuid().ToString("N");
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);

            var folder1 = Path.Combine(employeesFolder, "Ann_Smith - 2026-03-01");
            Directory.CreateDirectory(folder1);
            var data1 = new EmployeeData { UniqueId = "filter-ann", FirstName = "Ann", LastName = "Smith", PassportNumber = "AA111" };
            File.WriteAllText(Path.Combine(folder1, "employee.json"), JsonSerializer.Serialize(data1));
            _employeeService.SyncEmployeeIndexForFolder(folder1, firmName);

            var folder2 = Path.Combine(employeesFolder, "Bob_Jones - 2026-03-01");
            Directory.CreateDirectory(folder2);
            var data2 = new EmployeeData { UniqueId = "filter-bob", FirstName = "Bob", LastName = "Jones", PassportNumber = "BB222" };
            File.WriteAllText(Path.Combine(folder2, "employee.json"), JsonSerializer.Serialize(data2));
            _employeeService.SyncEmployeeIndexForFolder(folder2, firmName);

            var listed = _employeeService.GetEmployeesForFirm(firmName);
            Assert.Equal(2, listed.Count);
            Assert.Single(listed, e =>
                !string.IsNullOrEmpty(e.PassportNumber)
                && e.PassportNumber.Contains("AA111", StringComparison.OrdinalIgnoreCase));
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
        public void RebuildEmployeeIndex_ShouldKeepExistingRows_WhenScanIsIncomplete()
        {
            var firmName = "TestFirm";
            var firstFolder = CreateEmployee(firmName, "Keep", "One", "2026-03-01", uniqueId: "emp-rebuild-keep-1");
            var secondFolder = CreateEmployee(firmName, "Keep", "Two", "2026-03-02", uniqueId: "emp-rebuild-keep-2");
            _employeeService.SyncEmployeeIndexForFolder(firstFolder, firmName);
            _employeeService.SyncEmployeeIndexForFolder(secondFolder, firmName);

            Directory.Delete(secondFolder, recursive: true);

            var result = _employeeService.RebuildEmployeeIndex();
            var rows = _employeeIndexDbService.GetEmployeesForFirmRows(firmName);

            Assert.False(result.WasRebuildAttempted);
            Assert.True(result.IsSuccessful);
            Assert.Equal(2, _employeeIndexDbService.GetEmployeeIndexCount());
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.UniqueId == "emp-rebuild-keep-1");
            Assert.Contains(rows, row => row.UniqueId == "emp-rebuild-keep-2");
        }

        [Fact]
        public void RebuildEmployeeIndex_ShouldReplaceIndex_WhenScanCoversExistingIds()
        {
            var firmName = "TestFirm";
            var firstFolder = CreateEmployee(firmName, "Keep", "One", "2026-03-01", uniqueId: "emp-rebuild-full-1");
            var secondFolder = CreateEmployee(firmName, "Keep", "Two", "2026-03-02", uniqueId: "emp-rebuild-full-2");
            _employeeService.SyncEmployeeIndexForFolder(firstFolder, firmName);
            _employeeService.SyncEmployeeIndexForFolder(secondFolder, firmName);

            var existingRows = _employeeIndexDbService.GetEmployeesForFirmRows(firmName);
            existingRows[0].FullName = "Updated One";

            var result = _employeeIndexDbService.RebuildEmployeeIndex(existingRows);
            var rows = _employeeIndexDbService.GetEmployeesForFirmRows(firmName);

            Assert.True(result.WasRebuildAttempted);
            Assert.True(result.IsSuccessful);
            Assert.Equal(2, result.RecordsImported);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row.UniqueId == "emp-rebuild-full-1" && row.FullName == "Updated One");
            Assert.Contains(rows, row => row.UniqueId == "emp-rebuild-full-2");
        }

        [Fact]
        public void GetEmployeesForFirm_ShouldReturnIndexRows_WhenEmployeesFolderIsMissing()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Index", "User", "2026-03-01", uniqueId: "emp-missing-folder-1");
            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            Directory.Delete(employeesFolder, recursive: true);

            var list = _employeeService.GetEmployeesForFirm(firmName);
            var status = _employeeService.GetEmployeesForFirmWithStatus(firmName);

            var employee = Assert.Single(list);
            Assert.Equal("emp-missing-folder-1", employee.UniqueId);
            Assert.Equal("Index User", employee.FullName);
            Assert.Equal("EmployeesFolderMissing", status.Status);
            Assert.Single(status.Employees);
            Assert.Equal("emp-missing-folder-1", status.Employees[0].UniqueId);
        }

        [Fact]
        public void GetEmployeesForFirm_ShouldReadFromEmployeeIndex_WhenJsonIsMissingAfterRebuild()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Index", "User", "2026-03-01", uniqueId: "emp-index-1");

            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            File.Delete(Path.Combine(employeeFolder, "employee.json"));

            var list = _employeeService.GetEmployeesForFirm(firmName);

            Assert.Single(list);
            Assert.Equal("emp-index-1", list[0].UniqueId);
            Assert.Equal("Index User", list[0].FullName);
        }

        [Fact]
        public void GetEmployeesForFirm_ShouldReadPhotoFromEmployeeIndex()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployeeWithPhoto(firmName, "John", "Doe", "2026-03-01", uniqueId: "emp-photo-1");

            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);
            File.Delete(Path.Combine(employeeFolder, "employee.json"));

            var list = _employeeService.GetEmployeesForFirm(firmName);

            var employee = Assert.Single(list);
            Assert.Equal("emp-photo-1", employee.UniqueId);
            Assert.True(employee.HasPhoto);
            Assert.True(File.Exists(employee.PhotoPath));
            Assert.EndsWith("John Doe - Photo.jpg", employee.PhotoPath, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GetEmployeesForFirm_ShouldResolvePhotoAfterRootFolderMove()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployeeWithPhoto(firmName, "Jane", "Roe", "2026-03-01", uniqueId: "emp-photo-move-1");

            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            var movedRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorEmployeesTests_Moved_" + Guid.NewGuid());
            CopyDirectory(_testRootPath, movedRootPath);

            try
            {
                var movedSettings = new AppSettingsService();
                movedSettings.Settings.RootFolderPath = movedRootPath;
                var movedDocumentLocalizationService = new DocumentLocalizationService();
                var movedTagCatalog = new TagCatalogService(movedDocumentLocalizationService);
                var movedFolderService = new FolderService(movedSettings);
                var movedIndexDbService = new EmployeeIndexDbService(movedFolderService);
                var movedCurrentProfileService = new CurrentProfileService();
                var movedEmployeeService = new EmployeeService(
                    movedSettings,
                    movedTagCatalog,
                    movedFolderService,
                    employeeIndexDbService: movedIndexDbService,
                    currentProfileService: movedCurrentProfileService);

                var list = movedEmployeeService.GetEmployeesForFirm(firmName);

                var employee = Assert.Single(list);
                Assert.True(employee.HasPhoto);
                Assert.True(File.Exists(employee.PhotoPath));
                Assert.StartsWith(Path.GetFullPath(movedRootPath), Path.GetFullPath(employee.PhotoPath), StringComparison.OrdinalIgnoreCase);
                Assert.False(
                    Path.GetFullPath(employee.PhotoPath).StartsWith(
                        Path.GetFullPath(_testRootPath),
                        StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(movedRootPath, true); } catch { }
            }
        }

        [Fact]
        public void GetEmployeesForFirm_DoesNotProbeDiskForPhoto_WhenIndexHasNoPhoto()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Petro", "Lys", "2026-03-01", uniqueId: "emp-no-photo-1");
            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            File.WriteAllBytes(
                Path.Combine(employeeFolder, "Petro Lys - Photo.jpg"),
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

            var employee = Assert.Single(_employeeService.GetEmployeesForFirm(firmName));
            Assert.False(employee.HasPhoto);
            Assert.True(string.IsNullOrWhiteSpace(employee.PhotoPath));
        }

        [Fact]
        public void EmployeeListAndProfile_ShouldResolveSamePhotoPath()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployeeWithPhoto(firmName, "Olena", "Zmereha", "2026-03-01", uniqueId: "emp-photo-profile-1");

            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            var employee = Assert.Single(_employeeService.GetEmployeesForFirm(firmName));
            var detailsViewModel = new EmployeeDetailsViewModel(
                firmName,
                employeeFolder,
                _employeeService,
                employeeId: "emp-photo-profile-1",
                geminiApiService: _geminiApiService,
                financeService: _financeService,
                appSettingsService: _appSettingsService,
                activityLogService: _activityLogService,
                companyService: _companyService,
                documentLocalizationService: _documentLocalizationService,
                templateService: _templateService,
                documentGenerationService: _documentGenerationService,
                tagCatalogService: _tagCatalogService,
                aiWindowFactory: _aiWindowFactory,
                appStatisticsService: _appStatisticsService);

            Assert.True(employee.HasPhoto);
            Assert.False(string.IsNullOrWhiteSpace(detailsViewModel.PhotoFilePath));
            Assert.Equal(
                Path.GetFullPath(employee.PhotoPath),
                Path.GetFullPath(detailsViewModel.PhotoFilePath));
        }

        [Fact]
        public async Task GetArchivedEmployees_ShouldReadFromEmployeeIndex_WhenArchivedJsonIsMissing()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Archived", "User", "2026-03-01", uniqueId: "emp-arch-1");

            var archiveResult = await _employeeService.ArchiveEmployee(employeeFolder, firmName, "2026-03-20");
            Assert.True(archiveResult.Success);

            File.Delete(Path.Combine(archiveResult.ArchiveFolder, "employee.json"));

            var archived = _employeeService.GetArchivedEmployees();

            var item = Assert.Single(archived);
            Assert.Equal("emp-arch-1", item.UniqueId);
            Assert.Equal("Archived User", item.FullName);
        }

        [Fact]
        public async Task AddHistoryEntry_ShouldStoreActorNameFromCurrentProfile()
        {
            var employeeFolder = Path.Combine(_testRootPath, "TestFirm", "Employees", "John_Doe - 2026-03-01");
            Directory.CreateDirectory(employeeFolder);

            _currentProfileService.SetCurrentProfile(new ClientProfileRecord
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
                _currentProfileService.SetCurrentProfile(null);
            }
        }

        [Fact]
        public void ArchiveCopyContainsAllSourceFiles_ShouldDetectMissingAndSizeMismatch()
        {
            var source = Path.Combine(_testRootPath, "CopyCheck", "Source");
            var dest = Path.Combine(_testRootPath, "CopyCheck", "Dest");
            Directory.CreateDirectory(Path.Combine(source, "CustomDocs"));
            Directory.CreateDirectory(Path.Combine(dest, "CustomDocs"));
            File.WriteAllText(Path.Combine(source, "employee.json"), "{}");
            File.WriteAllText(Path.Combine(dest, "employee.json"), "{}");
            File.WriteAllBytes(Path.Combine(source, "CustomDocs", "passport.jpg"), new byte[] { 1, 2, 3, 4 });
            File.WriteAllBytes(Path.Combine(dest, "CustomDocs", "passport.jpg"), new byte[] { 1, 2, 3, 4 });

            Assert.True(EmployeeService.ArchiveCopyContainsAllSourceFiles(source, dest, out _));

            File.Delete(Path.Combine(dest, "CustomDocs", "passport.jpg"));
            Assert.False(EmployeeService.ArchiveCopyContainsAllSourceFiles(source, dest, out var missingReason));
            Assert.Contains("passport.jpg", missingReason, StringComparison.OrdinalIgnoreCase);

            File.WriteAllBytes(Path.Combine(dest, "CustomDocs", "passport.jpg"), new byte[] { 1 });
            Assert.False(EmployeeService.ArchiveCopyContainsAllSourceFiles(source, dest, out var sizeReason));
            Assert.Contains("size mismatch", sizeReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RollbackFailedArchive_ShouldRestoreJson_AndRemovePartialCopy()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Rollback", "User", "2026-03-01", uniqueId: "emp-rollback-1");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var originalJson = File.ReadAllText(jsonPath);

            var archivedData = JsonSerializer.Deserialize<EmployeeData>(originalJson);
            Assert.NotNull(archivedData);
            archivedData!.IsArchived = true;
            archivedData.Status = "Dismissed";
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(archivedData));

            var destFolder = Path.Combine(_testRootPath, "Archive", "Rollback_User - 2026-03-01");
            Directory.CreateDirectory(destFolder);
            File.WriteAllText(Path.Combine(destFolder, "employee.json"), "partial");

            await _employeeService.RollbackFailedArchiveAsync(jsonPath, originalJson, destFolder);

            var restored = JsonSerializer.Deserialize<EmployeeData>(File.ReadAllText(jsonPath));
            Assert.NotNull(restored);
            Assert.False(restored!.IsArchived);
            Assert.False(Directory.Exists(destFolder));
        }

        [Fact]
        public async Task RollbackFailedArchive_ShouldKeepArchiveFolder_WhenDestNotPassed()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Keep", "Archive", "2026-03-01", uniqueId: "emp-rollback-keep");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var originalJson = File.ReadAllText(jsonPath);

            var destFolder = Path.Combine(_testRootPath, "Archive", "Keep_Archive - 2026-03-01");
            Directory.CreateDirectory(destFolder);
            File.WriteAllText(Path.Combine(destFolder, "employee.json"), "{\"FirstName\":\"Keep\"}");

            await _employeeService.RollbackFailedArchiveAsync(jsonPath, originalJson, destFolder: null);

            Assert.True(Directory.Exists(destFolder));
            Assert.True(File.Exists(Path.Combine(destFolder, "employee.json")));
        }

        [Fact]
        public async Task ArchiveEmployee_ShouldRemoveSource_WhenAllFilesCopied()
        {
            var firmName = "TestFirm";
            var employeeFolder = CreateEmployee(firmName, "Keep", "Scan", "2026-03-01", uniqueId: "emp-copy-complete");
            var customDocs = Path.Combine(employeeFolder, "CustomDocs");
            Directory.CreateDirectory(customDocs);
            File.WriteAllBytes(Path.Combine(customDocs, "passport.jpg"), new byte[] { 9, 8, 7 });

            var result = await _employeeService.ArchiveEmployee(employeeFolder, firmName, "2026-03-20");

            Assert.True(result.Success);
            Assert.False(Directory.Exists(employeeFolder));
            Assert.True(File.Exists(Path.Combine(result.ArchiveFolder, "CustomDocs", "passport.jpg")));
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

        private string CreateEmployeeWithPhoto(string firmName, string firstName, string lastName, string startDate, string uniqueId)
        {
            var employeeFolder = CreateEmployee(firmName, firstName, lastName, startDate, uniqueId);
            var photoPath = Path.Combine(employeeFolder, $"{firstName} {lastName} - Photo.jpg");
            File.WriteAllBytes(photoPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
            return employeeFolder;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var targetDirectory = Path.Combine(destinationDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetDirectory);
            }
        }

        private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Timed out waiting for background employee load.");

                PumpDispatcherOnce();
                await Task.Delay(25);
            }
        }

        private static void PumpDispatcherOnce()
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        [Fact]
        public void LoadEmployeeData_DoesNotRewriteFile_WhenScanIsMissing()
        {
            var employeeFolder = CreateProfileFolder("Anna", "Koval", "missing-pass.jpg");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var beforeWrite = File.GetLastWriteTimeUtc(jsonPath);
            var beforeText = File.ReadAllText(jsonPath);

            var loaded = _employeeService.LoadEmployeeData(employeeFolder);

            Assert.NotNull(loaded);
            Assert.Equal("missing-pass.jpg", loaded.Files.Passport);
            Assert.Equal(beforeText, File.ReadAllText(jsonPath));
            Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(jsonPath));
        }

        [Fact]
        public void RepairEmployeeProfile_DoesNotClearMissingScanName()
        {
            var employeeFolder = CreateProfileFolder("Oksana", "Melnyk", "onedrive-pass.jpg");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var beforeText = File.ReadAllText(jsonPath);

            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            _employeeService.RepairEmployeeProfile(employeeFolder, data);
            Assert.Equal("onedrive-pass.jpg", data.Files.Passport);
            Assert.Equal(beforeText, File.ReadAllText(jsonPath));
        }

        [Fact]
        public void SaveProfileRepair_PicksUpNewPassportScan_WithoutTreatingPdfAsInsurance()
        {
            var employeeFolder = CreateProfileFolder("Ivan", "Petrenko", passportFileName: "");
            File.WriteAllBytes(Path.Combine(employeeFolder, "Ivan Petrenko - Pass.jpg"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(employeeFolder, "Ivan Petrenko - Contract.pdf"), new byte[] { 4, 5, 6 });

            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            Assert.True(string.IsNullOrEmpty(data.Files.Passport));

            Assert.True(_employeeService.RepairEmployeeProfile(employeeFolder, data));
            Assert.Equal("Ivan Petrenko - Pass.jpg", data.Files.Passport);
            Assert.True(string.IsNullOrEmpty(data.Files.Insurance));
            Assert.True(_employeeService.SaveEmployeeData(employeeFolder, data, notifyUser: false));

            var reloaded = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(reloaded);
            Assert.Equal("Ivan Petrenko - Pass.jpg", reloaded.Files.Passport);
            Assert.True(string.IsNullOrEmpty(reloaded.Files.Insurance));
        }

        [Fact]
        public void RepairEmployeeProfile_StampsMissingCustomDocumentFirm_WithoutWriting()
        {
            var employeeFolder = CreateProfileFolder("Maria", "Hnat", passportFileName: "");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            data.CustomDocuments = new List<CustomSignedDocument>
            {
                new CustomSignedDocument { Name = "Dohoda", FirmName = string.Empty }
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));
            var beforeText = File.ReadAllText(jsonPath);
            var beforeWrite = File.GetLastWriteTimeUtc(jsonPath);

            var loaded = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(loaded);
            Assert.True(_employeeService.RepairEmployeeProfile(employeeFolder, loaded, "RepairFirm"));
            Assert.Equal("RepairFirm", loaded.CustomDocuments.Single().FirmName);
            Assert.Equal(beforeText, File.ReadAllText(jsonPath));
            Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(jsonPath));

            Assert.True(_employeeService.SaveEmployeeData(employeeFolder, loaded, notifyUser: false));
            var saved = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(saved);
            Assert.Equal("RepairFirm", saved.CustomDocuments.Single().FirmName);
        }

        [Fact]
        public void RepairEmployeeProfile_DoesNotStampFirmFromFolderPath_WhenFirmUnknown()
        {
            var employeeFolder = CreateProfileFolder("Taras", "Bondar", passportFileName: "");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            data.CustomDocuments = new List<CustomSignedDocument>
            {
                new CustomSignedDocument { Name = "Dohoda", FirmName = string.Empty }
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));

            var loaded = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(loaded);
            _employeeService.RepairEmployeeProfile(employeeFolder, loaded);
            Assert.True(string.IsNullOrWhiteSpace(loaded.CustomDocuments.Single().FirmName));
            Assert.DoesNotContain("RepairFirm", File.ReadAllText(jsonPath), StringComparison.Ordinal);
        }

        [Fact]
        public void SyncEmployeeIndexForFolder_DoesNotCreatePhantomUniqueId()
        {
            var firmName = "PhantomFirm_" + Guid.NewGuid().ToString("N");
            var employeesFolder = Path.Combine(_testRootPath, firmName, "Employees");
            Directory.CreateDirectory(employeesFolder);
            var employeeFolder = Path.Combine(employeesFolder, "Petro_Lys - 01.01.2026");
            Directory.CreateDirectory(employeeFolder);
            var data = new EmployeeData
            {
                FirstName = "Petro",
                LastName = "Lys",
                StartDate = "01.01.2026"
            };
            File.WriteAllText(Path.Combine(employeeFolder, "employee.json"), JsonSerializer.Serialize(data));

            _employeeService.SyncEmployeeIndexForFolder(employeeFolder, firmName);

            Assert.Empty(_employeeIndexDbService.GetEmployeesForFirmRows(firmName));
            var reloaded = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(reloaded);
            Assert.True(string.IsNullOrWhiteSpace(reloaded.UniqueId));
        }

        [Fact]
        public void RenameFirmReferencesInEmployeeProfiles_RewritesPackageAndSignedDocs_WithoutRenamingFolder()
        {
            var employeeFolder = CreateProfileFolder("Hanna", "Shelever", passportFileName: "");
            var folderName = Path.GetFileName(employeeFolder);
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            data.CustomDocuments = new List<CustomSignedDocument>
            {
                new CustomSignedDocument { Name = "Dohoda", FirmName = "Old Firm s.r.o.", FileName = "dohoda.pdf" }
            };
            data.RequiredDocumentStatuses = new List<RequiredDocumentStatus>
            {
                new RequiredDocumentStatus
                {
                    FirmName = "Old Firm s.r.o.",
                    TemplateId = "pracovni",
                    NameSnapshot = "Pracovní smlouva",
                    TrackPresence = true,
                    TrackScanned = true,
                    IsPresent = true,
                    IsScanned = true
                }
            };
            data.FirmHistory = new List<FirmHistoryEntry>
            {
                new FirmHistoryEntry { FirmName = "Old Firm s.r.o.", StartDate = "01.01.2026" }
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));

            var otherFolder = Path.Combine(_testRootPath, "OtherFirm", "Employees", "Ivan_Petrenko - 01.01.2026");
            Directory.CreateDirectory(otherFolder);
            File.WriteAllText(Path.Combine(otherFolder, "employee.json"), JsonSerializer.Serialize(new EmployeeData
            {
                UniqueId = "other-keep",
                FirstName = "Ivan",
                LastName = "Petrenko",
                CustomDocuments =
                {
                    new CustomSignedDocument { Name = "Keep", FirmName = "OtherFirm" }
                },
                FirmHistory =
                {
                    new FirmHistoryEntry { FirmName = "Old Firm s.r.o.", StartDate = "01.01.2025" },
                    new FirmHistoryEntry { FirmName = "OtherFirm", StartDate = "01.01.2026" }
                }
            }));

            var updated = _employeeService.RenameFirmReferencesInEmployeeProfiles("Old Firm s.r.o.", "New Firm s.r.o.");
            Assert.Equal(2, updated);
            Assert.Equal(folderName, Path.GetFileName(employeeFolder));
            Assert.True(Directory.Exists(employeeFolder));

            var rewritten = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(rewritten);
            Assert.Equal("New Firm s.r.o.", rewritten.CustomDocuments.Single().FirmName);
            Assert.Equal("dohoda.pdf", rewritten.CustomDocuments.Single().FileName);
            var package = rewritten.RequiredDocumentStatuses.Single();
            Assert.Equal("New Firm s.r.o.", package.FirmName);
            Assert.True(package.IsPresent);
            Assert.True(package.IsScanned);
            Assert.Equal("New Firm s.r.o.", rewritten.FirmHistory.Single().FirmName);

            var other = _employeeService.LoadEmployeeData(otherFolder);
            Assert.NotNull(other);
            Assert.Equal("OtherFirm", other.CustomDocuments.Single().FirmName);
            Assert.Equal("New Firm s.r.o.", other.FirmHistory[0].FirmName);
            Assert.Equal("OtherFirm", other.FirmHistory[1].FirmName);
        }

        [Fact]
        public void IsDocumentIgnored_ParsesEuropeanAndIsoDates()
        {
            var employeeFolder = CreateProfileFolder("Snooze", "Test", passportFileName: "");
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var data = _employeeService.LoadEmployeeData(employeeFolder);
            Assert.NotNull(data);
            data.IgnoredDocuments = new Dictionary<string, string>
            {
                ["passport"] = "31.12.2099",
                ["visa"] = "2020-01-01"
            };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(data));

            Assert.True(_employeeService.IsDocumentIgnored(employeeFolder, "passport"));
            Assert.False(_employeeService.IsDocumentIgnored(employeeFolder, "visa"));
            Assert.True(EmployeeService.IsIgnoredUntilActive("03.09.2099"));
            Assert.True(EmployeeService.IsIgnoredUntilActive("2099-09-03"));
        }

        private string CreateProfileFolder(string firstName, string lastName, string passportFileName)
        {
            var employeeFolder = Path.Combine(_testRootPath, "RepairFirm", "Employees", $"{firstName}_{lastName} - 01.01.2026");
            Directory.CreateDirectory(employeeFolder);
            var data = new EmployeeData
            {
                UniqueId = Guid.NewGuid().ToString(),
                FirstName = firstName,
                LastName = lastName,
                StartDate = "01.01.2026",
                Files = { Passport = passportFileName }
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

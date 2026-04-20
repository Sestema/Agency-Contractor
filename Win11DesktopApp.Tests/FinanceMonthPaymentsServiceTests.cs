using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class FinanceMonthPaymentsServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly SalaryDbService _salaryDbService;
        private readonly FinanceMonthPaymentsService _service;

        public FinanceMonthPaymentsServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorMonthPaymentsTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _folderService = new FolderService(_appSettingsService);
            _salaryDbService = new SalaryDbService(_folderService);
            _service = new FinanceMonthPaymentsService(_folderService, _salaryDbService, () => { }, () => { }, _ => { });
        }

        [Fact]
        public void TryLoadAllFirmPayments_WhenMonthDbResolutionFails_ShouldReportFailure_NotEmptyMonth()
        {
            var salaryDbFolder = _folderService.GetSalaryDbFolder();
            Directory.CreateDirectory(salaryDbFolder);

            File.WriteAllText(Path.Combine(salaryDbFolder, "salary_2026_04_a.db"), string.Empty);
            File.WriteAllText(Path.Combine(salaryDbFolder, "salary_2026_04_b.db"), string.Empty);

            var result = _service.TryLoadAllFirmPayments(2026, 4);

            Assert.False(result.success);
            Assert.NotEmpty(result.errorMessage);
            Assert.Empty(result.entries);
            Assert.Empty(result.expenses);
        }

        [Fact]
        public void TryLoadAllFirmPayments_WhenMonthDoesNotExist_ShouldReportSuccessWithEmptyData()
        {
            var result = _service.TryLoadAllFirmPayments(2026, 4);

            Assert.True(result.success);
            Assert.Equal(string.Empty, result.errorMessage);
            Assert.Empty(result.entries);
            Assert.Empty(result.expenses);
        }

        [Fact]
        public void SaveAllFirmPayments_WhenSalaryDbServiceIsMissing_ShouldReturnFalse()
        {
            var service = new FinanceMonthPaymentsService(_folderService, null, () => { }, () => { }, _ => { });

            var result = service.SaveAllFirmPayments(2026, 4, new List<SalaryEntry>(), new List<FirmExpense>());

            Assert.False(result);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_testRootPath, true);
            }
            catch
            {
            }
        }
    }
}

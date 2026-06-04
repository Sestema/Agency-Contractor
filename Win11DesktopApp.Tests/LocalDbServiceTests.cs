using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class LocalDbServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly FolderService _folderService;
        private readonly SalaryDbService _salaryDbService;
        private readonly LocalDbService _localDbService;

        public LocalDbServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorLocalDbTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _folderService = new FolderService(_appSettingsService);
            _salaryDbService = new SalaryDbService(_folderService);
            _localDbService = new LocalDbService(_folderService, _salaryDbService);
        }

        [Fact]
        public void EnsureInitialized_ShouldCreateCoreTables()
        {
            _localDbService.EnsureInitialized();

            Assert.True(File.Exists(_localDbService.DatabasePath));

            using var connection = _localDbService.OpenConnection();
            Assert.True(TableExists(connection, "custom_salary_fields"));
            Assert.True(TableExists(connection, "advances"));
            Assert.True(TableExists(connection, "migration_journal"));
        }

        [Fact]
        public void UpsertCustomSalaryField_ThenGetCustomSalaryFields_ShouldRoundTrip()
        {
            var field = new CustomSalaryField
            {
                Id = "field-1",
                Name = "Bonus",
                FirmName = "Firm A",
                Operation = FieldOperation.Add,
                Order = 3
            };

            _localDbService.UpsertCustomSalaryField(field);

            var fields = _localDbService.GetCustomSalaryFields();
            var stored = Assert.Single(fields);
            Assert.Equal("field-1", stored.Id);
            Assert.Equal("Bonus", stored.Name);
            Assert.Equal("Firm A", stored.FirmName);
            Assert.Equal(FieldOperation.Add, stored.Operation);
            Assert.Equal(3, stored.Order);
        }

        [Fact]
        public void ParseDecimal_WhenValueIsInvalid_ShouldLogWarning_AndReturnZero()
        {
            LoggingService.Initialize(_testRootPath);
            var parseMethod = typeof(LocalDbService).GetMethod("ParseDecimal", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(parseMethod);

            var result = (decimal)parseMethod!.Invoke(null, new object?[] { "bad-decimal" })!;

            Assert.Equal(0m, result);
            Assert.Contains(LoggingService.GetRecentEntries(), entry =>
                entry.Module == "LocalDbService.ParseDecimal"
                && entry.Severity == "WARN"
                && entry.Message.Contains("bad-decimal", StringComparison.Ordinal));
        }

        private static bool TableExists(SqliteConnection connection, string tableName)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = @name;";
            command.Parameters.AddWithValue("@name", tableName);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
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

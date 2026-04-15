using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly LocalDbService _localDbService;

        public LocalDbServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorLocalDbTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _folderService = new FolderService(_appSettingsService);
            _localDbService = new LocalDbService(_folderService);
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
        public void MigrateCustomFieldsIfNeeded_ShouldImportOnlyMissingFields_AndMarkCompleted()
        {
            _localDbService.UpsertCustomSalaryField(new CustomSalaryField
            {
                Id = "existing-id",
                Name = "Bonus",
                FirmName = "Firm A",
                Operation = FieldOperation.Add,
                Order = 1
            });

            var result = _localDbService.MigrateCustomFieldsIfNeeded(new[]
            {
                new CustomSalaryField
                {
                    Id = "legacy-duplicate",
                    Name = "Bonus",
                    FirmName = "Firm A",
                    Operation = FieldOperation.Add,
                    Order = 5
                },
                new CustomSalaryField
                {
                    Id = "legacy-new",
                    Name = "Transport",
                    FirmName = "Firm A",
                    Operation = FieldOperation.Subtract,
                    Order = 2
                }
            });

            Assert.True(result.WasMigrationAttempted);
            Assert.True(result.IsSuccessful);
            Assert.Equal(2, result.RecordsFound);
            Assert.Equal(2, result.RecordsImported);
            Assert.True(_localDbService.IsCustomFieldsMigrationCompleted());

            var fields = _localDbService.GetCustomSalaryFields()
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Assert.Equal(2, fields.Count);
            Assert.Contains(fields, f => f.Id == "legacy-duplicate" && f.Name == "Bonus");
            Assert.Contains(fields, f => f.Id == "legacy-new" && f.Name == "Transport");
        }

        [Fact]
        public void MigrateCustomFieldsIfNeeded_WithMissingLegacyId_ShouldReuseExistingFieldId()
        {
            _localDbService.UpsertCustomSalaryField(new CustomSalaryField
            {
                Id = "stable-id",
                Name = "Bonus",
                FirmName = "Firm A",
                Operation = FieldOperation.Add,
                Order = 1
            });

            var result = _localDbService.MigrateCustomFieldsIfNeeded(new[]
            {
                new CustomSalaryField
                {
                    Id = string.Empty,
                    Name = "Bonus",
                    FirmName = "Firm A",
                    Operation = FieldOperation.Add,
                    Order = 99
                }
            });

            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.RecordsFound);
            Assert.Equal(0, result.RecordsImported);

            var fields = _localDbService.GetCustomSalaryFields();
            var stored = Assert.Single(fields);
            Assert.Equal("stable-id", stored.Id);
            Assert.Equal("Bonus", stored.Name);
        }

        [Fact]
        public void MigrateCustomFieldsIfNeeded_WithSemanticMatchAndDifferentLegacyId_ShouldPreferLegacyId_AndRemapStoredValues()
        {
            _localDbService.UpsertCustomSalaryField(new CustomSalaryField
            {
                Id = "sqlite-id",
                Name = "Bonus",
                FirmName = "Firm A",
                Operation = FieldOperation.Add,
                Order = 1
            });

            _localDbService.UpsertSalaryReport(new MonthlySalaryReport
            {
                CompanyId = "Firm A",
                CompanyName = "Firm A",
                Year = 2026,
                Month = 3,
                Entries = new List<SalaryEntry>
                {
                    new()
                    {
                        EmployeeId = "emp-1",
                        EmployeeFolder = @"C:\Employees\John",
                        FullName = "John Doe",
                        FirmName = "Firm A",
                        CustomValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["sqlite-id"] = 125m
                        }
                    }
                }
            });

            _localDbService.UpsertSalaryHistoryRecord("emp-1", @"C:\Employees\John", new SalaryHistoryRecord
            {
                Id = "hist-1",
                Year = 2026,
                Month = 3,
                FirmName = "Firm A",
                FullName = "John Doe",
                CustomValues = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sqlite-id"] = 75m
                }
            });

            var result = _localDbService.MigrateCustomFieldsIfNeeded(new[]
            {
                new CustomSalaryField
                {
                    Id = "legacy-id",
                    Name = "Bonus",
                    FirmName = "Firm A",
                    Operation = FieldOperation.Add,
                    Order = 5
                }
            });

            Assert.True(result.IsSuccessful);
            Assert.Equal(1, result.RecordsFound);
            Assert.Equal(1, result.RecordsImported);

            var storedField = Assert.Single(_localDbService.GetCustomSalaryFields());
            Assert.Equal("legacy-id", storedField.Id);
            Assert.Equal("Bonus", storedField.Name);

            var report = _localDbService.GetSalaryReport("Firm A", 2026, 3);
            Assert.NotNull(report);
            var entry = Assert.Single(report!.Entries);
            Assert.True(entry.CustomValues.ContainsKey("legacy-id"));
            Assert.Equal(125m, entry.CustomValues["legacy-id"]);
            Assert.False(entry.CustomValues.ContainsKey("sqlite-id"));

            var history = _localDbService.GetSalaryHistory("emp-1", @"C:\Employees\John");
            var historyRecord = Assert.Single(history);
            Assert.True(historyRecord.CustomValues.ContainsKey("legacy-id"));
            Assert.Equal(75m, historyRecord.CustomValues["legacy-id"]);
            Assert.False(historyRecord.CustomValues.ContainsKey("sqlite-id"));
        }

        [Fact]
        public void MigrateCustomFieldsIfNeeded_WhenAlreadyCompleted_ShouldSkipSecondRun()
        {
            var firstResult = _localDbService.MigrateCustomFieldsIfNeeded(new[]
            {
                new CustomSalaryField
                {
                    Id = "legacy-1",
                    Name = "Housing",
                    FirmName = "Firm A",
                    Operation = FieldOperation.Subtract,
                    Order = 1
                }
            });

            var secondResult = _localDbService.MigrateCustomFieldsIfNeeded(new[]
            {
                new CustomSalaryField
                {
                    Id = "legacy-2",
                    Name = "Should Not Import",
                    FirmName = "Firm B",
                    Operation = FieldOperation.Add,
                    Order = 2
                }
            });

            Assert.True(firstResult.IsSuccessful);
            Assert.True(_localDbService.IsCustomFieldsMigrationCompleted());
            Assert.False(secondResult.WasMigrationAttempted);

            var fields = _localDbService.GetCustomSalaryFields();
            var stored = Assert.Single(fields);
            Assert.Equal("legacy-1", stored.Id);
            Assert.Equal("Housing", stored.Name);
        }

        [Fact]
        public void MigrateAdvancesIfNeeded_WithEmptySource_ShouldSucceed()
        {
            var result = _localDbService.MigrateAdvancesIfNeeded(Array.Empty<AdvanceMigrationSource>());

            Assert.True(result.WasMigrationAttempted);
            Assert.True(result.IsSuccessful);
            Assert.Equal(0, result.RecordsFound);
            Assert.Equal(0, result.RecordsImported);
            Assert.True(_localDbService.IsAdvancesMigrationCompleted());
        }

        [Fact]
        public void MigrateAdvancesIfNeeded_WithDuplicateId_ShouldBeIdempotent()
        {
            var source = new[]
            {
                new AdvanceMigrationSource
                {
                    EmployeeId = "emp-1",
                    EmployeeFolder = @"C:\Employees\John",
                    Advance = new AdvancePayment
                    {
                        Id = "adv-1",
                        EmployeeName = "John Doe",
                        CompanyId = "Firm A",
                        Date = new DateTime(2026, 4, 15),
                        Amount = 1500m,
                        Month = "2026-04",
                        Note = "first"
                    }
                },
                new AdvanceMigrationSource
                {
                    EmployeeId = "emp-1",
                    EmployeeFolder = @"C:\Employees\John",
                    Advance = new AdvancePayment
                    {
                        Id = "adv-1",
                        EmployeeName = "John Doe",
                        CompanyId = "Firm A",
                        Date = new DateTime(2026, 4, 16),
                        Amount = 1750m,
                        Month = "2026-04",
                        Note = "updated"
                    }
                }
            };

            var result = _localDbService.MigrateAdvancesIfNeeded(source);

            Assert.True(result.IsSuccessful);
            Assert.Equal(2, result.RecordsFound);
            Assert.Equal(2, result.RecordsImported);
            Assert.True(_localDbService.IsAdvancesMigrationCompleted());

            var advances = _localDbService.GetAdvances("Firm A", "2026-04");
            var stored = Assert.Single(advances);
            Assert.Equal("adv-1", stored.Id);
            Assert.Equal(1750m, stored.Amount);
            Assert.Equal("updated", stored.Note);
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class PersistenceServiceTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly AppSettingsService _appSettingsService;
        private readonly PersistenceService _persistenceService;

        public PersistenceServiceTests()
        {
            // Setup temporary test directory
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);

            // Mock AppSettingsService
            _appSettingsService = new AppSettingsService();
            _appSettingsService.Settings.RootFolderPath = _testRootPath;
            _appSettingsService.Settings.LanguageCode = "en"; // Use English folder names in tests

            var folderService = new FolderService(_appSettingsService);
            _persistenceService = new PersistenceService(_appSettingsService, folderService);
        }

        [Fact]
        public void SaveCompanies_ShouldCreateFile()
        {
            var companies = new List<EmployerCompany>
            {
                new EmployerCompany { Name = "Test Company 1" }
            };

            _persistenceService.SaveCompanies(companies);

            var coreDbPath = Path.Combine(_testRootPath, "SQLite", "core.db");
            var jsonSnapshotPath = Path.Combine(_testRootPath, "database.json");
            var syncStatePath = Path.Combine(_testRootPath, "SQLite", "core.sync.json");
            Assert.True(File.Exists(coreDbPath));
            Assert.False(File.Exists(jsonSnapshotPath));
            Assert.False(File.Exists(syncStatePath));
        }

        [Fact]
        public void LoadCompanies_ShouldReturnSavedData()
        {
            var companies = new List<EmployerCompany>
            {
                new EmployerCompany { Name = "Test Company 1", ICO = "12345678" }
            };

            _persistenceService.SaveCompanies(companies);
            var loaded = _persistenceService.LoadCompanies();

            Assert.Single(loaded);
            Assert.Equal("Test Company 1", loaded[0].Name);
            Assert.Equal("12345678", loaded[0].ICO);
        }

        [Fact]
        public void SaveCompanies_ShouldNotWriteLegacyJsonSnapshot()
        {
            var companies = new List<EmployerCompany> { new EmployerCompany { Name = "Authenticated Snapshot" } };

            _persistenceService.SaveCompanies(companies);

            var jsonSnapshotPath = Path.Combine(_testRootPath, "database.json");
            var pendingFolder = Path.Combine(_testRootPath, "SQLite", "PendingChanges");
            var writeLockPath = Path.Combine(_testRootPath, "SQLite", "write.lock");

            Assert.False(File.Exists(jsonSnapshotPath));
            Assert.False(File.Exists(writeLockPath));
            Assert.True(!Directory.Exists(pendingFolder) || Directory.GetFiles(pendingFolder, "*.*").Length == 0);
        }

        [Fact]
        public void SaveCompanies_ShouldDeleteObsoleteCoreSyncState()
        {
            var sqliteFolder = Path.Combine(_testRootPath, "SQLite");
            Directory.CreateDirectory(sqliteFolder);
            var syncStatePath = Path.Combine(sqliteFolder, "core.sync.json");
            File.WriteAllText(syncStatePath, "{}", Encoding.UTF8);

            _persistenceService.SaveCompanies(new List<EmployerCompany>
            {
                new() { Name = "Sync Cleanup" }
            });

            Assert.False(File.Exists(syncStatePath));
        }

        [Fact]
        public void SaveCompanies_ShouldDeleteStaleWriteLockAndNotLeavePendingFiles()
        {
            var sqliteFolder = Path.Combine(_testRootPath, "SQLite");
            Directory.CreateDirectory(sqliteFolder);
            var writeLockPath = Path.Combine(sqliteFolder, "write.lock");
            File.WriteAllText(writeLockPath, "stale", Encoding.UTF8);
            File.SetLastWriteTimeUtc(writeLockPath, DateTime.UtcNow.AddMinutes(-10));

            _persistenceService.SaveCompanies(new List<EmployerCompany>
            {
                new() { Name = "Stale Lock Cleanup" }
            });

            var pendingFolder = Path.Combine(sqliteFolder, "PendingChanges");
            Assert.False(File.Exists(writeLockPath));
            Assert.True(!Directory.Exists(pendingFolder) || Directory.GetFiles(pendingFolder, "*.*").Length == 0);
        }

        [Fact]
        public void LoadCompanies_ShouldApplyPendingCoreChangesFromAnotherComputer()
        {
            var companyA = new EmployerCompany { Name = "Core Company" };
            _persistenceService.SaveCompanies(new List<EmployerCompany> { companyA });
            _persistenceService.LoadCompanies();

            var companyB = new EmployerCompany { Name = "Pending Company" };
            WritePendingCoreChange(new PendingCoreDatabaseChange
            {
                OperationId = Guid.NewGuid().ToString("N"),
                MachineName = "OTHER-PC",
                UserName = "OtherUser",
                UpsertCompanies = new List<EmployerCompany> { companyB },
                Settings = new DatabaseSettings { LanguageCode = "en" }
            });

            var loaded = _persistenceService.LoadCompanies();

            Assert.Contains(loaded, company => company.Id == companyA.Id && company.Name == "Core Company");
            Assert.Contains(loaded, company => company.Id == companyB.Id && company.Name == "Pending Company");
            Assert.Empty(Directory.GetFiles(Path.Combine(_testRootPath, "SQLite", "PendingChanges"), "*.json"));
        }

        [Fact]
        public void LoadCompanies_ShouldKeepPendingCoreChange_WhenCoreDbSaveFails()
        {
            var companyA = new EmployerCompany { Name = "Core Company" };
            _persistenceService.SaveCompanies(new List<EmployerCompany> { companyA });
            _persistenceService.LoadCompanies();

            var companyB = new EmployerCompany { Name = "Pending Company" };
            var pendingPath = WritePendingCoreChange(new PendingCoreDatabaseChange
            {
                OperationId = Guid.NewGuid().ToString("N"),
                MachineName = "OTHER-PC",
                UserName = "OtherUser",
                UpsertCompanies = new List<EmployerCompany> { companyB },
                Settings = new DatabaseSettings { LanguageCode = "en" }
            });

            var coreDbPath = Path.Combine(_testRootPath, "SQLite", "core.db");
            using (var connection = new SqliteConnection($"Data Source={coreDbPath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TRIGGER force_app_database_update_failure
BEFORE UPDATE ON app_database
BEGIN
    SELECT RAISE(ABORT, 'forced save failure');
END;";
                command.ExecuteNonQuery();
            }

            _ = Record.Exception(() => _persistenceService.LoadCompanies());

            Assert.True(File.Exists(pendingPath));
        }

        [Fact]
        public void LoadCompanies_ShouldRejectTamperedAuthenticatedJson_WhenCoreDbMissing()
        {
            var jsonSnapshotPath = Path.Combine(_testRootPath, "database.json");
            var checksumPath = Path.Combine(_testRootPath, "database.json.sha256");
            WriteLegacyDatabaseJson(jsonSnapshotPath, checksumPath, new DatabaseRoot
            {
                Companies = new List<EmployerCompany> { new EmployerCompany { Name = "Tamper Test" } }
            });

            var payload = File.ReadAllBytes(jsonSnapshotPath);
            payload[25] = (byte)(payload[25] ^ 0xFF);
            File.WriteAllBytes(jsonSnapshotPath, payload);
            File.WriteAllText(checksumPath, Convert.ToBase64String(SHA256.HashData(payload)), Encoding.UTF8);

            var loaded = _persistenceService.LoadCompanies();

            Assert.Empty(loaded);
        }

        [Fact]
        public void SaveCompanies_ShouldCreateBackup_WhenFileExists()
        {
            var companies1 = new List<EmployerCompany> { new EmployerCompany { Name = "V1" } };
            _persistenceService.SaveCompanies(companies1);

            // Wait a bit or ensure backup logic works
            var companies2 = new List<EmployerCompany> { new EmployerCompany { Name = "V2" } };
            _persistenceService.SaveCompanies(companies2);

            var backupDir = Path.Combine(_testRootPath, "backups");
            Assert.True(Directory.Exists(backupDir));
            Assert.NotEmpty(Directory.GetFiles(backupDir, "*.bak"));
        }

        [Fact]
        public void LoadCompanies_ShouldPreferCoreDb_WhenLegacyJsonIsCorrupted()
        {
            var companies = new List<EmployerCompany> { new EmployerCompany { Name = "Integrity Test" } };
            _persistenceService.SaveCompanies(companies);

            var filePath = Path.Combine(_testRootPath, "database.json");
            File.WriteAllBytes(filePath, new byte[] { 1, 2, 3, 4, 5 });

            var loaded = _persistenceService.LoadCompanies();
            Assert.Single(loaded);
            Assert.Equal("Integrity Test", loaded[0].Name);
            Assert.False(File.Exists(filePath));
            Assert.True(File.Exists(filePath + ".migrated"));
        }

        [Fact]
        public void LoadCompanies_ShouldMigrateLegacyDatabaseJsonToCoreDb()
        {
            var legacyJsonPath = Path.Combine(_testRootPath, "database.json");
            var legacyChecksumPath = Path.Combine(_testRootPath, "database.json.sha256");
            var coreDbPath = Path.Combine(_testRootPath, "SQLite", "core.db");
            WriteLegacyDatabaseJson(legacyJsonPath, legacyChecksumPath, new DatabaseRoot
            {
                Version = "2.0",
                Companies = new List<EmployerCompany> { new EmployerCompany { Name = "Migration Test" } },
                Settings = new DatabaseSettings { LanguageCode = "en" }
            });

            var loaded = _persistenceService.LoadCompanies();

            Assert.Single(loaded);
            Assert.Equal("Migration Test", loaded[0].Name);
            Assert.True(File.Exists(coreDbPath));
            Assert.False(File.Exists(legacyJsonPath));
            Assert.False(File.Exists(legacyChecksumPath));
            Assert.True(File.Exists(legacyJsonPath + ".migrated"));
            Assert.True(File.Exists(legacyChecksumPath + ".migrated"));
        }

        [Fact]
        public void LoadCompanies_ShouldIgnoreLegacyJson_WhenCoreDbExists()
        {
            var initialCompanies = new List<EmployerCompany> { new EmployerCompany { Name = "Core Value" } };
            _persistenceService.SaveCompanies(initialCompanies);

            var legacyJsonPath = Path.Combine(_testRootPath, "database.json");
            var legacyChecksumPath = Path.Combine(_testRootPath, "database.json.sha256");
            var incoming = new DatabaseRoot
            {
                Version = "2.0",
                Companies = new List<EmployerCompany> { new EmployerCompany { Name = "Json Override" } },
                Settings = new DatabaseSettings
                {
                    LanguageCode = "en",
                    SelectedCompanyId = string.Empty,
                    AppVersion = "0.1.59"
                }
            };

            WriteLegacyDatabaseJson(legacyJsonPath, legacyChecksumPath, incoming);

            var loaded = _persistenceService.LoadCompanies();
            Assert.Single(loaded);
            Assert.Equal("Core Value", loaded[0].Name);

            var loadedAgain = _persistenceService.LoadCompanies();
            Assert.Single(loadedAgain);
            Assert.Equal("Core Value", loadedAgain[0].Name);
            Assert.False(File.Exists(legacyJsonPath));
            Assert.False(File.Exists(legacyChecksumPath));
            Assert.True(File.Exists(legacyJsonPath + ".migrated"));
            Assert.True(File.Exists(legacyChecksumPath + ".migrated"));
        }

        private static void WriteLegacyDatabaseJson(string jsonPath, string checksumPath, DatabaseRoot database)
        {
            var json = JsonSerializer.Serialize(database, new JsonSerializerOptions { WriteIndented = true });
            var encrypted = EncryptLegacyJson(json);
            File.WriteAllBytes(jsonPath, encrypted);
            File.WriteAllText(checksumPath, Convert.ToBase64String(SHA256.HashData(encrypted)), Encoding.UTF8);
        }

        private string WritePendingCoreChange(PendingCoreDatabaseChange change)
        {
            var pendingFolder = Path.Combine(_testRootPath, "SQLite", "PendingChanges");
            Directory.CreateDirectory(pendingFolder);

            var operationId = string.IsNullOrWhiteSpace(change.OperationId)
                ? Guid.NewGuid().ToString("N")
                : change.OperationId;
            var tmpPath = Path.Combine(pendingFolder, $"test_{operationId}.tmp");
            var finalPath = Path.Combine(pendingFolder, $"test_{operationId}.json");
            var json = JsonSerializer.Serialize(change, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, finalPath);
            return finalPath;
        }

        private static byte[] EncryptLegacyJson(string plainText)
        {
            var key = new byte[32];
            var iv = new byte[16];
            var keyBytes = Encoding.UTF8.GetBytes("AgencyContractorSecretKey2024_Secure");
            Array.Copy(keyBytes, key, Math.Min(keyBytes.Length, key.Length));
            var ivBytes = Encoding.UTF8.GetBytes("AgencyContractor");
            Array.Copy(ivBytes, iv, Math.Min(ivBytes.Length, iv.Length));

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }

            return ms.ToArray();
        }

        private void DeleteCoreDbFiles()
        {
            var sqliteFolder = Path.Combine(_testRootPath, "SQLite");
            foreach (var path in new[]
            {
                Path.Combine(sqliteFolder, "core.db"),
                Path.Combine(sqliteFolder, "core.db-wal"),
                Path.Combine(sqliteFolder, "core.db-shm")
            })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                    Directory.Delete(_testRootPath, true);
            }
            catch { }
        }
    }
}

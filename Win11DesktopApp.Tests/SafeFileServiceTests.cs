using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class SafeFileServiceTests : IDisposable
    {
        private readonly string _testRootPath;

        public SafeFileServiceTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorSafeFileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);
        }

        [Fact]
        public void WriteJsonAtomic_NormalOverwrite_Works()
        {
            var path = Path.Combine(_testRootPath, "data.json");
            File.WriteAllText(path, "{\"Name\":\"Old\",\"Values\":[0]}");

            var data = new SampleData
            {
                Name = "Test",
                Values = new List<int> { 1, 2, 3 }
            };

            SafeFileService.WriteJsonAtomic(path, data);
            var restored = SafeFileService.ReadJson<SampleData>(path);

            Assert.NotNull(restored);
            Assert.Equal("Test", restored!.Name);
            Assert.Equal(new[] { 1, 2, 3 }, restored.Values);
            Assert.False(File.Exists(GetBackupPath(path)));
        }

        [Fact]
        public void WriteJsonAtomic_LockedTarget_CreatesRecovery()
        {
            var path = Path.Combine(_testRootPath, "locked.json");
            File.WriteAllText(path, "{\"Name\":\"Old\",\"Values\":[0]}");

            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var data = new SampleData
            {
                Name = "Recovered",
                Values = new List<int> { 4, 5, 6 }
            };

            var ex = Assert.Throws<SafeFileRecoveryException>(() => SafeFileService.WriteJsonAtomic(path, data));

            Assert.Equal(GetRecoveryPath(path), ex.RecoveryPath);
            Assert.True(File.Exists(ex.RecoveryPath));

            var recovered = SafeFileService.ReadJson<SampleData>(ex.RecoveryPath);
            Assert.NotNull(recovered);
            Assert.Equal("Recovered", recovered!.Name);
            Assert.Equal(new[] { 4, 5, 6 }, recovered.Values);
        }

        [Fact]
        public void WriteJsonAtomic_RecoveryCreated_TempCleaned()
        {
            var path = Path.Combine(_testRootPath, "tempclean.json");
            File.WriteAllText(path, "{\"Name\":\"Old\"}");

            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.Throws<SafeFileRecoveryException>(() =>
                SafeFileService.WriteJsonAtomic(path, new SampleData { Name = "Recovered", Values = new List<int> { 7 } }));

            Assert.Empty(GetTempFiles(path));
        }

        [Fact]
        public void WriteJsonAtomic_RecoveryFailed_TempRemains()
        {
            var path = Path.Combine(_testRootPath, "tempremains.json");
            File.WriteAllText(path, "{\"Name\":\"Old\"}");

            var recoveryPath = GetRecoveryPath(path);
            File.WriteAllText(recoveryPath, "{\"Name\":\"ExistingRecovery\"}");

            using var targetLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            using var recoveryLock = new FileStream(recoveryPath, FileMode.Open, FileAccess.Read, FileShare.None);

            Assert.ThrowsAny<IOException>(() =>
                SafeFileService.WriteJsonAtomic(path, new SampleData { Name = "NewData", Values = new List<int> { 8 } }));

            Assert.NotEmpty(GetTempFiles(path));
        }

        [Fact]
        public void WriteJsonAtomic_ReadonlyTarget_StillWorks()
        {
            var path = Path.Combine(_testRootPath, "readonly.json");
            File.WriteAllText(path, "{\"Name\":\"Old\"}");
            File.SetAttributes(path, FileAttributes.ReadOnly);

            var data = new SampleData
            {
                Name = "Updated",
                Values = new List<int> { 9 }
            };

            SafeFileService.WriteJsonAtomic(path, data);
            var restored = SafeFileService.ReadJson<SampleData>(path);

            Assert.NotNull(restored);
            Assert.Equal("Updated", restored!.Name);
            Assert.Equal(new[] { 9 }, restored.Values);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRootPath))
                {
                    foreach (var file in Directory.GetFiles(_testRootPath, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);

                    Directory.Delete(_testRootPath, true);
                }
            }
            catch
            {
            }
        }

        private sealed class SampleData
        {
            public string Name { get; set; } = string.Empty;
            public List<int> Values { get; set; } = new();
        }

        private static string GetBackupPath(string path) => $"{path}.bak";

        private static string GetRecoveryPath(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            return Path.Combine(directory, $"{fileName}.recovery{extension}");
        }

        private static string[] GetTempFiles(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var fileName = Path.GetFileName(path);
            return Directory.GetFiles(directory, $"{fileName}.*.tmp", SearchOption.TopDirectoryOnly);
        }
    }
}

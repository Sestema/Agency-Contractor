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
        public void WriteJsonAtomic_ShouldRoundTripJson()
        {
            var path = Path.Combine(_testRootPath, "data.json");
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
        }

        [Fact]
        public void WriteJsonAtomic_ShouldOverwriteReadOnlyFile()
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
    }
}

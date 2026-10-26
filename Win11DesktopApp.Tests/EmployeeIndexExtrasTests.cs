using System;
using System.Collections.Generic;
using System.IO;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeIndexExtrasTests
    {
        [Fact]
        public void IsFresh_RequiresVersionAndSourceTime()
        {
            var row = new EmployeeIndexRow { ExtrasVersion = 0, SourceJsonUtc = DateTime.UtcNow.ToString("O") };
            Assert.False(EmployeeIndexExtras.IsFresh(row, DateTime.UtcNow));

            row.ExtrasVersion = EmployeeIndexExtras.CurrentVersion;
            row.SourceJsonUtc = DateTime.UtcNow.AddMinutes(-1).ToString("O");
            Assert.True(EmployeeIndexExtras.IsFresh(row, DateTime.UtcNow.AddMinutes(-2)));
            Assert.False(EmployeeIndexExtras.IsFresh(row, DateTime.UtcNow));
            Assert.True(EmployeeIndexExtras.IsFresh(row, null));
        }

        [Fact]
        public void ToEmployeeData_HidesRodneCisloWithoutFlagAndKeepsFirmHistory()
        {
            var folder = Path.Combine(Path.GetTempPath(), "agency-index-extras-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, "employee.json"), "{}");
                var data = new EmployeeData
                {
                    UniqueId = "id-1",
                    FirstName = "Ivan",
                    LastName = "Novak",
                    HasRodneCisloData = false,
                    RodneCislo = "secret",
                    BirthDate = "01.02.1990",
                    Gender = "female",
                    HighestEducationCode = "high_school",
                    AddressLocal = new EmployeeAddress { Street = "Hlavní", Number = "1", City = "Plzeň", Zip = "30100" },
                    IgnoredDocuments = new Dictionary<string, string> { ["passport"] = "2026-12-01" },
                    FirmHistory = new List<FirmHistoryEntry>
                    {
                        new() { FirmName = "Old", StartDate = "01.01.2020", EndDate = "01.01.2021" },
                        new() { FirmName = "New", StartDate = "02.01.2021", EndDate = "" }
                    }
                };

                var row = new EmployeeIndexRow();
                EmployeeIndexExtras.Apply(row, data, folder);
                var restored = EmployeeIndexExtras.ToEmployeeData(row);

                Assert.Equal(EmployeeIndexExtras.CurrentVersion, row.ExtrasVersion);
                Assert.False(string.IsNullOrWhiteSpace(row.SourceJsonUtc));
                Assert.False(restored.HasRodneCisloData);
                Assert.Equal(string.Empty, restored.RodneCislo);
                Assert.Equal("01.02.1990", restored.BirthDate);
                Assert.Equal("female", restored.Gender);
                Assert.Equal("high_school", restored.HighestEducationCode);
                Assert.Equal("Plzeň", restored.AddressLocal.City);
                Assert.Equal("2026-12-01", restored.IgnoredDocuments["passport"]);
                Assert.Equal(2, restored.FirmHistory.Count);
                Assert.Equal("Old", restored.FirmHistory[0].FirmName);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}

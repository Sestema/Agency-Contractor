using System.Collections.Generic;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class SalaryColumnWidthLayoutTests
    {
        [Fact]
        public void Merge_ShouldKeepExistingWidth_WhenNewValueIsZero()
        {
            var existing = new Dictionary<string, double> { ["DisplayName"] = 320 };

            var merged = SalaryColumnWidthLayout.Merge(existing, new[]
            {
                ("DisplayName", 0d),
                ("HoursWorked", 80d)
            });

            Assert.Equal(320, merged["DisplayName"]);
            Assert.Equal(80, merged["HoursWorked"]);
        }

        [Fact]
        public void FromLegacyList_ShouldIgnoreZeroAndMapFixedColumns()
        {
            var legacy = new[] { 280d, 0d, 72d, 90d, 85d, 95d, 96d, 200d };

            var mapped = SalaryColumnWidthLayout.FromLegacyList(legacy);

            Assert.Equal(280, mapped["DisplayName"]);
            Assert.False(mapped.ContainsKey("HoursWorked"));
            Assert.Equal(72, mapped["HourlyRate"]);
            Assert.Equal(200, mapped["Note"]);
        }

        [Fact]
        public void ResolveStore_ShouldPreferKeyedWidthsOverLegacyList()
        {
            var keyed = new Dictionary<string, double> { ["DisplayName"] = 340 };
            var legacy = new[] { 120d, 70d, 60d, 90d, 85d, 95d, 96d, 180d };

            var resolved = SalaryColumnWidthLayout.ResolveStore(keyed, legacy);

            Assert.Equal(340, resolved["DisplayName"]);
            Assert.False(resolved.ContainsKey("HoursWorked"));
        }
    }
}

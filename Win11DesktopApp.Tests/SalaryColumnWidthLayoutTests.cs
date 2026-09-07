using System;
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

        [Fact]
        public void MergeUserChanges_ShouldNotOverwriteStoredWidth_WhenColumnWasNotResized()
        {
            var existing = new Dictionary<string, double>
            {
                ["DisplayName"] = 320,
                ["HoursWorked"] = 88
            };

            var merged = SalaryColumnWidthLayout.MergeUserChanges(
                existing,
                new[]
                {
                    ("DisplayName", 220d),
                    ("HoursWorked", 70d),
                    ("GrossSalary", 90d)
                },
                changedKeys: new[] { "HoursWorked" });

            Assert.Equal(320, merged["DisplayName"]);
            Assert.Equal(70, merged["HoursWorked"]);
            Assert.False(merged.ContainsKey("GrossSalary"));
        }

        [Fact]
        public void MergeUserChanges_ShouldKeepStore_WhenNothingChanged()
        {
            var existing = new Dictionary<string, double> { ["DisplayName"] = 320 };

            var merged = SalaryColumnWidthLayout.MergeUserChanges(
                existing,
                new[] { ("DisplayName", 220d) },
                changedKeys: Array.Empty<string>());

            Assert.Equal(320, merged["DisplayName"]);
            Assert.Single(merged);
        }

        [Fact]
        public void TryMeasurePersistedWidth_ShouldUseActualWidth_WhenColumnIsStar()
        {
            Assert.True(SalaryColumnWidthLayout.TryMeasurePersistedWidth(
                SalaryColumnWidthLayout.WidthUnit.Star,
                specifiedWidth: 1,
                actualWidth: 40,
                displayWidth: 260,
                out var width));
            Assert.Equal(260, width);
        }

        [Fact]
        public void TryMeasurePersistedWidth_ShouldFallBackToActualWidth_WhenDisplayIsStarWeight()
        {
            Assert.True(SalaryColumnWidthLayout.TryMeasurePersistedWidth(
                SalaryColumnWidthLayout.WidthUnit.Star,
                specifiedWidth: 1,
                actualWidth: 240,
                displayWidth: 1,
                out var width));
            Assert.Equal(240, width);
        }

        [Fact]
        public void TryMeasurePersistedWidth_ShouldIgnoreAutoAndStarWeight()
        {
            Assert.False(SalaryColumnWidthLayout.TryMeasurePersistedWidth(
                SalaryColumnWidthLayout.WidthUnit.Other,
                specifiedWidth: 180,
                actualWidth: 180,
                displayWidth: 180,
                out _));

            Assert.False(SalaryColumnWidthLayout.TryMeasurePersistedWidth(
                SalaryColumnWidthLayout.WidthUnit.Star,
                specifiedWidth: 1,
                actualWidth: 1,
                displayWidth: 1,
                out _));
        }

        [Fact]
        public void MergeUserChanges_ShouldPersistNoteWidth()
        {
            var merged = SalaryColumnWidthLayout.MergeUserChanges(
                existing: null,
                measured: new[] { ("Note", 260d) },
                changedKeys: new[] { "Note" });

            Assert.Equal(260, merged["Note"]);
        }
    }
}

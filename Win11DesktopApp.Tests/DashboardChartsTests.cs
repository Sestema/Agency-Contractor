using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class DashboardChartsTests
    {
        [Fact]
        public void PayoutBars_LastTwelveMonthsOldestFirst_NotStartedAsZero()
        {
            var months = Enumerable.Range(1, 11)
                .Select(m => new SalaryMonthSummary { MonthKey = $"2026-{m:D2}", TotalGross = 100m * m, TotalNet = 80m * m, TotalExpenses = 5m })
                .ToList();
            months.Add(new SalaryMonthSummary { MonthKey = "2025-12", TotalGross = 50m, TotalNet = 40m });
            months.Add(new SalaryMonthSummary { MonthKey = "2025-11", TotalGross = 50m, TotalNet = 40m });
            months.Add(new SalaryMonthSummary { MonthKey = "2026-12", TotalGross = 0m, TotalNet = -1_687m }); // not started

            var bars = DashboardViewModel.BuildPayoutBars(months);

            Assert.Equal(12, bars.Count);
            Assert.Equal(80, bars[0].Primary);      // 2026-01 is the oldest kept month
            Assert.Equal(0, bars[^1].Primary);      // 2026-12 not started -> 0, not negative
            Assert.Equal(5, bars[0].Secondary);
        }

        [Fact]
        public void FirmPeople_TopThreeAndOthers()
        {
            var segments = DashboardViewModel.BuildFirmPeopleSegments(new List<(string, int)>
            {
                ("RUMPOLD-P", 38), ("DCK", 18), ("Marius", 15), ("AGRO", 6), ("ZDV", 6), ("Empty", 0)
            }, "інші");

            Assert.Equal(new[] { "RUMPOLD-P", "DCK", "Marius", "інші" }, segments.Select(s => s.Label));
            Assert.Equal(12, segments[^1].Value);
        }

        [Theory]
        [InlineData(44_186_821, true, "44,2 млн")]
        [InlineData(3_779_444, false, "3,78")]
        [InlineData(362_712, true, "362 712")]
        public void FormatCompact_MillionsShortOtherwiseFull(int amount, bool withUnit, string expected)
        {
            Assert.Equal(expected, DashboardViewModel.FormatCompact(amount, "млн", withUnit));
        }

        [Fact]
        public void FirmHours_BiggestFirstWithShareOfTheBiggest()
        {
            var items = DashboardViewModel.BuildFirmHours(new Dictionary<string, decimal>
            {
                ["DCK"] = 5_076m, ["RUMPOLD-P"] = 5_782.5m, ["Marius"] = 3_487m, ["Nothing"] = 0m
            });

            Assert.Equal(new[] { "RUMPOLD-P", "DCK", "Marius" }, items.Select(i => i.FirmName));
            Assert.Equal(1.0, items[0].Ratio);
            Assert.Equal("5 782", items[0].HoursText);
            Assert.InRange(items[2].Ratio, 0.6, 0.61);
        }
        [Theory]
        [InlineData("expiring", "companies", "salary", null, "expiring,companies,salary,charts")]
        [InlineData("salary", "expiring", "companies", "charts", "salary,expiring,companies,charts")]
        [InlineData("charts", "charts", "bogus", "", "charts,expiring,companies,salary")]
        public void NormalizeSlots_EachWidgetOnce(string? a, string? b, string? c, string? d, string expected)
        {
            Assert.Equal(expected, string.Join(",", DashboardViewModel.NormalizeSlots(a, b, c, d)));
        }
    }
}

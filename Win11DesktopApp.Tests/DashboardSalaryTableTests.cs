using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class DashboardSalaryTableTests
    {
        [Fact]
        public void PaidMonth_ShowsEveryStepFromGrossToTotal()
        {
            // October from the real screenshot: gross 4 651 123, to pay 3 607 144, expenses 172 300.
            var month = new SalaryMonthSummary
            {
                TotalGross = 4_651_123m,
                TotalAdvances = 112_400m,
                TotalNet = 3_607_144m,
                TotalExpenses = 172_300m,
                TotalEntries = 146,
                PaidEntries = 146
            };

            Assert.Equal(-931_579m, month.TotalAdjustments);
            Assert.Equal("4 651 123", month.GrossCell);
            Assert.Equal("−112 400", month.AdvancesCell);
            Assert.Equal("−931 579", month.AdjustmentsCell);
            Assert.Equal("3 607 144", month.NetCell);
            Assert.Equal("+172 300", month.ExpensesCell);
            Assert.Equal("3 779 444", month.TotalCell);
            Assert.Equal("✓ 146", month.StatusCell);
            Assert.True(month.IsAdjustmentNegative);
        }

        [Fact]
        public void MonthWithoutHours_IsNotStarted_InsteadOfNegativeSums()
        {
            // November from the screenshot: no hours yet, only an advance, so net was "-1 687".
            var month = new SalaryMonthSummary
            {
                TotalGross = 0m,
                TotalAdvances = 1_687m,
                TotalNet = -1_687m,
                TotalEntries = 133,
                PaidEntries = 0,
                NotStartedText = "не почато"
            };

            Assert.True(month.IsNotStarted);
            Assert.Equal("не почато", month.NetCell);
            Assert.Equal("—", month.GrossCell);
            Assert.Equal("—", month.AdvancesCell);
            Assert.Equal("—", month.TotalCell);
            Assert.Equal("0/133", month.StatusCell);
        }

        [Fact]
        public void ExtrasAboveDeductions_ShowAsPlus()
        {
            var month = new SalaryMonthSummary { TotalGross = 10_000m, TotalAdvances = 0m, TotalNet = 10_500m, TotalEntries = 1 };
            Assert.Equal("+500", month.AdjustmentsCell);
            Assert.False(month.IsAdjustmentNegative);
        }
    }
}

using Win11DesktopApp.ViewModels;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class SalaryExpenseInputTests
    {
        [Theory]
        [InlineData(172300, "172 300")]
        [InlineData(105000, "105 000")]
        [InlineData(950, "950")]
        [InlineData(0, "0")]
        public void FormatMoney_UsesSpacesBetweenThousands(int amount, string expected)
        {
            Assert.Equal(expected, SalaryViewModel.FormatMoney(amount));
        }

        [Fact]
        public void FormatMoney_KeepsCents()
        {
            Assert.Equal("1 250.5", SalaryViewModel.FormatMoney(1250.50m));
        }

        [Theory]
        [InlineData("105 000", 105000)]
        [InlineData("105 000", 105000)]
        [InlineData("105000", 105000)]
        [InlineData("1 250,50", 1250.50)]
        [InlineData("1250.5", 1250.5)]
        [InlineData("32 800 Kč", 32800)]
        [InlineData("", 0)]
        public void TryParseMoney_AcceptsUsualWriting(string text, double expected)
        {
            Assert.True(SalaryViewModel.TryParseMoney(text, out var amount));
            Assert.Equal((decimal)expected, amount);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-500")]
        [InlineData("10 000 тис")]
        public void TryParseMoney_RejectsNonsense(string text)
        {
            Assert.False(SalaryViewModel.TryParseMoney(text, out _));
        }
    }
}

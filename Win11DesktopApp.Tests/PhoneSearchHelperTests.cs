using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class PhoneSearchHelperTests
    {
        [Theory]
        [InlineData("+420 777 123 456", "777123456")]
        [InlineData("+420 777 123 456", "777 123 456")]
        [InlineData("+420 777 123 456", "+420777123456")]
        public void MatchesPhone_FindsFormattedNumber(string stored, string query)
        {
            Assert.True(PhoneSearchHelper.MatchesPhone(stored, query));
        }

        [Fact]
        public void MatchesPhone_KeepsPlainTextMatch()
        {
            Assert.True(PhoneSearchHelper.MatchesPhone("+420 777 123 456", "+420 777"));
        }

        [Theory]
        [InlineData("12")]
        [InlineData("420")]
        [InlineData("77712")]
        public void MatchesPhone_IgnoresShortDigitQueries(string query)
        {
            Assert.False(PhoneSearchHelper.MatchesPhone("+420 777 123 456", query));
        }

        [Fact]
        public void MatchesPhone_DoesNotMatchUnrelatedNumber()
        {
            Assert.False(PhoneSearchHelper.MatchesPhone("+420 777 123 456", "111222333"));
        }
    }
}

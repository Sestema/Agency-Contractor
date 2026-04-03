using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class CzechBankAccountResolverTests
    {
        [Theory]
        [InlineData("123456789/2010", "2010")]
        [InlineData("19-123456789/0800", "0800")]
        [InlineData(" 19-123456789 / 0800 ", "0800")]
        public void ExtractBankCode_ShouldReturnCzechBankCode(string accountNumber, string expectedCode)
        {
            var code = CzechBankAccountResolver.ExtractBankCode(accountNumber);

            Assert.Equal(expectedCode, code);
        }

        [Theory]
        [InlineData("123456789/2010", "Fio banka, a.s.")]
        [InlineData("19-123456789/0800", "Česká spořitelna, a.s.")]
        public void TryResolveBankName_ShouldReturnKnownBankName(string accountNumber, string expectedBankName)
        {
            var resolved = CzechBankAccountResolver.TryResolveBankName(accountNumber, out var bankName);

            Assert.True(resolved);
            Assert.Equal(expectedBankName, bankName);
        }

        [Fact]
        public void TryResolveBankName_ShouldReturnFalseForUnknownCode()
        {
            var resolved = CzechBankAccountResolver.TryResolveBankName("123456789/9999", out var bankName);

            Assert.False(resolved);
            Assert.Equal(string.Empty, bankName);
        }
    }
}

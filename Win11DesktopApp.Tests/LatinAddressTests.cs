using Win11DesktopApp.Helpers;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class LatinAddressTests
    {
        [Fact]
        public void ToLatin_WritesUkrainianInLatin()
        {
            Assert.Equal("Khreshchatyk", LatinAddress.ToLatin("Хрещатик"));
            Assert.Equal("Kyiv", LatinAddress.ToLatin("Київ"));
            Assert.Equal("vulytsia", LatinAddress.ToLatin("вулиця"));
        }

        [Fact]
        public void ToLatin_KeepsCzechAndPolish()
        {
            Assert.Equal("Plzeň", LatinAddress.ToLatin("Plzeň"));
            Assert.Equal("Bielsko-Biała", LatinAddress.ToLatin("Bielsko-Biała"));
        }
    }
}

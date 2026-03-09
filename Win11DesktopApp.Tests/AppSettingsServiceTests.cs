using System.IO;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class AppSettingsServiceTests
    {
        [Fact]
        public void AppVersion_ShouldHaveDefaultValue()
        {
            var settings = new AppSettingsService.AppSettings();

            Assert.Equal(AppSettingsService.CurrentAppVersion, settings.AppVersion);
        }
    }
}

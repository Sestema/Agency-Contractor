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
            // Arrange
            // We need to ensure we are not reading an existing settings file that might have different data.
            // AppSettingsService constructor loads from a fixed path in AppData.
            // This makes it hard to test in isolation without modifying the service to accept a path or mocking File operations.
            // However, the service logic is: if file doesn't exist or key missing, use default.
            
            // Let's test the AppSettings class directly first, as it holds the default.
            var settings = new AppSettingsService.AppSettings();

            // Assert
            Assert.Equal("0.0.02", settings.AppVersion);
        }
    }
}

using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class PasswordHashServiceTests
    {
        [Fact]
        public void HashPassword_ShouldVerifyWithOriginalPasswordOnly()
        {
            var salt = PasswordHashService.CreateSalt();
            var hash = PasswordHashService.HashPassword("Secret123", salt);

            Assert.True(PasswordHashService.VerifyPassword("Secret123", salt, hash));
            Assert.False(PasswordHashService.VerifyPassword("Wrong123", salt, hash));
        }
    }
}

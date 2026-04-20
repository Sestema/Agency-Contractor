using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests
{
    public class LicenseServiceTests
    {
        [Fact]
        public void CanTrustLocalLicense_ShouldAllowLegacyLicenseBeforeServerMigration()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 1,
                SignatureSecret = string.Empty
            };

            Assert.True(LicenseService.CanTrustLocalLicense(info, string.Empty));
        }

        [Fact]
        public void CanTrustLocalLicense_ShouldRejectLegacyLicenseAfterServerMigration()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 1,
                SignatureSecret = string.Empty
            };

            Assert.False(LicenseService.CanTrustLocalLicense(info, "2026-04-15T16:00:00Z"));
        }

        [Fact]
        public void CanTrustLocalLicense_ShouldKeepModernLicenseTrustedAfterServerMigration()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 2,
                SignatureSecret = "secret"
            };

            Assert.True(LicenseService.CanTrustLocalLicense(info, "2026-04-15T16:00:00Z"));
        }

        [Fact]
        public void ShouldLogLegacyLicenseWarning_ShouldAllowWarningBeforeServerMigration()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 1,
                SignatureSecret = string.Empty
            };

            Assert.True(LicenseService.ShouldLogLegacyLicenseWarning(info, string.Empty));
        }

        [Fact]
        public void ShouldLogLegacyLicenseWarning_ShouldSuppressWarningAfterServerMigration()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 1,
                SignatureSecret = string.Empty
            };

            Assert.False(LicenseService.ShouldLogLegacyLicenseWarning(info, "2026-04-15T16:00:00Z"));
        }

        [Fact]
        public void ShouldLogLegacyLicenseWarning_ShouldNotLogForModernLicense()
        {
            var info = new LicenseInfo
            {
                SignatureVersion = 2,
                SignatureSecret = "secret"
            };

            Assert.False(LicenseService.ShouldLogLegacyLicenseWarning(info, string.Empty));
        }
    }
}

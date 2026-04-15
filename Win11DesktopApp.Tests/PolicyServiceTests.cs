using System.Threading.Tasks;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class PolicyServiceTests
    {
        [Theory]
        [InlineData("1.0.0", "1.0.0", 0)]
        [InlineData("1.0.0", "2.0.0", -1)]
        [InlineData("v2.0.0", "1.0.0", 1)]
        [InlineData("", "1.0.0", -1)]
        [InlineData("1.0.0", "", 1)]
        [InlineData("1.0.0-beta", "1.0.0", 0)]
        [InlineData("abc", "1.0.0", -1)]
        public void CompareVersions_ShouldReturnExpectedOrdering(string? currentVersion, string? targetVersion, int expectedSign)
        {
            var result = PolicyService.CompareVersions(currentVersion, targetVersion);

            Assert.Equal(expectedSign, Math.Sign(result));
        }

        [Fact]
        public void CompareVersions_WhenBothVersionsAreInvalid_ShouldTreatThemAsEqual()
        {
            var result = PolicyService.CompareVersions(null, null);

            Assert.Equal(-1, result);
        }

        [Fact]
        public async Task IsCurrentVersionBelowMinimum_ShouldUseCurrentPolicyMinimumVersion()
        {
            await PolicyService.ApplyPolicyAsync(new RemotePolicy
            {
                MinimumSupportedVersion = "2.0.0"
            }, saveSettings: false);

            Assert.True(PolicyService.IsCurrentVersionBelowMinimum("1.9.9"));
            Assert.False(PolicyService.IsCurrentVersionBelowMinimum("2.0.0"));
        }
    }
}

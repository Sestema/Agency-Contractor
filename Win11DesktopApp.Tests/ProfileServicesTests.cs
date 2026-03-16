using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class ProfileServicesTests : IDisposable
    {
        private readonly string _testRootPath;
        private readonly List<AppSettingsService> _settingsServices = new();

        public ProfileServicesTests()
        {
            _testRootPath = Path.Combine(Path.GetTempPath(), "AgencyContractorProfileTests_" + Guid.NewGuid());
            Directory.CreateDirectory(_testRootPath);
        }

        [Theory]
        [InlineData("Ivan", true)]
        [InlineData("Petrenko", true)]
        [InlineData("ivan", false)]
        [InlineData("IVAN", false)]
        [InlineData("Іван", false)]
        [InlineData("Ivan1", false)]
        [InlineData("Van-Dam", false)]
        public void IsValidProfileName_ShouldValidateLatinAbcFormat(string value, bool expected)
        {
            Assert.Equal(expected, ProfileAuthService.IsValidProfileName(value));
        }

        [Fact]
        public void SaveRememberedSession_ShouldUpdateSettingsAndRestoreSession()
        {
            var settingsService = CreateIsolatedSettingsService();
            var sessionService = new ProfileSessionService(settingsService);
            var profile = new ClientProfileRecord
            {
                ClientId = "client-1",
                SessionVersion = 3,
                RememberMeEnabled = true
            };

            sessionService.SaveRememberedSession(profile);

            Assert.True(settingsService.Settings.RememberProfileLogin);
            Assert.Equal(profile.ClientId, settingsService.Settings.ProfileClientId);
            Assert.Equal(profile.SessionVersion, settingsService.Settings.ProfileSessionVersion);
            Assert.False(string.IsNullOrWhiteSpace(settingsService.Settings.EncryptedProfileSessionToken));
            Assert.True(sessionService.TryRestoreRememberedSession(profile));
        }

        [Fact]
        public void ClearRememberedSession_ShouldRemoveStoredSession()
        {
            var settingsService = CreateIsolatedSettingsService();
            var sessionService = new ProfileSessionService(settingsService);
            var profile = new ClientProfileRecord
            {
                ClientId = "client-2",
                SessionVersion = 5,
                RememberMeEnabled = true
            };

            sessionService.SaveRememberedSession(profile);
            sessionService.ClearRememberedSession();

            Assert.False(settingsService.Settings.RememberProfileLogin);
            Assert.Equal(string.Empty, settingsService.Settings.ProfileClientId);
            Assert.Equal(0, settingsService.Settings.ProfileSessionVersion);
            Assert.Equal(string.Empty, settingsService.Settings.EncryptedProfileSessionToken);
            Assert.False(sessionService.TryRestoreRememberedSession(profile));
        }

        [Fact]
        public void TryRestoreRememberedSession_ShouldFailWhenProfileDisablesRememberMe()
        {
            var settingsService = CreateIsolatedSettingsService();
            var sessionService = new ProfileSessionService(settingsService);
            var rememberedProfile = new ClientProfileRecord
            {
                ClientId = "client-3",
                SessionVersion = 7,
                RememberMeEnabled = true
            };

            sessionService.SaveRememberedSession(rememberedProfile);

            var disabledProfile = new ClientProfileRecord
            {
                ClientId = "client-3",
                SessionVersion = 7,
                RememberMeEnabled = false
            };

            Assert.False(sessionService.TryRestoreRememberedSession(disabledProfile));
        }

        public void Dispose()
        {
            Thread.Sleep(700);

            foreach (var settingsService in _settingsServices)
            {
                var timerField = typeof(AppSettingsService).GetField("_debounceTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                (timerField?.GetValue(settingsService) as Timer)?.Dispose();
            }

            try
            {
                if (Directory.Exists(_testRootPath))
                    Directory.Delete(_testRootPath, true);
            }
            catch
            {
            }
        }

        private AppSettingsService CreateIsolatedSettingsService()
        {
            var service = new AppSettingsService();
            var settingsDir = Path.Combine(_testRootPath, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(settingsDir);

            typeof(AppSettingsService).GetField("_settingsPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, Path.Combine(settingsDir, "settings.json"));
            typeof(AppSettingsService).GetField("_backupPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(service, Path.Combine(settingsDir, "settings.json.bak"));

            service.Settings.RememberProfileLogin = false;
            service.Settings.ProfileClientId = string.Empty;
            service.Settings.ProfileSessionVersion = 0;
            service.Settings.EncryptedProfileSessionToken = string.Empty;

            _settingsServices.Add(service);
            return service;
        }
    }
}

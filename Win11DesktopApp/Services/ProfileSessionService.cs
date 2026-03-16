using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class ProfileSessionService
    {
        private readonly AppSettingsService _appSettingsService;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

        public ProfileSessionService(AppSettingsService appSettingsService)
        {
            _appSettingsService = appSettingsService;
        }

        public bool TryRestoreRememberedSession(ClientProfileRecord profile)
        {
            try
            {
                var settings = _appSettingsService.Settings;
                if (!settings.RememberProfileLogin)
                    return false;
                if (string.IsNullOrWhiteSpace(settings.EncryptedProfileSessionToken))
                    return false;
                if (!profile.RememberMeEnabled)
                    return false;

                var payload = Unprotect(settings.EncryptedProfileSessionToken);
                if (payload == null)
                    return false;

                return string.Equals(payload.ClientId, profile.ClientId, StringComparison.Ordinal)
                    && payload.SessionVersion == profile.SessionVersion
                    && string.Equals(settings.ProfileClientId, profile.ClientId, StringComparison.Ordinal)
                    && settings.ProfileSessionVersion == profile.SessionVersion;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("ProfileSessionService.TryRestore", ex.Message);
                return false;
            }
        }

        public void SaveRememberedSession(ClientProfileRecord profile)
        {
            var payload = new RememberedProfileSession
            {
                ClientId = profile.ClientId,
                SessionVersion = profile.SessionVersion,
                RememberedAtUtc = DateTime.UtcNow
            };

            _appSettingsService.Settings.RememberProfileLogin = true;
            _appSettingsService.Settings.ProfileClientId = profile.ClientId;
            _appSettingsService.Settings.ProfileSessionVersion = profile.SessionVersion;
            _appSettingsService.Settings.EncryptedProfileSessionToken = Protect(payload);
            _appSettingsService.SaveSettings();
        }

        public void ClearRememberedSession()
        {
            _appSettingsService.Settings.RememberProfileLogin = false;
            _appSettingsService.Settings.ProfileClientId = string.Empty;
            _appSettingsService.Settings.ProfileSessionVersion = 0;
            _appSettingsService.Settings.EncryptedProfileSessionToken = string.Empty;
            _appSettingsService.SaveSettings();
        }

        private static string Protect(RememberedProfileSession payload)
        {
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json),
                GetEntropy(),
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        private static RememberedProfileSession? Unprotect(string token)
        {
            var encryptedBytes = Convert.FromBase64String(token);
            var decrypted = ProtectedData.Unprotect(encryptedBytes, GetEntropy(), DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<RememberedProfileSession>(json, _jsonOptions);
        }

        private static byte[] GetEntropy()
        {
            return Encoding.UTF8.GetBytes("AC-ProfileSession-2026");
        }
    }
}

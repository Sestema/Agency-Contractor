using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Win11DesktopApp.Services;

public sealed class BusinessUserSessionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly AppSettingsService _appSettingsService;
    private readonly BusinessUserAuthService _businessUserAuthService;

    public BusinessUserSessionService(
        AppSettingsService appSettingsService,
        BusinessUserAuthService businessUserAuthService)
    {
        _appSettingsService = appSettingsService;
        _businessUserAuthService = businessUserAuthService;
    }

    public bool IsRememberEnabled =>
        _appSettingsService.Settings.RememberBusinessUserLogin
        && !string.IsNullOrWhiteSpace(_appSettingsService.Settings.EncryptedBusinessUserSessionToken);

    public bool TryGetRememberedLogin(out string login)
    {
        login = string.Empty;
        var payload = TryReadPayload();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Login))
            return false;

        login = payload.Login;
        return true;
    }

    public bool TryRestoreRememberedSession()
    {
        try
        {
            var settings = _appSettingsService.Settings;
            if (!settings.RememberBusinessUserLogin)
                return false;
            if (string.IsNullOrWhiteSpace(settings.EncryptedBusinessUserSessionToken))
                return false;

            var payload = TryReadPayload();
            if (payload == null)
                return false;

            if (!string.IsNullOrWhiteSpace(payload.RootFolderPath)
                && !string.Equals(
                    NormalizePath(settings.RootFolderPath),
                    NormalizePath(payload.RootFolderPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var user = settings.BusinessUsers.FirstOrDefault(candidate =>
                candidate.IsActive
                && string.Equals(candidate.UserId, payload.UserId, StringComparison.OrdinalIgnoreCase));
            if (user == null)
                return false;

            _businessUserAuthService.ActivateSession(user);
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning("BusinessUserSessionService.TryRestore", ex.Message);
            return false;
        }
    }

    public void SaveRememberedSession(AppSettingsService.BusinessUserSetting user)
    {
        var payload = new RememberedBusinessUserSession
        {
            UserId = user.UserId,
            Login = user.Login,
            RootFolderPath = _appSettingsService.Settings.RootFolderPath ?? string.Empty,
            RememberedAtUtc = DateTime.UtcNow
        };

        _appSettingsService.Settings.RememberBusinessUserLogin = true;
        _appSettingsService.Settings.RememberedBusinessUserId = user.UserId;
        _appSettingsService.Settings.EncryptedBusinessUserSessionToken = Protect(payload);
        _appSettingsService.SaveSettings();
    }

    public void ClearRememberedSession()
    {
        _appSettingsService.Settings.RememberBusinessUserLogin = false;
        _appSettingsService.Settings.RememberedBusinessUserId = string.Empty;
        _appSettingsService.Settings.EncryptedBusinessUserSessionToken = string.Empty;
        _appSettingsService.SaveSettings();
    }

    private RememberedBusinessUserSession? TryReadPayload()
    {
        var token = _appSettingsService.Settings.EncryptedBusinessUserSessionToken;
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return Unprotect(token);
    }

    private static string Protect(RememberedBusinessUserSession payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            GetEntropy(),
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static RememberedBusinessUserSession? Unprotect(string token)
    {
        var encryptedBytes = Convert.FromBase64String(token);
        var decrypted = ProtectedData.Unprotect(encryptedBytes, GetEntropy(), DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(decrypted);
        return JsonSerializer.Deserialize<RememberedBusinessUserSession>(json, JsonOptions);
    }

    private static byte[] GetEntropy() =>
        Encoding.UTF8.GetBytes("AC-BusinessUserSession-2026");

    private static string NormalizePath(string? path) =>
        (path ?? string.Empty).Trim().TrimEnd('\\', '/');
}

public sealed class RememberedBusinessUserSession
{
    public string UserId { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string RootFolderPath { get; set; } = string.Empty;
    public DateTime RememberedAtUtc { get; set; }
}

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Win11DesktopApp.Services;

public sealed class BusinessUserAuthService
{
    private readonly AppSettingsService _appSettingsService;
    private readonly CurrentProfileService _currentProfileService;

    public BusinessUserAuthService(
        AppSettingsService appSettingsService,
        CurrentProfileService currentProfileService)
    {
        _appSettingsService = appSettingsService;
        _currentProfileService = currentProfileService;
    }

    public static bool ShouldShowStartupRoleSelection(AppSettingsService.AppSettings settings) =>
        UnifiedLoginService.ShouldShowUnifiedLogin(settings);

    public static bool HasLocalActiveBusinessUsers(AppSettingsService.AppSettings settings) =>
        settings.BusinessUsers.Any(user => user.IsActive);

    public bool HasLocalActiveBusinessUsers() =>
        HasLocalActiveBusinessUsers(_appSettingsService.Settings);

    public AppSettingsService.BusinessUserSetting? FindActiveUserByLogin(string login)
    {
        var normalized = NormalizeLogin(login);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return _appSettingsService.Settings.BusinessUsers.FirstOrDefault(user =>
            user.IsActive
            && string.Equals(NormalizeLogin(user.Login), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public BusinessUserLoginResult TryLoginByLogin(string login, string password)
    {
        var user = FindActiveUserByLogin(login);
        if (user == null)
            return BusinessUserLoginResult.Failed("not_found");

        return TryLogin(user, password);
    }

    public BusinessUserLoginResult TryLoginByUserId(string userId, string password)
    {
        var user = _appSettingsService.Settings.BusinessUsers.FirstOrDefault(candidate =>
            candidate.IsActive
            && string.Equals(candidate.UserId, userId, StringComparison.OrdinalIgnoreCase));
        if (user == null)
            return BusinessUserLoginResult.Failed("not_found");

        return TryLogin(user, password);
    }

    public BusinessUserLoginResult TryLogin(AppSettingsService.BusinessUserSetting user, string password)
    {
        if (!VerifyPassword(user, password))
            return BusinessUserLoginResult.Failed("wrong_password");

        user.LastLoginAtUtc = DateTime.UtcNow;
        ActivateSession(user);
        return BusinessUserLoginResult.Succeeded(user);
    }

    public void ActivateSession(AppSettingsService.BusinessUserSetting user)
    {
        _appSettingsService.Settings.CurrentBusinessUserId = user.UserId;
        _appSettingsService.SaveSettings();
        _currentProfileService.SetCurrentBusinessUser(user);
    }

    public void LogoutSession()
    {
        _appSettingsService.Settings.CurrentBusinessUserId = string.Empty;
        _appSettingsService.SaveSettings();
        _currentProfileService.SetCurrentBusinessUser(null);
    }

    public static bool VerifyPassword(AppSettingsService.BusinessUserSetting user, string password)
    {
        if (string.IsNullOrWhiteSpace(user.PasswordHash) || string.IsNullOrWhiteSpace(user.PasswordSalt))
            return false;

        var hash = HashPassword(password, user.PasswordSalt);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hash),
            Encoding.UTF8.GetBytes(user.PasswordHash));
    }

    public static string GenerateSalt()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static string HashPassword(string password, string salt)
    {
        var input = Encoding.UTF8.GetBytes($"{salt}|{password}");
        return Convert.ToBase64String(SHA256.HashData(input));
    }

    public static string NormalizeLogin(string value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}

public sealed class BusinessUserLoginResult
{
    public bool Success { get; init; }
    public string FailureReason { get; init; } = string.Empty;
    public AppSettingsService.BusinessUserSetting? User { get; init; }

    public static BusinessUserLoginResult Succeeded(AppSettingsService.BusinessUserSetting user) =>
        new() { Success = true, User = user };

    public static BusinessUserLoginResult Failed(string reason) =>
        new() { Success = false, FailureReason = reason };
}

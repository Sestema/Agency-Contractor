using System.Text;
using System.Threading.Tasks;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services;

public sealed class UnifiedLoginService
{
    private readonly BusinessUserAuthService _businessUserAuthService;
    private readonly BusinessUserDirectoryService _businessUserDirectoryService;
    private readonly BusinessUserSessionService _businessUserSessionService;
    private readonly ProfileAuthService _profileAuthService;
    private readonly ProfileSessionService _profileSessionService;
    private readonly AppSettingsService _appSettingsService;

    public UnifiedLoginService(
        BusinessUserAuthService businessUserAuthService,
        BusinessUserDirectoryService businessUserDirectoryService,
        BusinessUserSessionService businessUserSessionService,
        ProfileAuthService profileAuthService,
        ProfileSessionService profileSessionService,
        AppSettingsService appSettingsService)
    {
        _businessUserAuthService = businessUserAuthService;
        _businessUserDirectoryService = businessUserDirectoryService;
        _businessUserSessionService = businessUserSessionService;
        _profileAuthService = profileAuthService;
        _profileSessionService = profileSessionService;
        _appSettingsService = appSettingsService;
    }

    public static bool ShouldShowUnifiedLogin(AppSettingsService.AppSettings settings)
    {
        if (settings.PendingMemberRoleSelection)
            return true;

        if (!settings.ExperimentalMultiUser)
            return false;

        if (settings.RememberProfileLogin
            && !string.IsNullOrWhiteSpace(settings.EncryptedProfileSessionToken))
        {
            return false;
        }

        if (settings.RememberBusinessUserLogin
            && !string.IsNullOrWhiteSpace(settings.EncryptedBusinessUserSessionToken))
        {
            return false;
        }

        return true;
    }

    public void ImportBusinessUsersFromRootIfAvailable()
    {
        if (string.IsNullOrWhiteSpace(_appSettingsService.Settings.RootFolderPath))
            return;

        _businessUserDirectoryService.ImportFromRootFolder();
    }

    public bool TryImportBusinessUsersFromRoot(out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(_appSettingsService.Settings.RootFolderPath))
            return false;

        var result = _businessUserDirectoryService.ImportFromRootFolder();
        if (result.Success)
            return true;

        errorMessage = result.Message;
        return false;
    }

    public async Task<UnifiedLoginAttemptResult> AuthenticateAsync(
        string login,
        string password,
        string? clientId,
        ClientProfileRecord? ownerProfile,
        bool rememberLogin)
    {
        ImportBusinessUsersFromRootIfAvailable();

        var businessUser = _businessUserAuthService.FindActiveUserByLogin(login);
        if (businessUser != null)
        {
            var businessResult = _businessUserAuthService.TryLogin(businessUser, password);
            if (!businessResult.Success)
            {
                return UnifiedLoginAttemptResult.Failed(
                    businessResult.FailureReason == "wrong_password"
                        ? Res("UnifiedLoginWrongPassword")
                        : Res("UnifiedLoginInvalidCredentials"));
            }

            if (rememberLogin)
                _businessUserSessionService.SaveRememberedSession(businessResult.User!);
            else
                _businessUserSessionService.ClearRememberedSession();

            return UnifiedLoginAttemptResult.MemberSuccess(businessResult.User!);
        }

        if (ownerProfile == null
            || string.IsNullOrWhiteSpace(clientId)
            || !MatchesOwnerProfileLogin(ownerProfile, login))
        {
            return UnifiedLoginAttemptResult.Failed(Res("UnifiedLoginInvalidCredentials"));
        }

        var auth = await _profileAuthService.AuthenticateAsync(clientId, password).ConfigureAwait(false);
        if (!auth.Success || auth.Profile == null)
        {
            return UnifiedLoginAttemptResult.Failed(
                string.IsNullOrWhiteSpace(auth.ErrorMessage)
                    ? Res("UnifiedLoginWrongPassword")
                    : auth.ErrorMessage);
        }

        var rememberResult = await _profileAuthService.UpdateRememberMeAsync(auth.Profile.ClientId, rememberLogin)
            .ConfigureAwait(false);
        if (!rememberResult.Success || rememberResult.Profile == null)
        {
            return UnifiedLoginAttemptResult.Failed(
                string.IsNullOrWhiteSpace(rememberResult.ErrorMessage)
                    ? Res("UnifiedLoginRememberMeFailed")
                    : rememberResult.ErrorMessage);
        }

        if (rememberLogin)
            _profileSessionService.SaveRememberedSession(rememberResult.Profile);
        else
            _profileSessionService.ClearRememberedSession();

        return UnifiedLoginAttemptResult.OwnerSuccess(rememberResult.Profile);
    }

    public static bool MatchesOwnerProfileLogin(ClientProfileRecord profile, string login)
    {
        var normalized = BusinessUserAuthService.NormalizeLogin(login);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var first = NormalizeLoginPart(profile.FirstName);
        var last = NormalizeLoginPart(profile.LastName);
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
            return false;

        var dotted = string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last)
            ? string.Empty
            : $"{first}.{last}";
        var combined = $"{first}{last}";

        return (!string.IsNullOrWhiteSpace(dotted)
                && string.Equals(normalized, dotted, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(combined)
                   && string.Equals(normalized, combined, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(first)
                   && string.Equals(normalized, first, StringComparison.OrdinalIgnoreCase))
               || (!string.IsNullOrWhiteSpace(last)
                   && string.Equals(normalized, last, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeLoginPart(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string Res(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as string ?? key;
}

public sealed class UnifiedLoginAttemptResult
{
    public UnifiedLoginKind Kind { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public ClientProfileRecord? OwnerProfile { get; init; }
    public AppSettingsService.BusinessUserSetting? MemberUser { get; init; }

    public bool Success => Kind is UnifiedLoginKind.Owner or UnifiedLoginKind.Member;

    public static UnifiedLoginAttemptResult OwnerSuccess(ClientProfileRecord profile) =>
        new() { Kind = UnifiedLoginKind.Owner, OwnerProfile = profile };

    public static UnifiedLoginAttemptResult MemberSuccess(AppSettingsService.BusinessUserSetting user) =>
        new() { Kind = UnifiedLoginKind.Member, MemberUser = user };

    public static UnifiedLoginAttemptResult Failed(string message) =>
        new() { Kind = UnifiedLoginKind.Failed, ErrorMessage = message };
}

public enum UnifiedLoginKind
{
    Failed,
    Owner,
    Member
}

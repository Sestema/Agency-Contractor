using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services;

public sealed class CurrentProfileService
{
    public ClientProfileRecord? CurrentProfile { get; private set; }
    public AppSettingsService.BusinessUserSetting? CurrentBusinessUser { get; private set; }

    public void SetCurrentProfile(ClientProfileRecord? profile)
    {
        CurrentProfile = profile;
    }

    public void SetCurrentBusinessUser(AppSettingsService.BusinessUserSetting? user)
    {
        CurrentBusinessUser = user;
    }

    public string CurrentActorDisplayName
    {
        get
        {
            if (CurrentBusinessUser != null)
            {
                var businessName = $"{CurrentBusinessUser.FirstName} {CurrentBusinessUser.LastName}".Trim();
                if (!string.IsNullOrWhiteSpace(businessName))
                    return businessName;
            }

            if (CurrentProfile != null)
                return $"{CurrentProfile.FirstName} {CurrentProfile.LastName}".Trim();

            return string.Empty;
        }
    }
}

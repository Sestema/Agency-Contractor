using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services;

public sealed class CurrentProfileService
{
    public ClientProfileRecord? CurrentProfile { get; private set; }

    public void SetCurrentProfile(ClientProfileRecord? profile)
    {
        CurrentProfile = profile;
    }
}

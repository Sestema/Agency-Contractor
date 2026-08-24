using Win11DesktopApp.Services;

namespace Win11DesktopApp.AppStartup
{
    public sealed class StartupFlowState
    {
        public bool SkipLicenseGate { get; init; }
        public LocalLicenseStatus LocalLicenseStatus { get; set; } = null!;
        public ClientAccessState StartupAccess { get; set; } = new();
        public string? StartupClientId { get; set; }
        public bool IsRemoteTrialExpired { get; set; }
        public RemotePolicy? StartupPolicy { get; set; }
    }
}

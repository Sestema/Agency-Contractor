namespace Win11DesktopApp.Services;

public sealed class GeminiApiKeyConfigurationService
{
    private readonly AppSettingsService _appSettingsService;
    private readonly GeminiApiService _geminiApiService;
    private readonly AccessStatusService _accessStatusService;

    public GeminiApiKeyConfigurationService(
        AppSettingsService appSettingsService,
        GeminiApiService geminiApiService,
        AccessStatusService accessStatusService)
    {
        _appSettingsService = appSettingsService;
        _geminiApiService = geminiApiService;
        _accessStatusService = accessStatusService;
    }

    public void RefreshEffectiveApiKey()
    {
        var policy = PolicyService.CurrentPolicy;
        if (policy?.DisableAI == true || PolicyService.IsAIDisabled)
        {
            _geminiApiService.SetApiKey(null);
            return;
        }

        var userKey = _appSettingsService.Settings.GeminiApiKey;
        var accessState = _accessStatusService.ClientAccessState;
        var serverKey = accessState.IsLive ? accessState.ManagedGeminiApiKey : string.Empty;
        var effectiveKey = !string.IsNullOrWhiteSpace(userKey) ? userKey : serverKey;
        _geminiApiService.SetApiKey(!string.IsNullOrWhiteSpace(effectiveKey) ? effectiveKey : null);
    }
}

using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace Win11DesktopApp.Services
{
    public static class UpdateService
    {
        private const string RepoUrl = "https://github.com/Sestema/Agency-Contractor";

        private static UpdateManager? _mgr;
        private static string _managerChannel = "stable";

        public static UpdateManager GetManager(string? channel = null)
        {
            var effectiveChannel = string.IsNullOrWhiteSpace(channel)
                ? "stable"
                : channel;
            var prerelease = string.Equals(effectiveChannel, "beta", StringComparison.OrdinalIgnoreCase);

            if (_mgr == null || !string.Equals(_managerChannel, effectiveChannel, StringComparison.OrdinalIgnoreCase))
            {
                _managerChannel = effectiveChannel;
                _mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: prerelease));
            }

            return _mgr;
        }

        public static async Task<UpdateInfo?> CheckForUpdatesAsync(string? channel = null)
        {
            try
            {
                var mgr = GetManager(channel);
                var result = await mgr.CheckForUpdatesAsync();
                if (result != null) LoggingService.LogInfo("UpdateService", $"Update available: {result.TargetFullRelease.Version}");
                return result;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("UpdateService.CheckForUpdates", ex.Message);
                return null;
            }
        }

        public static async Task<bool> DownloadAndApplyAsync(UpdateInfo updateInfo, Action<int>? progressCallback = null, string? channel = null)
        {
            try
            {
                var mgr = GetManager(channel);
                await mgr.DownloadUpdatesAsync(updateInfo, progress: progressCallback);
                mgr.ApplyUpdatesAndRestart(updateInfo);
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("UpdateService.DownloadAndApply", ex.Message);
                return false;
            }
        }

        public static bool IsInstalled
        {
            get
            {
                try { return GetManager().IsInstalled; }
                catch (Exception ex) { LoggingService.LogWarning("UpdateService.IsInstalled", ex.Message); return false; }
            }
        }
    }
}

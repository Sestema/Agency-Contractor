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

        public static UpdateManager GetManager()
        {
            _mgr ??= new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            return _mgr;
        }

        public static async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            try
            {
                var mgr = GetManager();
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

        public static async Task<bool> DownloadAndApplyAsync(UpdateInfo updateInfo, Action<int>? progressCallback = null)
        {
            try
            {
                var mgr = GetManager();
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

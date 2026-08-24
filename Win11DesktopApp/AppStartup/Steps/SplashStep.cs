using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class SplashStep : IStartupStep
    {
        public string Name => "splash";

        public Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            App.ShowSplashWindow();
            return Task.FromResult(StartupStepResult.Continue);
        }
    }
}

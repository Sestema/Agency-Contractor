using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class BackgroundHostedServicesStep : IStartupStep
    {
        public string Name => "background";

        public async Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            await App.FinalizeStartupAsync(ctx.Integrity!, ctx.State);
            return StartupStepResult.Continue;
        }
    }
}

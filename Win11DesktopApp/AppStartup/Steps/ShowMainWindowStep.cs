using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class ShowMainWindowStep : IStartupStep
    {
        public string Name => "window";

        public Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            ctx.App.ShowMainWindow(ctx.LogStartupPhase);
            return Task.FromResult(StartupStepResult.Continue);
        }
    }
}

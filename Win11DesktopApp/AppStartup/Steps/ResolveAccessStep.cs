using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class ResolveAccessStep : IStartupStep
    {
        public string Name => "access";

        public async Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            ctx.State = App.CreateStartupFlowState();
            await App.ResolveStartupAccessAsync(ctx.State, ctx.LogStartupPhase);
            return StartupStepResult.Continue;
        }
    }
}

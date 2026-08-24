using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class UnifiedLoginStep : IStartupStep
    {
        public string Name => "login";

        public async Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            ctx.LoginResult = await ctx.App.RunMultiUserStartupGateAsync(ctx.State, ctx.LogStartupPhase);
            return ctx.LoginResult == MultiUserStartupResult.Cancelled
                ? StartupStepResult.Stop
                : StartupStepResult.Continue;
        }
    }
}

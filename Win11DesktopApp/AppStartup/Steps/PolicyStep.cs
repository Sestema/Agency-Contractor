using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class PolicyStep : IStartupStep
    {
        public string Name => "license";

        public async Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            await App.TryMigrateLegacyLicenseAsync(ctx.State, ctx.LogStartupPhase);
            return await ctx.App.ApplyStartupPolicyAsync(ctx.State, ctx.LogStartupPhase)
                ? StartupStepResult.Continue
                : StartupStepResult.Stop;
        }
    }
}

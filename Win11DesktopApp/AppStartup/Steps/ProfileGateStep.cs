using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class ProfileGateStep : IStartupStep
    {
        public string Name => "profile";

        public async Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            if (ctx.LoginResult == MultiUserStartupResult.MemberLoggedIn)
                return StartupStepResult.Continue;

            if (!await ctx.App.RunProfileGateAsync(ctx.State, ctx.LogStartupPhase))
                return StartupStepResult.Stop;

            if (ctx.LoginResult == MultiUserStartupResult.OwnerSelected)
                App.ClearBusinessUserSession();
            else
                App.RestoreBusinessUserSession();

            return StartupStepResult.Continue;
        }
    }
}

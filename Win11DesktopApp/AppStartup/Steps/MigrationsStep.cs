using System.Threading;
using System.Threading.Tasks;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class MigrationsStep : IStartupStep
    {
        public string Name => "migrations";

        public Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            App.RunStartupMigrations();
            ctx.App.StartStatisticsSession();
            LoggingService.LogInfo("App", "All services initialized");
            ctx.LogStartupPhase("services_initialized");
            return Task.FromResult(StartupStepResult.Continue);
        }
    }
}

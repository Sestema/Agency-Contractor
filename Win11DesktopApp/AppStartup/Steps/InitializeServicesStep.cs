using System;
using System.Threading;
using System.Threading.Tasks;
#if DEBUG
using Win11DesktopApp.Diagnostics;
#endif
using Win11DesktopApp.Services;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class InitializeServicesStep : IStartupStep
    {
        public string Name => "init";

        public Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            try
            {
                ctx.Integrity = App.InitializeCoreServices();
#if DEBUG
                BindingErrorTraceListener.Enable();
#endif
                ctx.LogStartupPhase("startup_begin");
                ctx.App.IncludeFinanceStartupState(ctx.Integrity);
                App.RunBackgroundWarmupTasks();
                return Task.FromResult(StartupStepResult.Continue);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("App.OnStartup.Init", ex);
                ErrorHandler.Report("App.OnStartup", ex, ErrorSeverity.Critical, showUser: true);
                ctx.App.Shutdown(-1);
                return Task.FromResult(StartupStepResult.Stop);
            }
        }
    }
}

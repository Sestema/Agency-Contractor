using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup.Steps
{
    public sealed class LanguageThemeStep : IStartupStep
    {
        public string Name => "language/theme";

        public Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct)
        {
            App.ApplySavedLanguageAndTheme();
            return Task.FromResult(StartupStepResult.Continue);
        }
    }
}

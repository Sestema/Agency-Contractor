using System.Threading;
using System.Threading.Tasks;

namespace Win11DesktopApp.AppStartup
{
    public interface IStartupStep
    {
        string Name { get; }
        Task<StartupStepResult> RunAsync(StartupContext ctx, CancellationToken ct);
    }
}

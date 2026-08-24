using System;
using System.Threading;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.AppStartup
{
    public sealed class StartupContext
    {
        public StartupContext(App app, Action<string> logStartupPhase, CancellationToken token)
        {
            App = app ?? throw new ArgumentNullException(nameof(app));
            LogStartupPhase = logStartupPhase ?? throw new ArgumentNullException(nameof(logStartupPhase));
            Token = token;
        }

        public App App { get; }
        public Action<string> LogStartupPhase { get; }
        public CancellationToken Token { get; }
        public StartupFlowState State { get; set; } = null!;
        public StartupIntegrityService? Integrity { get; set; }
        public MultiUserStartupResult LoginResult { get; set; }
    }
}

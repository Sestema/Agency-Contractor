#if DEBUG
using System.Diagnostics;
using System.Text;
using System.Windows;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Diagnostics
{
    /// <summary>
    /// DEBUG-only listener that routes WPF data-binding failures into
    /// <see cref="LoggingService"/>. Failing bindings keep re-evaluating and
    /// silently burn CPU; surfacing them in the log lets us find and remove the
    /// ones that hurt performance. Compiled out entirely in Release builds.
    /// </summary>
    internal sealed class BindingErrorTraceListener : TraceListener
    {
        private readonly StringBuilder _buffer = new();
        private static bool _enabled;

        public static void Enable()
        {
            if (_enabled) return;
            _enabled = true;

            PresentationTraceSources.Refresh();
            var source = PresentationTraceSources.DataBindingSource;
            source.Listeners.Add(new BindingErrorTraceListener());
            source.Switch.Level = SourceLevels.Warning;
        }

        public override void Write(string? message) => _buffer.Append(message);

        public override void WriteLine(string? message)
        {
            _buffer.Append(message);
            var full = _buffer.ToString().Trim();
            _buffer.Clear();

            if (!string.IsNullOrEmpty(full))
                LoggingService.LogWarning("WPF.Binding", full);
        }
    }
}
#endif

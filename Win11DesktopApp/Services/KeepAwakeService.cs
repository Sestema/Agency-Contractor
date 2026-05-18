using System;
using System.Runtime.InteropServices;

namespace Win11DesktopApp.Services
{
    public sealed class KeepAwakeService
    {
        [Flags]
        private enum ExecutionState : uint
        {
            Continuous = 0x80000000,
            SystemRequired = 0x00000001,
            AwayModeRequired = 0x00000040
        }

        private bool _isActive;

        public bool IsActive => _isActive;

        public void Start()
        {
            if (_isActive)
                return;

            SetThreadExecutionState(
                ExecutionState.Continuous |
                ExecutionState.SystemRequired |
                ExecutionState.AwayModeRequired);
            _isActive = true;
            LoggingService.LogInfo("KeepAwakeService", "Enabled while web panel is running.");
        }

        public void Stop()
        {
            if (!_isActive)
                return;

            SetThreadExecutionState(ExecutionState.Continuous);
            _isActive = false;
            LoggingService.LogInfo("KeepAwakeService", "Disabled.");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);
    }
}

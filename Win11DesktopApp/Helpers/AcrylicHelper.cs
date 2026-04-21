using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Win11DesktopApp.Helpers
{
    public static class AcrylicHelper
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left, Right, Top, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0;
        private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private const int DWMSBT_AUTO = 0;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;      // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4;    // Mica Alt

        public enum BackdropKind
        {
            Mica = DWMSBT_MAINWINDOW,
            Acrylic = DWMSBT_TRANSIENTWINDOW,
            MicaAlt = DWMSBT_TABBEDWINDOW
        }

        private static bool _isEnabled;
        private static Color _previousCompositionColor;

        public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

        public static void EnableAcrylic(Window window, bool isDark)
        {
            EnableBackdrop(window, isDark, BackdropKind.Acrylic);
        }

        public static void EnableMica(Window window, bool isDark)
        {
            EnableBackdrop(window, isDark, BackdropKind.Mica);
        }

        public static void EnableBackdrop(Window window, bool isDark, BackdropKind kind)
        {
            if (window == null) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var hwndSource = HwndSource.FromHwnd(hwnd);
            if (hwndSource?.CompositionTarget == null) return;

            _previousCompositionColor = hwndSource.CompositionTarget.BackgroundColor;

            if (TryEnableWin11Backdrop(hwnd, isDark, kind))
            {
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                hwndSource.CompositionTarget.BackgroundColor = Colors.Transparent;
                window.Background = Brushes.Transparent;
                _isEnabled = true;
                return;
            }

            if (kind == BackdropKind.Acrylic && TryEnableWin10Acrylic(hwnd, isDark))
            {
                _isEnabled = true;
            }
        }

        public static void DisableAcrylic(Window window)
        {
            if (window == null || !_isEnabled) return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            TryDisableWin11Backdrop(hwnd);
            TryDisableWin10Acrylic(hwnd);

            var hwndSource = HwndSource.FromHwnd(hwnd);
            if (hwndSource?.CompositionTarget != null)
            {
                hwndSource.CompositionTarget.BackgroundColor = _previousCompositionColor;

                var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
            }

            window.SetResourceReference(Window.BackgroundProperty, "WindowBackgroundBrush");

            _isEnabled = false;
        }

        public static void ApplyImmersiveDarkMode(Window window, bool isDark)
        {
            if (window == null) return;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
            }
            catch { }
        }

        private static bool TryEnableWin11Backdrop(IntPtr hwnd, bool isDark, BackdropKind kind)
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22621)
                    return false;

                int darkMode = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int backdropType = (int)kind;
                int hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryEnableWin10Acrylic(IntPtr hwnd, bool isDark)
        {
            try
            {
                uint tintColor = isDark ? 0xA01E2235u : 0xA0FAFAFAu;

                var accent = new AccentPolicy
                {
                    AccentState = ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags = 2,
                    GradientColor = tintColor
                };

                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);

                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };

                    int result = SetWindowCompositionAttribute(hwnd, ref data);
                    return result != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch
            {
                return false;
            }
        }

        private static void TryDisableWin11Backdrop(IntPtr hwnd)
        {
            try
            {
                if (Environment.OSVersion.Version.Build < 22621) return;
                int backdropType = 1; // DWMSBT_NONE
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
            catch { }
        }

        private static void TryDisableWin10Acrylic(IntPtr hwnd)
        {
            try
            {
                var accent = new AccentPolicy { AccentState = ACCENT_DISABLED };
                int accentSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);
                try
                {
                    Marshal.StructureToPtr(accent, accentPtr, false);

                    var data = new WindowCompositionAttributeData
                    {
                        Attribute = WCA_ACCENT_POLICY,
                        Data = accentPtr,
                        SizeOfData = accentSize
                    };

                    SetWindowCompositionAttribute(hwnd, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(accentPtr);
                }
            }
            catch { }
        }
    }
}

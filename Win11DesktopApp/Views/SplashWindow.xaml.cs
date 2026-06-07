using System;
using System.Windows;
using System.Windows.Media.Animation;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class SplashWindow : Window
    {
        private bool _closing;

        public SplashWindow()
        {
            InitializeComponent();
            try
            {
                VersionText.Text = $"v{AppSettingsService.CurrentAppVersion}";
            }
            catch
            {
                VersionText.Text = string.Empty;
            }
        }

        /// <summary>
        /// Fades the splash out smoothly and closes it. Safe to call multiple times.
        /// </summary>
        public void FadeOutAndClose()
        {
            if (_closing)
                return;
            _closing = true;

            try
            {
                var fade = new DoubleAnimation
                {
                    From = Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(380)
                };
                fade.Completed += (_, _) => SafeClose();
                BeginAnimation(OpacityProperty, fade);
            }
            catch
            {
                SafeClose();
            }
        }

        private void SafeClose()
        {
            try { Close(); }
            catch { /* window may already be closed */ }
        }
    }
}

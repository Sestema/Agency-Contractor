using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using Velopack;

namespace Win11DesktopApp
{
    public static class Program
    {
        private const string AppMutexName = @"Local\AgencyContractor.Win11DesktopApp";
        public const string RestartArgument = "--restart";

        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp.Build().Run();

            var isRestart = HasRestartFlag(args);
            using var mutex = new Mutex(initiallyOwned: false, AppMutexName);
            if (!TryAcquireMutex(mutex, isRestart))
            {
                MessageBox.Show(
                    "Програма вже запущена. Закрийте інше вікно або використовуйте вже відкриту програму.",
                    "Agency Contractor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                var app = new App();
                app.InitializeComponent();
                app.Run();
            }
            finally
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }
            }
        }

        public static ProcessStartInfo CreateRestartStartInfo(string exePath, bool runAsAdministrator = false)
        {
            var info = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Arguments = RestartArgument
            };
            if (runAsAdministrator)
                info.Verb = "runas";
            return info;
        }

        private static bool HasRestartFlag(string[] args) =>
            args.Any(argument => string.Equals(argument, RestartArgument, StringComparison.OrdinalIgnoreCase));

        private static bool TryAcquireMutex(Mutex mutex, bool isRestart)
        {
            try
            {
                return isRestart
                    ? mutex.WaitOne(TimeSpan.FromSeconds(20))
                    : mutex.WaitOne(TimeSpan.Zero);
            }
            catch (AbandonedMutexException)
            {
                return true;
            }
        }
    }
}

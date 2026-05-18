using System;
using System.Threading;
using System.Windows;
using Velopack;

namespace Win11DesktopApp
{
    public static class Program
    {
        private const string AppMutexName = @"Local\AgencyContractor.Win11DesktopApp";

        [STAThread]
        public static void Main()
        {
            VelopackApp.Build().Run();

            using var mutex = new Mutex(initiallyOwned: true, AppMutexName, out var createdNew);
            if (!createdNew)
            {
                MessageBox.Show(
                    "Програма вже запущена. Закрийте інше вікно або використовуйте вже відкриту програму.",
                    "Agency Contractor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}

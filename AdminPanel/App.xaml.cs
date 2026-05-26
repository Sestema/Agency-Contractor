using System.Windows;
using System.Windows.Threading;

namespace AdminPanel
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            base.OnStartup(e);
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var details = e.Exception.Message;
            if (e.Exception.InnerException != null)
                details += $"\n\nДеталі:\n{e.Exception.InnerException.Message}";

            MessageBox.Show(
                $"Сталася помилка в Admin Panel:\n{details}",
                "Admin Panel",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}

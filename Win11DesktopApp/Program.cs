using System;
using Velopack;

namespace Win11DesktopApp
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;
using Win11DesktopApp.Services.Scanning;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class ScanningModule
    {
        public static IServiceCollection AddScanning(this IServiceCollection services)
        {
            services.AddSingleton<ImageEnhancementService>();
            services.AddSingleton<IScannerService, CompositeScannerService>();
            services.AddSingleton<IScanDocumentAssemblyService, ScanDocumentAssemblyService>();
            return services;
        }
    }
}

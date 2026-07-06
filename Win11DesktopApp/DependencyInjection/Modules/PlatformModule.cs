using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class PlatformModule
    {
        public static IServiceCollection AddPlatform(this IServiceCollection services)
        {
            services.AddSingleton<KeepAwakeService>();
            services.AddSingleton<WebPanelHostService>();
            return services;
        }
    }
}

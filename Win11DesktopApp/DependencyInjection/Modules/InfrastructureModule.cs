using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class InfrastructureModule
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services)
        {
            services.AddSingleton(sp => new NavigationService(sp));
            services.AddSingleton<ThemeService>();
            services.AddSingleton<LanguageService>();
            services.AddSingleton(_ => new AppSettingsService(suppressStartupNotifications: true));
            services.AddSingleton<AccessStatusService>();
            services.AddSingleton(sp => new FolderService(sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton<WeatherService>();
            return services;
        }
    }
}

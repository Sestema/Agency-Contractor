using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class AuthModule
    {
        public static IServiceCollection AddAuth(this IServiceCollection services)
        {
            services.AddSingleton<BusinessUserAuthService>();
            services.AddSingleton<BusinessUserDirectoryService>();
            services.AddSingleton<WorkspaceSessionService>();
            services.AddSingleton<BusinessUserSessionService>();
            services.AddSingleton<UnifiedLoginService>();
            services.AddSingleton<ProfileAuthService>();
            services.AddSingleton(sp => new ProfileSessionService(sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton<CurrentProfileService>();
            return services;
        }
    }
}

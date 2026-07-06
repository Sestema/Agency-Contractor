using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class SyncModule
    {
        public static IServiceCollection AddSyncAndNotifications(this IServiceCollection services)
        {
            services.AddSingleton<TagCatalogService>();
            services.AddSingleton<AppNotificationService>();
            services.AddSingleton(sp => new SyncEventService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<CurrentProfileService>(),
                sp.GetRequiredService<AppNotificationService>(),
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton<AppUpdateNotificationService>();
            services.AddSingleton<SharedOperationLockService>();
            services.AddSingleton(sp => new ConnectedClientsService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<AppDataStorageFactory>(),
                sp.GetRequiredService<CurrentProfileService>()));
            return services;
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class StorageModule
    {
        public static IServiceCollection AddStorage(this IServiceCollection services)
        {
            services.AddSingleton(sp => new CoreDbService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new SalaryDbService(sp.GetRequiredService<FolderService>()));
            services.AddSingleton(sp => new LocalDbService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<SalaryDbService>()));
            services.AddSingleton(sp => new EmployeeIndexDbService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton(sp => new AppDataStorageFactory(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<CoreDbService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<SalaryDbService>()));
            services.AddSingleton(sp => new PersistenceService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppDataStorageFactory>().CreateCoreDatabaseStorage()));
            services.AddSingleton(sp => new StartupIntegrityService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<PersistenceService>()));
            return services;
        }
    }
}

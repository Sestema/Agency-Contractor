using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class DomainModule
    {
        public static IServiceCollection AddDomain(this IServiceCollection services)
        {
            services.AddSingleton(sp => new CompanyService(
                sp.GetRequiredService<TagCatalogService>(),
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<PersistenceService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<SyncEventService>()));
            services.AddSingleton(sp => new TemplateService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<TagCatalogService>()));
            services.AddSingleton<StarterTemplateCatalogService>();
            services.AddSingleton(sp => new AdminMirrorSyncService(
                sp.GetRequiredService<CompanyService>()));
            services.AddSingleton(sp => new EmployeeService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<TagCatalogService>(),
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<CurrentProfileService>(),
                sp.GetRequiredService<AdminMirrorSyncService>(),
                companyService: sp.GetRequiredService<CompanyService>(),
                storageFactory: sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton(sp => new RecentlyDeletedService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<EmployeeService>(),
                sp.GetRequiredService<CurrentProfileService>(),
                sp.GetRequiredService<FinanceService>(),
                sp.GetRequiredService<ActivityLogService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton(sp => new FinanceService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<SalaryDbService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<CompanyService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<SharedOperationLockService>(),
                suppressStartupNotifications: true,
                storageFactory: sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton(sp => new ActivityLogService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppDataStorageFactory>().CreateActivityLogStorage(),
                sp.GetRequiredService<CurrentProfileService>()));
            services.AddSingleton(sp => new AppStatisticsService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton(sp => new CandidateService(sp.GetRequiredService<FolderService>()));
            return services;
        }
    }
}

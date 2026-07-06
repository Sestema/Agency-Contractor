using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class PostgresModule
    {
        public static IServiceCollection AddPostgres(this IServiceCollection services)
        {
            services.AddSingleton<PostgresConnectionTestService>();
            services.AddSingleton(sp => new PostgresMigrationService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<CoreDbService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<SalaryDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<AppStatisticsService>()));
            services.AddSingleton<PostgresResetService>();
            services.AddSingleton<PostgresNetworkAccessService>();
            services.AddSingleton(sp => new PostgresToSqliteBackupService(
                sp.GetRequiredService<AppSettingsService>(),
                sp.GetRequiredService<CoreDbService>(),
                sp.GetRequiredService<LocalDbService>(),
                sp.GetRequiredService<SalaryDbService>(),
                sp.GetRequiredService<EmployeeIndexDbService>(),
                sp.GetRequiredService<AppStatisticsService>(),
                sp.GetRequiredService<AppDataStorageFactory>()));
            services.AddSingleton(sp => new DailySqliteBackupService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppDataStorageFactory>(),
                sp.GetRequiredService<PostgresToSqliteBackupService>()));
            return services;
        }
    }
}

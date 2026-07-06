using Microsoft.Extensions.DependencyInjection;

namespace Win11DesktopApp.DependencyInjection
{
    /// <summary>
    /// Single composition root for the desktop application. Each domain registers its
    /// services in a dedicated module so the registrations stay small and reviewable.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services) =>
            services
                .AddInfrastructure()
                .AddStorage()
                .AddPostgres()
                .AddSyncAndNotifications()
                .AddDomain()
                .AddScanning()
                .AddInvoices()
                .AddDocuments()
                .AddAuth()
                .AddAi()
                .AddTelegram()
                .AddPlatform()
                .AddViewModelFactories()
                .AddViewModels();
    }
}

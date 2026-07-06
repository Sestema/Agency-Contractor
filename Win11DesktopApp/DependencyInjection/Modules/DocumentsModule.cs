using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class DocumentsModule
    {
        public static IServiceCollection AddDocuments(this IServiceCollection services)
        {
            services.AddSingleton<DocumentGenerationService>();
            services.AddSingleton<DocumentLocalizationService>();
            services.AddSingleton<ReportColumnLayoutService>();
            return services;
        }
    }
}

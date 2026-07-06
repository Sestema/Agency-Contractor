using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class AiModule
    {
        public static IServiceCollection AddAi(this IServiceCollection services)
        {
            services.AddSingleton<ChatPersistenceService>();
            services.AddSingleton<GeminiApiService>();
            services.AddSingleton<NewsService>();
            services.AddSingleton<GeminiApiKeyConfigurationService>();
            return services;
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Telegram;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class TelegramModule
    {
        public static IServiceCollection AddTelegram(this IServiceCollection services)
        {
            services.AddSingleton<TelegramPairingService>();
            services.AddSingleton<TelegramBotService>();
            return services;
        }
    }
}

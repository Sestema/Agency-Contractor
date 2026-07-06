using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class InvoicesModule
    {
        public static IServiceCollection AddInvoices(this IServiceCollection services)
        {
            services.AddSingleton(sp => new InvoiceStorageService(
                sp.GetRequiredService<FolderService>(),
                sp.GetRequiredService<AppSettingsService>()));
            services.AddSingleton(sp => new AresLookupService(sp.GetRequiredService<InvoiceStorageService>()));
            services.AddSingleton<InvoiceQrPaymentService>();
            services.AddSingleton(sp => new InvoicePdfRenderService(
                sp.GetRequiredService<InvoiceStorageService>(),
                sp.GetRequiredService<InvoiceQrPaymentService>()));
            return services;
        }
    }
}

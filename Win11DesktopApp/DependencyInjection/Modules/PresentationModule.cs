using Microsoft.Extensions.DependencyInjection;
using Win11DesktopApp.Invoices.Services;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.DependencyInjection
{
    internal static class PresentationModule
    {
        public static IServiceCollection AddViewModelFactories(this IServiceCollection services)
        {
            services.AddSingleton<EmployeeDetailsViewModelFactory>();
            services.AddSingleton<AddEmployeeWizardViewModelFactory>();
            services.AddSingleton<AddCompanyViewModelFactory>();
            services.AddSingleton<CandidateViewModelFactory>();
            services.AddSingleton<TemplateViewModelFactory>();
            services.AddSingleton<InvoiceViewModelFactory>();
            services.AddSingleton<MainModuleViewModelFactory>();
            services.AddSingleton<FinanceModuleViewModelFactory>();
            services.AddSingleton<AiWindowFactory>();
            services.AddSingleton<ProfileDialogFactory>();
            services.AddSingleton<StartupDialogFactory>();
            return services;
        }

        public static IServiceCollection AddViewModels(this IServiceCollection services)
        {
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindowViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<DashboardViewModel>();
            services.AddTransient<ReportViewModel>();
            services.AddTransient<ActivityLogViewModel>();
            services.AddTransient<FinanceTablesViewModel>();
            services.AddTransient<CandidatesViewModel>();
            services.AddTransient<NewsViewModel>();
            return services;
        }
    }
}

using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Backup;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Infrastructure.Authentication;
using FinancialManagement.Infrastructure.Authorization;
using FinancialManagement.Infrastructure.Backup;
using FinancialManagement.Infrastructure.Clients;
using FinancialManagement.Infrastructure.CustomerAdvances;
using FinancialManagement.Infrastructure.CustomerPayments;
using FinancialManagement.Infrastructure.Dashboard;
using FinancialManagement.Infrastructure.Alerts;
using FinancialManagement.Infrastructure.Purchases;
using FinancialManagement.Infrastructure.Sales;
using FinancialManagement.Infrastructure.SupplierPayments;
using FinancialManagement.Infrastructure.SupplierAdvances;
using FinancialManagement.Infrastructure.Suppliers;
using FinancialManagement.Infrastructure.Treasury;
using FinancialManagement.Infrastructure.InitialCustomerReceivables;
using FinancialManagement.Infrastructure.InitialSupplierPayables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FinancialManagement.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FinancialManagementDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(SqlitePragmaConnectionInterceptor.Instance);
        });

        services.AddSingleton<CurrentUserSession>();
        services.AddSingleton<ICurrentUserSession>(serviceProvider =>
            serviceProvider.GetRequiredService<CurrentUserSession>());
        services.AddSingleton<IPasswordHasher, MicrosoftIdentityPasswordHasher>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IBusinessReferenceGenerator, BusinessReferenceGenerator>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IClientLookupService, ClientLookupService>();
        services.AddScoped<IPurchaseService, PurchaseService>();
        services.AddScoped<ISupplierLookupService, SupplierLookupService>();
        services.AddScoped<ICustomerPaymentService, CustomerPaymentService>();
        services.AddScoped<ISupplierPaymentService, SupplierPaymentService>();
        services.AddScoped<IInitialCustomerReceivableService, InitialCustomerReceivableService>();
        services.AddScoped<IInitialSupplierPayableService, InitialSupplierPayableService>();
        services.AddScoped<ICustomerAdvanceService, CustomerAdvanceService>();
        services.AddScoped<ISupplierAdvanceService, SupplierAdvanceService>();
        services.AddScoped<ITreasuryService, TreasuryService>();
        services.AddScoped<IFinancialDashboardService, FinancialDashboardService>();
        services.AddScoped<IFinancialDueAlertService, FinancialDueAlertService>();
        services.TryAddSingleton<DatabaseBackupOptions>();
        services.AddSingleton<DatabaseMaintenanceLock>();
        services.AddSingleton<DatabaseRestoreTestHook>();
        services.AddScoped<IDatabaseBackupService, DatabaseBackupService>();

        return services;
    }
}

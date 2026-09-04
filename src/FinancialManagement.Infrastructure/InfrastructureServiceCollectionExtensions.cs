using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Sales;
using FinancialManagement.Infrastructure.Authentication;
using FinancialManagement.Infrastructure.Authorization;
using FinancialManagement.Infrastructure.Sales;
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

        return services;
    }
}

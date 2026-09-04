using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Infrastructure.Authentication;
using FinancialManagement.Infrastructure.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        return services;
    }
}

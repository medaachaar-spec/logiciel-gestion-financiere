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

        return services;
    }
}

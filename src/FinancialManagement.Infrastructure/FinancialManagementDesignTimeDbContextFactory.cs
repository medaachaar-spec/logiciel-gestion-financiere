using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FinancialManagement.Infrastructure;

public sealed class FinancialManagementDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<FinancialManagementDbContext>
{
    public FinancialManagementDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FinancialManagementDbContext>()
            .UseSqlite("Data Source=:memory:")
            .AddInterceptors(SqlitePragmaConnectionInterceptor.Instance)
            .Options;

        return new FinancialManagementDbContext(options);
    }
}

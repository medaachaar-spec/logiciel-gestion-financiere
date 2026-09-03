using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

public sealed class FinancialManagementDbContext(
    DbContextOptions<FinancialManagementDbContext> options) : DbContext(options)
{
}

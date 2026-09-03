using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

public sealed class FinancialManagementDbContext(
    DbContextOptions<FinancialManagementDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();
    public DbSet<CustomerPaymentAllocation> CustomerPaymentAllocations => Set<CustomerPaymentAllocation>();
    public DbSet<InitialCustomerReceivable> InitialCustomerReceivables => Set<InitialCustomerReceivable>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<InitialSupplierPayable> InitialSupplierPayables => Set<InitialSupplierPayable>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<TreasuryOpeningBalance> TreasuryOpeningBalances => Set<TreasuryOpeningBalance>();
    public DbSet<TreasuryTransfer> TreasuryTransfers => Set<TreasuryTransfer>();
    public DbSet<ApplicationSettings> ApplicationSettings => Set<ApplicationSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinancialManagementDbContext).Assembly);
    }
}

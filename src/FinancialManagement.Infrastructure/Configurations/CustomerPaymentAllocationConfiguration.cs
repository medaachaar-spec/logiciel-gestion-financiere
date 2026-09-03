using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class CustomerPaymentAllocationConfiguration
    : IEntityTypeConfiguration<CustomerPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<CustomerPaymentAllocation> builder)
    {
        builder.ToTable(
            "CustomerPaymentAllocations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CustomerPaymentAllocations_AmountMinor_Positive",
                    "\"AmountMinor\" > 0");
                table.HasCheckConstraint(
                    "CK_CustomerPaymentAllocations_Target_Xor",
                    "(\"SaleId\" IS NOT NULL AND \"InitialCustomerReceivableId\" IS NULL) " +
                    "OR (\"SaleId\" IS NULL AND \"InitialCustomerReceivableId\" IS NOT NULL)");
            });
        builder.HasKey(allocation => allocation.Id);

        builder.HasIndex(allocation => allocation.CustomerPaymentId);
        builder.HasIndex(allocation => allocation.SaleId);
        builder.HasIndex(allocation => allocation.InitialCustomerReceivableId);

        builder.HasOne<CustomerPayment>()
            .WithMany(payment => payment.Allocations)
            .HasForeignKey(allocation => allocation.CustomerPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Sale>()
            .WithMany(sale => sale.CustomerPaymentAllocations)
            .HasForeignKey(allocation => allocation.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InitialCustomerReceivable>()
            .WithMany(receivable => receivable.CustomerPaymentAllocations)
            .HasForeignKey(allocation => allocation.InitialCustomerReceivableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

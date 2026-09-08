using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class SupplierPaymentAllocationConfiguration
    : IEntityTypeConfiguration<SupplierPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<SupplierPaymentAllocation> builder)
    {
        builder.ToTable(
            "SupplierPaymentAllocations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SupplierPaymentAllocations_AmountMinor_Positive",
                    "\"AmountMinor\" > 0");
                table.HasCheckConstraint(
                    "CK_SupplierPaymentAllocations_Target_Xor",
                    "(\"PurchaseId\" IS NOT NULL AND \"InitialSupplierPayableId\" IS NULL) " +
                    "OR (\"PurchaseId\" IS NULL AND \"InitialSupplierPayableId\" IS NOT NULL)");
            });
        builder.HasKey(allocation => allocation.Id);
        builder.ToTable("SupplierPaymentAllocations", table =>
        {
            table.HasCheckConstraint("CK_SupplierPaymentAllocations_AmountMinor_IntegerStorage",
                "typeof(\"AmountMinor\") = 'integer'");
        });

        builder.HasIndex(allocation => allocation.SupplierPaymentId);
        builder.HasIndex(allocation => allocation.PurchaseId);
        builder.HasIndex(allocation => allocation.InitialSupplierPayableId);

        builder.HasOne<SupplierPayment>()
            .WithMany(payment => payment.Allocations)
            .HasForeignKey(allocation => allocation.SupplierPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Purchase>()
            .WithMany(purchase => purchase.SupplierPaymentAllocations)
            .HasForeignKey(allocation => allocation.PurchaseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<InitialSupplierPayable>()
            .WithMany(payable => payable.SupplierPaymentAllocations)
            .HasForeignKey(allocation => allocation.InitialSupplierPayableId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

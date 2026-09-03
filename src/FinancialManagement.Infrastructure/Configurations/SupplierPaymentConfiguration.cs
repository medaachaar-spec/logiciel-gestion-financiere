using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> builder)
    {
        builder.ToTable(
            "SupplierPayments",
            table => table.HasCheckConstraint(
                "CK_SupplierPayments_AmountMinor_Positive",
                "\"AmountMinor\" > 0"));
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Reference).IsRequired();
        builder.Property(payment => payment.PaymentMethod).HasConversion<string>().IsRequired();

        builder.HasIndex(payment => payment.Reference).IsUnique();
        builder.HasIndex(payment => payment.SupplierId);
        builder.HasIndex(payment => payment.PaymentDate);

        builder.HasOne<Supplier>()
            .WithMany(supplier => supplier.SupplierPayments)
            .HasForeignKey(payment => payment.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(payment => payment.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

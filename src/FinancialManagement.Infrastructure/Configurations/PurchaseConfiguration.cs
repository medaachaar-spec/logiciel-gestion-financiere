using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable(
            "Purchases",
            table => table.HasCheckConstraint(
                "CK_Purchases_TotalAmountMinor_Positive",
                "\"TotalAmountMinor\" > 0"));
        builder.HasKey(purchase => purchase.Id);

        builder.Property(purchase => purchase.Reference).IsRequired();

        builder.HasIndex(purchase => purchase.Reference).IsUnique();
        builder.HasIndex(purchase => purchase.SupplierId);
        builder.HasIndex(purchase => purchase.PurchaseDate);
        builder.HasIndex(purchase => purchase.DueDate);

        builder.HasOne<Supplier>()
            .WithMany(supplier => supplier.Purchases)
            .HasForeignKey(purchase => purchase.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(purchase => purchase.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

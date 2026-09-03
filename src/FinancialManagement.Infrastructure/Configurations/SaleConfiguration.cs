using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable(
            "Sales",
            table => table.HasCheckConstraint(
                "CK_Sales_TotalAmountMinor_Positive",
                "\"TotalAmountMinor\" > 0"));
        builder.HasKey(sale => sale.Id);

        builder.Property(sale => sale.Reference).IsRequired();

        builder.HasIndex(sale => sale.Reference).IsUnique();
        builder.HasIndex(sale => sale.ClientId);
        builder.HasIndex(sale => sale.SaleDate);
        builder.HasIndex(sale => sale.DueDate);

        builder.HasOne<Client>()
            .WithMany(client => client.Sales)
            .HasForeignKey(sale => sale.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(sale => sale.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

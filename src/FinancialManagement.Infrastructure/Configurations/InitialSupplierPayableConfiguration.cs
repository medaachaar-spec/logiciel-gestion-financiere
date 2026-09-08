using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class InitialSupplierPayableConfiguration
    : IEntityTypeConfiguration<InitialSupplierPayable>
{
    public void Configure(EntityTypeBuilder<InitialSupplierPayable> builder)
    {
        builder.ToTable(
            "InitialSupplierPayables",
            table => table.HasCheckConstraint(
                "CK_InitialSupplierPayables_InitialAmountMinor_Positive",
                "\"InitialAmountMinor\" > 0"));
        builder.HasKey(payable => payable.Id);
        builder.ToTable("InitialSupplierPayables", table =>
        {
            table.HasCheckConstraint("CK_InitialSupplierPayables_InitialAmountMinor_IntegerStorage",
                "typeof(\"InitialAmountMinor\") = 'integer'");
        });

        builder.Property(payable => payable.Reference).IsRequired();

        builder.HasIndex(payable => payable.Reference).IsUnique();
        builder.HasIndex(payable => payable.SupplierId);
        builder.HasIndex(payable => payable.DueDate);

        builder.HasOne<Supplier>()
            .WithMany(supplier => supplier.InitialSupplierPayables)
            .HasForeignKey(payable => payable.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(payable => payable.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

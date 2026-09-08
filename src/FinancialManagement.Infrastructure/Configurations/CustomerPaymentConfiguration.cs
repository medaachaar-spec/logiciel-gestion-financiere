using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
{
    public void Configure(EntityTypeBuilder<CustomerPayment> builder)
    {
        builder.ToTable(
            "CustomerPayments",
            table => table.HasCheckConstraint(
                "CK_CustomerPayments_AmountMinor_Positive",
                "\"AmountMinor\" > 0"));
        builder.HasKey(payment => payment.Id);
        builder.ToTable("CustomerPayments", table =>
        {
            table.HasCheckConstraint("CK_CustomerPayments_AmountMinor_IntegerStorage",
                "typeof(\"AmountMinor\") = 'integer'");
        });

        builder.Property(payment => payment.Reference).IsRequired();
        builder.Property(payment => payment.PaymentMethod).HasConversion<string>().IsRequired();

        builder.HasIndex(payment => payment.Reference).IsUnique();
        builder.HasIndex(payment => payment.ClientId);
        builder.HasIndex(payment => payment.PaymentDate);

        builder.HasOne<Client>()
            .WithMany(client => client.CustomerPayments)
            .HasForeignKey(payment => payment.ClientId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(payment => payment.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class InitialCustomerReceivableConfiguration
    : IEntityTypeConfiguration<InitialCustomerReceivable>
{
    public void Configure(EntityTypeBuilder<InitialCustomerReceivable> builder)
    {
        builder.ToTable(
            "InitialCustomerReceivables",
            table => table.HasCheckConstraint(
                "CK_InitialCustomerReceivables_InitialAmountMinor_Positive",
                "\"InitialAmountMinor\" > 0"));
        builder.HasKey(receivable => receivable.Id);

        builder.Property(receivable => receivable.Reference).IsRequired();

        builder.HasIndex(receivable => receivable.Reference).IsUnique();
        builder.HasIndex(receivable => receivable.ClientId);
        builder.HasIndex(receivable => receivable.DueDate);

        builder.HasOne<Client>()
            .WithMany(client => client.InitialCustomerReceivables)
            .HasForeignKey(receivable => receivable.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(receivable => receivable.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

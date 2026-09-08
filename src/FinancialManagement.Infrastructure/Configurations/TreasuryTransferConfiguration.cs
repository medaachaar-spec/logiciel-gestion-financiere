using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class TreasuryTransferConfiguration : IEntityTypeConfiguration<TreasuryTransfer>
{
    public void Configure(EntityTypeBuilder<TreasuryTransfer> builder)
    {
        builder.ToTable(
            "TreasuryTransfers",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_TreasuryTransfers_AmountMinor_Positive",
                    "\"AmountMinor\" > 0");
                table.HasCheckConstraint(
                    "CK_TreasuryTransfers_DistinctAccounts",
                    "\"FromAccount\" <> \"ToAccount\"");
            });
        builder.HasKey(transfer => transfer.Id);
        builder.ToTable("TreasuryTransfers", table =>
        {
            table.HasCheckConstraint("CK_TreasuryTransfers_AmountMinor_IntegerStorage",
                "typeof(\"AmountMinor\") = 'integer'");
        });

        builder.Property(transfer => transfer.Reference).IsRequired();
        builder.Property(transfer => transfer.FromAccount).HasConversion<string>().IsRequired();
        builder.Property(transfer => transfer.ToAccount).HasConversion<string>().IsRequired();

        builder.HasIndex(transfer => transfer.Reference).IsUnique();
        builder.HasIndex(transfer => transfer.TransferDate);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(transfer => transfer.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

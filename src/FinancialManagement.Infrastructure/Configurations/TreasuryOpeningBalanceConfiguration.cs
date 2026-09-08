using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class TreasuryOpeningBalanceConfiguration
    : IEntityTypeConfiguration<TreasuryOpeningBalance>
{
    public void Configure(EntityTypeBuilder<TreasuryOpeningBalance> builder)
    {
        builder.ToTable("TreasuryOpeningBalance");
        builder.HasKey(balance => balance.Id);
        builder.ToTable("TreasuryOpeningBalance", table =>
        {
            table.HasCheckConstraint("CK_TreasuryOpeningBalance_CashAmountMinor_IntegerStorage",
                "typeof(\"CashAmountMinor\") = 'integer'");
            table.HasCheckConstraint("CK_TreasuryOpeningBalance_BankAmountMinor_IntegerStorage",
                "typeof(\"BankAmountMinor\") = 'integer'");
        });

        builder.HasIndex(balance => balance.OpeningDate);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(balance => balance.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

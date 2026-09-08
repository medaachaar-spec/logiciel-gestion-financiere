using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable(
            "Expenses",
            table => table.HasCheckConstraint(
                "CK_Expenses_AmountMinor_Positive",
                "\"AmountMinor\" > 0"));
        builder.HasKey(expense => expense.Id);
        builder.ToTable("Expenses", table =>
        {
            table.HasCheckConstraint("CK_Expenses_AmountMinor_IntegerStorage",
                "typeof(\"AmountMinor\") = 'integer'");
        });

        builder.Property(expense => expense.Reference).IsRequired();
        builder.Property(expense => expense.PaymentMethod).HasConversion<string>().IsRequired();

        builder.HasIndex(expense => expense.Reference).IsUnique();
        builder.HasIndex(expense => expense.ExpenseCategoryId);
        builder.HasIndex(expense => expense.ExpenseDate);
        builder.HasIndex(expense => expense.ConcernedEmployeeUserId);

        builder.HasOne<ExpenseCategory>()
            .WithMany(category => category.Expenses)
            .HasForeignKey(expense => expense.ExpenseCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(expense => expense.ConcernedEmployeeUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(expense => expense.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

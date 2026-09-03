using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class ExpenseCategoryConfiguration : IEntityTypeConfiguration<ExpenseCategory>
{
    public void Configure(EntityTypeBuilder<ExpenseCategory> builder)
    {
        builder.ToTable("ExpenseCategories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).IsRequired();

        builder.HasIndex(category => category.Name).IsUnique();

        var createdAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new ExpenseCategory
            {
                Id = 1,
                Name = "Salaires",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            },
            new ExpenseCategory
            {
                Id = 2,
                Name = "Transport",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            },
            new ExpenseCategory
            {
                Id = 3,
                Name = "Loyer",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            },
            new ExpenseCategory
            {
                Id = 4,
                Name = "Factures",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            },
            new ExpenseCategory
            {
                Id = 5,
                Name = "Divers",
                IsActive = true,
                CreatedAtUtc = createdAtUtc
            });
    }
}

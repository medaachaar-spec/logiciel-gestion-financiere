using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FinancialManagement.Infrastructure.Configurations;

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> builder)
    {
        builder.ToTable("UserPermissions");
        builder.HasKey(permission => permission.Id);

        builder.Property(permission => permission.Module).HasConversion<string>().IsRequired();

        builder.HasIndex(permission => new { permission.UserId, permission.Module }).IsUnique();

        builder.HasOne<User>()
            .WithMany(user => user.UserPermissions)
            .HasForeignKey(permission => permission.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

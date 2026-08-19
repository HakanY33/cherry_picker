using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Position).HasMaxLength(100);
        builder.Property(x => x.ExternalId).HasMaxLength(100);
        builder.Property(x => x.PasswordHash).HasMaxLength(255);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.UserName).IsUnique();

        builder.HasIndex(x => x.FirmId)
            .HasDatabaseName("IX_Users_Firm")
            .HasFilter("[FirmId] IS NOT NULL");

        builder.HasOne(x => x.Department)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Firm)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    public void Configure(EntityTypeBuilder<Firm> builder)
    {
        builder.ToTable("Firms");
        builder.HasKey(x => x.FirmId);

        builder.Property(x => x.Code).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TaxNumber).HasMaxLength(20);
        builder.Property(x => x.TaxOffice).HasMaxLength(100);
        builder.Property(x => x.Iban).HasMaxLength(34);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasMany(x => x.Users)
            .WithOne(x => x.Firm)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Equipment)
            .WithOne(x => x.Firm)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Contracts)
            .WithOne(x => x.Firm)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Requests)
            .WithOne(x => x.Firm)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.WorkRecords)
            .WithOne(x => x.Firm)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

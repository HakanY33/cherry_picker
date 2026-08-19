using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ServiceVariantConfiguration : IEntityTypeConfiguration<ServiceVariant>
{
    public void Configure(EntityTypeBuilder<ServiceVariant> builder)
    {
        builder.ToTable("ServiceVariants");
        builder.HasKey(x => x.VariantId);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Capacity).HasMaxLength(50);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.ServiceId, x.Code })
            .IsUnique()
            .HasDatabaseName("UQ_Variant");

        builder.HasOne(x => x.ServiceCategory)
            .WithMany(x => x.ServiceVariants)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            new ServiceVariant { VariantId = 1, ServiceId = 1, Code = "30T_SEPETLI", Name = "30 Ton Sepetli", IsActive = true },
            new ServiceVariant { VariantId = 2, ServiceId = 1, Code = "60T_SEPETLI", Name = "60 Ton Sepetli", IsActive = true }
        );
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("ServiceCategories", t =>
            t.HasCheckConstraint("CK_Service_Unit", "[Unit] IN ('HOUR','DAY','SHIFT','METER','PIECE')"));
        builder.HasKey(x => x.ServiceId);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Unit)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.RequiresTimeTracking).HasDefaultValue(false);
        builder.Property(x => x.RequiresVehicle).HasDefaultValue(false);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasData(
            new ServiceCategory
            {
                ServiceId = 1,
                Code = "CHERRY_PICKER",
                Name = "Mobil Vinç",
                Unit = ServiceUnit.HOUR,
                RequiresTimeTracking = true,
                RequiresVehicle = true,
                IsActive = true
            }
        );
    }
}

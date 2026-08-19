using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ContractLineSurchargeConfiguration : IEntityTypeConfiguration<ContractLineSurcharge>
{
    public void Configure(EntityTypeBuilder<ContractLineSurcharge> builder)
    {
        builder.ToTable("ContractLineSurcharges");
        builder.HasKey(x => x.SurchargeId);

        builder.Property(x => x.SurchargeType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.ContractLine)
            .WithMany(x => x.ContractLineSurcharges)
            .HasForeignKey(x => x.ContractLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

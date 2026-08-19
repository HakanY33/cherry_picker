using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class ContractLineConfiguration : IEntityTypeConfiguration<ContractLine>
{
    public void Configure(EntityTypeBuilder<ContractLine> builder)
    {
        builder.ToTable("ContractLines", t =>
            t.HasCheckConstraint("CK_Rounding",
                "[RoundingRule] IN ('NONE','UP_15','UP_30','UP_60','NEAREST_15','NEAREST_30','NEAREST_60')"));
        builder.HasKey(x => x.ContractLineId);

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired().HasDefaultValue("TRY");
        builder.Property(x => x.RoundingRule)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(RoundingRule.NONE);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.ContractId, x.ServiceId, x.VariantId, x.ValidFrom, x.ValidTo })
            .HasDatabaseName("IX_ContractLines_Lookup");

        builder.HasOne(x => x.Contract)
            .WithMany(x => x.ContractLines)
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ServiceCategory)
            .WithMany(x => x.ContractLines)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ServiceVariant)
            .WithMany(x => x.ContractLines)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedContractLines)
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

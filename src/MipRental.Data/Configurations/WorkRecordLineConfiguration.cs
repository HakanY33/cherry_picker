using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class WorkRecordLineConfiguration : IEntityTypeConfiguration<WorkRecordLine>
{
    public void Configure(EntityTypeBuilder<WorkRecordLine> builder)
    {
        builder.ToTable("WorkRecordLines");
        builder.HasKey(x => x.WorkRecordLineId);

        builder.Property(x => x.LineNo).HasDefaultValue(1);
        builder.Property(x => x.Unit)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.PricingRuleSnapshot).HasColumnType("nvarchar(max)");
        builder.Property(x => x.SurchargeAmount).HasDefaultValue(0m);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired().HasDefaultValue("TRY");
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.OverrideReason).HasMaxLength(500);
        builder.Property(x => x.IsManualOverride).HasDefaultValue(false);

        builder.HasIndex(x => x.WorkRecordId)
            .HasDatabaseName("IX_WorkRecordLines_Record");

        // Mali kayıt: zincirleme silme kesinlikle kapalı.
        builder.HasOne(x => x.WorkRecord)
            .WithMany(x => x.WorkRecordLines)
            .HasForeignKey(x => x.WorkRecordId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ServiceCategory)
            .WithMany(x => x.WorkRecordLines)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ServiceVariant)
            .WithMany(x => x.WorkRecordLines)
            .HasForeignKey(x => x.VariantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ContractLine)
            .WithMany(x => x.WorkRecordLines)
            .HasForeignKey(x => x.ContractLineId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

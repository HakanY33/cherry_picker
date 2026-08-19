using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class IntegrationQueueConfiguration : IEntityTypeConfiguration<IntegrationQueue>
{
    public void Configure(EntityTypeBuilder<IntegrationQueue> builder)
    {
        builder.ToTable("IntegrationQueue");
        builder.HasKey(x => x.QueueId);

        builder.Property(x => x.TargetSystem).HasMaxLength(30).IsRequired().HasDefaultValue("ORACLE");
        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.Payload).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(IntegrationQueueStatus.PENDING);
        builder.Property(x => x.ExternalRef).HasMaxLength(100);
        builder.Property(x => x.ErrorMessage).HasColumnType("nvarchar(max)");
        builder.Property(x => x.RetryCount).HasDefaultValue(0);
    }
}

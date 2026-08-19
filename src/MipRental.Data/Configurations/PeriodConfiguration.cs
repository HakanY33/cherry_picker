using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class PeriodConfiguration : IEntityTypeConfiguration<Period>
{
    public void Configure(EntityTypeBuilder<Period> builder)
    {
        builder.ToTable("Periods", t =>
            t.HasCheckConstraint("CK_Month", "[Month] BETWEEN 1 AND 12"));
        builder.HasKey(x => x.PeriodId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PeriodStatus.OPEN);
        builder.Property(x => x.ReopenReason).HasMaxLength(500);

        builder.HasIndex(x => new { x.Year, x.Month })
            .IsUnique()
            .HasDatabaseName("UQ_Period");

        builder.HasOne(x => x.ClosedByUser)
            .WithMany(x => x.ClosedPeriods)
            .HasForeignKey(x => x.ClosedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReopenedByUser)
            .WithMany(x => x.ReopenedPeriods)
            .HasForeignKey(x => x.ReopenedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasData(
            Enumerable.Range(1, 12).Select(month => new Period
            {
                PeriodId = month,
                Year = 2026,
                Month = month,
                Status = PeriodStatus.OPEN
            })
        );
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class WorkRecordConfiguration : IEntityTypeConfiguration<WorkRecord>
{
    public void Configure(EntityTypeBuilder<WorkRecord> builder)
    {
        builder.ToTable("WorkRecords");
        builder.HasKey(x => x.WorkRecordId);

        builder.Property(x => x.DocumentNo).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(WorkRecordStatus.DRAFT);
        builder.Property(x => x.IntegrationStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(WorkRecordIntegrationStatus.NOT_SENT);
        builder.Property(x => x.LocationText).HasMaxLength(300);
        builder.Property(x => x.WorkDescription).HasMaxLength(1000);
        builder.Property(x => x.OperatorName).HasMaxLength(150);
        builder.Property(x => x.LicensePlate).HasMaxLength(20);
        builder.Property(x => x.ExternalReceiptNo).HasMaxLength(40);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.RevisionReason).HasMaxLength(500);
        builder.Property(x => x.SpansMidnight).HasDefaultValue(false);
        builder.Property(x => x.IsSuperseded).HasDefaultValue(false);

        builder.HasIndex(x => x.DocumentNo).IsUnique();
        builder.HasIndex(x => new { x.PeriodId, x.FirmId, x.Status })
            .HasDatabaseName("IX_WorkRecords_Period");
        builder.HasIndex(x => new { x.WorkDate, x.FirmId })
            .HasDatabaseName("IX_WorkRecords_Date");
        builder.HasIndex(x => new { x.FirmId, x.WorkDate, x.LicensePlate, x.StartTime })
            .HasDatabaseName("IX_WorkRecords_DupCheck");

        builder.HasOne(x => x.Request)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Firm)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Contract)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Period)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Location)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RequestedByUser)
            .WithMany(x => x.WorkRecordsRequested)
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.WitnessedByUser)
            .WithMany(x => x.WorkRecordsWitnessed)
            .HasForeignKey(x => x.WitnessedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Equipment)
            .WithMany(x => x.WorkRecords)
            .HasForeignKey(x => x.EquipmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.EnteredByUser)
            .WithMany(x => x.WorkRecordsEntered)
            .HasForeignKey(x => x.EnteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RevisionOf)
            .WithMany(x => x.Revisions)
            .HasForeignKey(x => x.RevisionOfId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

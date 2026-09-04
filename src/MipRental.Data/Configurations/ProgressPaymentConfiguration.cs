using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class ProgressPaymentConfiguration : IEntityTypeConfiguration<ProgressPayment>
{
    public void Configure(EntityTypeBuilder<ProgressPayment> builder)
    {
        builder.ToTable("ProgressPayments");
        builder.HasKey(x => x.ProgressPaymentId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(ProgressPaymentStatus.DRAFT);

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.BudgetNote).HasMaxLength(2000);
        builder.Property(x => x.ManagerNote).HasMaxLength(2000);
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);

        // A3 — bir dönem + bir firma için TEK hakediş. Garanti veritabanında:
        // uygulama katmanındaki "önce var mı diye bak" kontrolü iki paralel
        // istekte de "yok" görebilir (ADR-027 ile aynı gerekçe).
        builder.HasIndex(x => new { x.PeriodId, x.FirmId })
            .IsUnique()
            .HasDatabaseName("UQ_ProgressPayments_Period_Firm");

        builder.HasOne(x => x.Period)
            .WithMany()
            .HasForeignKey(x => x.PeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Firm)
            .WithMany()
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BudgetApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.BudgetApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ManagerApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.ManagerApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class ProgressPaymentRecordConfiguration : IEntityTypeConfiguration<ProgressPaymentRecord>
{
    public void Configure(EntityTypeBuilder<ProgressPaymentRecord> builder)
    {
        builder.ToTable("ProgressPaymentRecords");
        builder.HasKey(x => x.ProgressPaymentRecordId);

        // Aynı kayıt aynı hakedişe iki kez giremez.
        builder.HasIndex(x => new { x.ProgressPaymentId, x.WorkRecordId })
            .IsUnique()
            .HasDatabaseName("UQ_ProgressPaymentRecords_Record");

        // Bir çalışma kaydı yalnızca TEK hakedişe girebilir: aynı kaydın iki
        // hakedişte sayılması çift ödeme demektir.
        builder.HasIndex(x => x.WorkRecordId)
            .IsUnique()
            .HasDatabaseName("UQ_ProgressPaymentRecords_WorkRecord");

        builder.HasOne(x => x.ProgressPayment)
            .WithMany(x => x.Records)
            .HasForeignKey(x => x.ProgressPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.WorkRecord)
            .WithMany()
            .HasForeignKey(x => x.WorkRecordId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

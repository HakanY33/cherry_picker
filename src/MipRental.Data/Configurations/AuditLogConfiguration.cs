using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(x => x.AuditId);

        builder.Property(x => x.TableName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(x => x.FieldName).HasMaxLength(100);
        builder.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.IpAddress).HasMaxLength(45);

        builder.HasIndex(x => new { x.TableName, x.RecordId, x.OccurredAt })
            .HasDatabaseName("IX_Audit_Record");

        // Islak imzanin yerini alan tek sey: zincirleme silme kesinlikle kapalı.
        builder.HasOne(x => x.User)
            .WithMany(x => x.AuditLogEntries)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

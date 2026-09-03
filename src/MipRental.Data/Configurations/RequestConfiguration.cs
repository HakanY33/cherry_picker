using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("Requests");
        builder.HasKey(x => x.RequestId);

        builder.Property(x => x.DocumentNo).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired()
            .HasDefaultValue(RequestStatus.DRAFT);
        builder.Property(x => x.LocationText).HasMaxLength(300);
        builder.Property(x => x.WorkDescription).HasMaxLength(1000);
        builder.Property(x => x.Notes).HasMaxLength(1000);

        // Adım 10 — talep akışı alanları.
        builder.Property(x => x.AssignedOperatorName).HasMaxLength(200);
        builder.Property(x => x.AssignedLicensePlate).HasMaxLength(20);
        builder.Property(x => x.RejectionReason).HasMaxLength(500);
        builder.Property(x => x.CancellationReason).HasMaxLength(500);

        builder.HasIndex(x => x.DocumentNo).IsUnique();
        builder.HasIndex(x => new { x.Status, x.RequestedDate })
            .HasDatabaseName("IX_Requests_Status");

        builder.HasOne(x => x.RequestedByUser)
            .WithMany(x => x.RequestsCreated)
            .HasForeignKey(x => x.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Department)
            .WithMany(x => x.Requests)
            .HasForeignKey(x => x.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Firm)
            .WithMany(x => x.Requests)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Location)
            .WithMany(x => x.Requests)
            .HasForeignKey(x => x.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

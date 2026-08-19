using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ApprovalConfiguration : IEntityTypeConfiguration<Approval>
{
    public void Configure(EntityTypeBuilder<Approval> builder)
    {
        builder.ToTable("Approvals");
        builder.HasKey(x => x.ApprovalId);

        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.Decision)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(x => x.Comment).HasMaxLength(1000);

        builder.HasIndex(x => new { x.DocumentType, x.DocumentId, x.StepNo })
            .HasDatabaseName("IX_Approvals_Doc");

        builder.HasIndex(x => new { x.AssignedToUserId, x.Decision })
            .HasDatabaseName("IX_Approvals_Pending")
            .HasFilter("[Decision] IS NULL");

        builder.HasOne(x => x.ApprovalFlowStep)
            .WithMany(x => x.Approvals)
            .HasForeignKey(x => x.FlowStepId)
            .OnDelete(DeleteBehavior.Restrict);

        // Mali kayda bağlı iz: zincirleme silme kesinlikle kapalı.
        builder.HasOne(x => x.AssignedToUser)
            .WithMany(x => x.ApprovalsAssigned)
            .HasForeignKey(x => x.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AssignedToRole)
            .WithMany(x => x.ApprovalsAssigned)
            .HasForeignKey(x => x.AssignedToRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DecidedByUser)
            .WithMany(x => x.ApprovalsDecided)
            .HasForeignKey(x => x.DecidedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

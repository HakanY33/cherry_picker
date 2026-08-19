using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ApprovalFlowStepConfiguration : IEntityTypeConfiguration<ApprovalFlowStep>
{
    public void Configure(EntityTypeBuilder<ApprovalFlowStep> builder)
    {
        builder.ToTable("ApprovalFlowSteps");
        builder.HasKey(x => x.FlowStepId);

        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.IsMandatory).HasDefaultValue(true);

        builder.HasIndex(x => new { x.FlowId, x.StepNo })
            .IsUnique()
            .HasDatabaseName("UQ_FlowStep");

        builder.HasOne(x => x.ApprovalFlow)
            .WithMany(x => x.ApprovalFlowSteps)
            .HasForeignKey(x => x.FlowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Role)
            .WithMany(x => x.ApprovalFlowSteps)
            .HasForeignKey(x => x.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

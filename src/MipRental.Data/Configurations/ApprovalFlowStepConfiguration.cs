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

        // Varsayılan akışın adımları: 1) Ekipman Müdürlüğü Yöneticisi, 2) Bütçe Yöneticisi.
        // RoleId'ler RoleConfiguration.HasData ile sabittir (2 = EQUIPMENT_MANAGER, 3 = BUDGET_MANAGER).
        // Adım 10'da bu iki rolün KODU değişti, RoleId değişmedi — zincir olduğu gibi çalışır.
        // AmountThreshold burada NULL: her iki adım da tutardan bağımsız çalışır.
        // Eşik istenirse sadece bu satıra değer yazmak yeterli (kural 6).
        builder.HasData(
            new ApprovalFlowStep
            {
                FlowStepId = 1,
                FlowId = 1,
                StepNo = 1,
                RoleId = 2,
                Name = "Amir Onayı",
                IsMandatory = true,
                AmountThreshold = null,
                ReminderAfterHours = 24,
                EscalateAfterHours = 48
            },
            new ApprovalFlowStep
            {
                FlowStepId = 2,
                FlowId = 1,
                StepNo = 2,
                RoleId = 3,
                Name = "Bütçe Yöneticisi Onayı",
                IsMandatory = true,
                AmountThreshold = null,
                ReminderAfterHours = 24,
                EscalateAfterHours = 48
            });
    }
}

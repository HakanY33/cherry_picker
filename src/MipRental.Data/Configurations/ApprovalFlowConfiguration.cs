using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class ApprovalFlowConfiguration : IEntityTypeConfiguration<ApprovalFlow>
{
    public void Configure(EntityTypeBuilder<ApprovalFlow> builder)
    {
        builder.ToTable("ApprovalFlows");
        builder.HasKey(x => x.FlowId);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.Code).IsUnique();

        builder.HasOne(x => x.ServiceCategory)
            .WithMany(x => x.ApprovalFlows)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // CLAUDE.md kural 6: zincir VERİDEN okunur. Varsayılan akış (ServiceId = null)
        // tüm çalışma kayıtlarına uygulanır. Toplantıdan farklı bir karar çıkarsa
        // SADECE bu veri ve ApprovalFlowSteps değişir — kodda hiçbir şey değişmez.
        builder.HasData(new ApprovalFlow
        {
            FlowId = 1,
            Code = "WR-DEFAULT",
            Name = "Çalışma Kaydı Varsayılan Onay Akışı",
            DocumentType = DocumentType.WORK_RECORD,
            ServiceId = null,
            IsActive = true
        });
    }
}

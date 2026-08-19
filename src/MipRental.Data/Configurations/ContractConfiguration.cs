using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts");
        builder.HasKey(x => x.ContractId);

        builder.Property(x => x.ContractNo).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired().HasDefaultValue("TRY");
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(ContractStatus.ACTIVE)
            // DRAFT (0) enum'un CLR varsayılanıyla çakışıyor; sentinel olmadan
            // Status = DRAFT atanan bir Contract yanlışlıkla DB varsayılanı olan
            // ACTIVE ile INSERT edilir. Gerçek bir değer olmayan -1 sentinel yapılır.
            .HasSentinel((ContractStatus)(-1));
        builder.Property(x => x.Notes).HasMaxLength(1000);

        builder.HasIndex(x => new { x.FirmId, x.ContractNo })
            .IsUnique()
            .HasDatabaseName("UQ_Contract");

        builder.HasOne(x => x.Firm)
            .WithMany(x => x.Contracts)
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.CreatedContracts)
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class GeneratedDocumentConfiguration : IEntityTypeConfiguration<GeneratedDocument>
{
    public void Configure(EntityTypeBuilder<GeneratedDocument> builder)
    {
        builder.ToTable("GeneratedDocuments");
        builder.HasKey(x => x.GeneratedDocumentId);

        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VerificationCode).HasMaxLength(40);
        builder.Property(x => x.TemplateVersion).HasMaxLength(20);
        builder.Property(x => x.Currency).HasMaxLength(3);

        // Doğrulama kodu açık bir sayfadan (AllowAnonymous) sorgulanır; iki belgeye
        // aynı kod düşerse doğrulama anlamını yitirir. Filtreli benzersiz index:
        // kodu olmayan (eski) satırlar kısıtlamaya takılmaz.
        builder.HasIndex(x => x.VerificationCode)
            .IsUnique()
            .HasFilter("[VerificationCode] IS NOT NULL");

        // Bir belgenin tüm sürümlerini üretim sırasına göre çekmek için.
        builder.HasIndex(x => new { x.DocumentType, x.DocumentId, x.Kind, x.GeneratedAt });

        builder.HasOne(x => x.Firm)
            .WithMany()
            .HasForeignKey(x => x.FirmId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.GeneratedByUser)
            .WithMany(x => x.GeneratedDocuments)
            .HasForeignKey(x => x.GeneratedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

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

        builder.HasOne(x => x.GeneratedByUser)
            .WithMany(x => x.GeneratedDocuments)
            .HasForeignKey(x => x.GeneratedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

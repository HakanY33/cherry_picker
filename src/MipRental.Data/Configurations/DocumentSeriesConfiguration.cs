using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;
using MipRental.Domain.Enums;

namespace MipRental.Data.Configurations;

public class DocumentSeriesConfiguration : IEntityTypeConfiguration<DocumentSeries>
{
    public void Configure(EntityTypeBuilder<DocumentSeries> builder)
    {
        builder.ToTable("DocumentSeries");
        builder.HasKey(x => x.SeriesId);

        builder.Property(x => x.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Padding).HasDefaultValue(5);
        builder.Property(x => x.LastNumber).HasDefaultValue(0);

        builder.HasIndex(x => new { x.DocumentType, x.Year })
            .IsUnique()
            .HasDatabaseName("UQ_Series");

        builder.HasData(
            new DocumentSeries { SeriesId = 1, DocumentType = DocumentType.WORK_RECORD, Prefix = "WR", Year = 2026, LastNumber = 0, Padding = 5 },
            new DocumentSeries { SeriesId = 2, DocumentType = DocumentType.REQUEST, Prefix = "CPR", Year = 2026, LastNumber = 0, Padding = 5 }
        );
    }
}

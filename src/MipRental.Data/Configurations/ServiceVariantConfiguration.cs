using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ServiceVariantConfiguration : IEntityTypeConfiguration<ServiceVariant>
{
    public void Configure(EntityTypeBuilder<ServiceVariant> builder)
    {
        builder.ToTable("ServiceVariants");
        builder.HasKey(x => x.VariantId);

        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Capacity).HasMaxLength(50);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => new { x.ServiceId, x.Code })
            .IsUnique()
            .HasDatabaseName("UQ_Variant");

        builder.HasOne(x => x.ServiceCategory)
            .WithMany(x => x.ServiceVariants)
            .HasForeignKey(x => x.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Ekrandaki ad MIP'in Excel formundaki yazımıyla birebir aynıdır
        // ("30 Ton / Sepetli"); form doldurulurken kolon eşleştirmesi göz kararı
        // yapılıyor, farklı yazım o eşleştirmeyi bozar.
        //
        // Yalnızca bu İKİSİ HasData ile gelir. 30T_KANCALI / 60T_KANCALI /
        // 120T_KANCALI / 200T varyantları Adım 18 migration'ında "yoksa ekle"
        // SQL'i ile eklenir: HasData SABİT VariantId ister, oysa geliştirme
        // veritabanlarında 3 ve sonrası ekrandan eklenmiş satırlarca dolu
        // olabilir (öyle de oldu) — sabit id çakışırdı.
        builder.HasData(
            new ServiceVariant { VariantId = 1, ServiceId = 1, Code = "30T_SEPETLI", Name = "30 Ton / Sepetli", IsActive = true },
            new ServiceVariant { VariantId = 2, ServiceId = 1, Code = "60T_SEPETLI", Name = "60 Ton / Sepetli", IsActive = true }
        );
    }
}

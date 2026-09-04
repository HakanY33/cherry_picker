using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MipRental.Domain.Entities;

namespace MipRental.Data.Configurations;

public class ApprovalTokenConfiguration : IEntityTypeConfiguration<ApprovalToken>
{
    public void Configure(EntityTypeBuilder<ApprovalToken> builder)
    {
        builder.ToTable("ApprovalTokens");
        builder.HasKey(x => x.ApprovalTokenId);

        // Hash SABİT 32 bayt (SHA-256). Ham token burada DEĞİL — hiçbir yerde.
        builder.Property(x => x.TokenHash)
            .HasColumnType("varbinary(32)")
            .IsRequired();

        builder.Property(x => x.UsedFromIp).HasMaxLength(45);        // IPv6 dahil
        builder.Property(x => x.UsedUserAgent).HasMaxLength(400);

        // Arama HASH ile yapılır; benzersizlik hem çakışmayı hem de aynı hash'in
        // iki satıra yazılmasını engeller.
        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("UQ_ApprovalTokens_Hash");

        builder.HasIndex(x => x.ProgressPaymentId)
            .HasDatabaseName("IX_ApprovalTokens_Payment");

        builder.HasOne(x => x.ProgressPayment)
            .WithMany(x => x.ApprovalTokens)
            .HasForeignKey(x => x.ProgressPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.IssuedToUser)
            .WithMany()
            .HasForeignKey(x => x.IssuedToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
